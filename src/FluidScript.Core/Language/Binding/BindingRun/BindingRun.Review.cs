using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- step 10: validate --------------------------------------------------------------------------

    private void Validate()
    {
        var mentioned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in _sourceConnections)
        {
            mentioned.Add(connection.From.Component);
            mentioned.Add(connection.To.Component);
        }

        foreach (var component in _components)
        {
            // A warning, not an error: a partially written script is the normal editing state, and
            // erroring here would blank the diagram on every keystroke.
            if (component.Origin is Origin.Declared
                && component.Kind is { HasUnlimitedPorts: false, Ports.IsEmpty: false }
                && !mentioned.Contains(component.Name))
            {
                Report(
                    BinderDiagnostics.NotConnected,
                    component.DeclarationSpan ?? default,
                    ("name", component.Name));
            }
        }

        ReportIslands(mentioned);
        ReportDeadEnds();
        ReviewExchangerModes();
        ReviewDutyDirection();
    }

    /// <summary>Reports a neutral exchanger whose signed duty contradicts its stated terminals (<c>FS2119</c>).</summary>
    /// <remarks>
    /// <c>C-67</c>. <c>power</c> is positive when side 1 gains heat (<c>22</c>), so on side 1 the outlet must
    /// then be the warmer end and on side 2 the cooler one. Only the neutral spellings are checked: a role
    /// word carries the sign (<c>D-91</c>) and lowering applies it, so <c>load power=24 in.t=50 out.t=30</c>
    /// is consistent by construction. A stated <c>dt</c> is a magnitude and cannot contradict anything.
    /// Arithmetic on stated values, nothing more -- the fluid is not needed to compare two temperatures.
    /// <para>
    /// <strong>Every case is read, and the finding is one per side however many cases share it</strong>
    /// (<c>C-120</c>). <see cref="ParameterValue.Value"/> holds the design case alone, so reading it let a
    /// contradictory summer bind clean and fail two layers down as a non-finite residual. Each case is
    /// checked whole — its own duty against its own terminals — so a reversible duty whose sign turns
    /// with its temperatures is consistent in both. The message quotes the first failing case's numbers
    /// and names the cases it fails in, and names none when no list touches the side, because then the
    /// contradiction is the file's rather than a case's.
    /// </para>
    /// </remarks>
    private void ReviewDutyDirection()
    {
        // A file without scenarios is one case, read from `Value`, marked -1.
        int[] cases = _scenarios.Count == 0 ? [-1] : [.. Enumerable.Range(0, _scenarios.Count)];

        foreach (var component in _components)
        {
            if (component.Kind is not { Keyword: "heat_exchanger" } kind
                || NameResolution.Normalize(component.WrittenKind) is "load" or "cooler" or "radiator" or "chiller" or "heater" or "boiler"
                || !component.Parameters.TryGetValue("power", out var duty))
            {
                continue;
            }

            foreach (var (inlet, outlet, side) in ((string, string, int)[])[("in", "out", 1), ("in2", "out2", 2)])
            {
                if (!component.Parameters.TryGetValue(inlet, out var entering)
                    || !component.Parameters.TryGetValue(outlet, out var leaving))
                {
                    continue;
                }

                var failing = new List<int>();
                (Quantity Power, Quantity In, Quantity Out)? first = null;

                foreach (var at in cases)
                {
                    if (ValueAt(duty, at) is not { SiValue: not 0 } power
                        || ValueAt(entering, at) is not { } a
                        || ValueAt(leaving, at) is not { } b
                        || a.SiValue == b.SiValue)
                    {
                        continue;
                    }

                    // Side 1 gains what the duty says; side 2 gives it.
                    var gains = power.SiValue > 0;

                    if (b.SiValue > a.SiValue == (side == 1 ? gains : !gains))
                    {
                        continue;
                    }

                    failing.Add(at);
                    first ??= (power, a, b);
                }

                if (first is not var (stated, a1, b1))
                {
                    continue;
                }

                var unit = UnitTable.CanonicalUnitFor(stated.Dimension);
                var celsius = UnitTable.CanonicalUnitFor(a1.Dimension);
                var losing = side == 1 ? stated.SiValue < 0 : stated.SiValue > 0;
                var listed = !duty.Scenarios.IsEmpty || !entering.Scenarios.IsEmpty || !leaving.Scenarios.IsEmpty;

                Report(
                    BinderDiagnostics.DutyContradictsTerminals,
                    duty.Span,
                    ("name", component.Name),
                    ("power", Format(unit is null ? stated.SiValue : stated.ValueIn(unit), unit?.Text)),
                    ("side", side.ToString(CultureInfo.InvariantCulture)),
                    ("duty", losing ? "loses heat" : "gains heat"),
                    ("inlet", kind.ParameterName(inlet)),
                    ("in", Format(celsius is null ? a1.SiValue : a1.ValueIn(celsius), celsius?.Text)),
                    ("outlet", kind.ParameterName(outlet)),
                    ("out", Format(celsius is null ? b1.SiValue : b1.ValueIn(celsius), celsius?.Text)),
                    ("change", b1.SiValue > a1.SiValue ? "warms" : "cools"),
                    ("cases", listed ? CaseClause(failing) : string.Empty));
            }
        }
    }

    /// <summary>A parameter's value in one case: the element when the file wrote a list, the scalar otherwise.</summary>
    /// <param name="value">The bound parameter.</param>
    /// <param name="scenario">The case's position, or -1 for a file without scenarios.</param>
    /// <returns>The evaluated value, or <see langword="null"/> when that case's expression was deferred.</returns>
    private static Quantity? ValueAt(ParameterValue value, int scenario) =>
        scenario < 0 || value.Scenarios.IsEmpty ? value.Value : value.Scenarios[scenario].Value;

    /// <summary>Names the cases a review failed in: <c> in summer</c>, <c> in winter, and likewise in summer</c>.</summary>
    /// <param name="cases">The failing cases' positions, in declared order; the first is the one the message quotes.</param>
    /// <returns>The clause, with its leading space.</returns>
    private string CaseClause(List<int> cases)
    {
        var clause = " in " + _scenarios[cases[0]];

        if (cases.Count == 1)
        {
            return clause;
        }

        var rest = cases.Skip(1).Select(at => _scenarios[at]).ToArray();

        return clause + ", and likewise in "
            + (rest.Length == 1 ? rest[0] : string.Join(", ", rest[..^1]) + " and " + rest[^1]);
    }

    /// <summary>Reports the exchanger's mode codes: <c>FS2112</c>, <c>FS2110</c> and <c>FS2109</c>.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than in step 3 because the mode is evidence of a second side, and half of that
    /// evidence is what the connections wired (<c>D-19</c>). Exactly one secondary port claimed is
    /// <c>FS2112</c>; a rating parameter on a component with neither secondary connections nor a
    /// secondary profile is <c>FS2110</c>, once per parameter, on the parameter; and an extended
    /// exchanger stating all four terminals, the duty and a thermal size is <c>FS2109</c>, on the size.
    /// </para>
    /// <para>
    /// A thermal size is <c>ua</c>, or <c>u</c> with an area -- stated outright or as a plate count and
    /// a plate area. Geometry alone (<c>plates</c>, <c>lamella</c>) is not one until the correlation that
    /// turns it into <c>u</c> ships, which <c>P4.1</c> defers.
    /// </para>
    /// </remarks>
    private void ReviewExchangerModes()
    {
        foreach (var component in _components)
        {
            if (component.Kind is not { Keyword: "heat_exchanger" } kind)
            {
                continue;
            }

            var claimed = _claimed.TryGetValue(component.Name, out var used) ? used : [];
            var inletWired = claimed.ContainsKey("in2");
            var outletWired = claimed.ContainsKey("out2");

            if (inletWired != outletWired)
            {
                Report(
                    BinderDiagnostics.OneSecondaryPortOpen,
                    component.DeclarationSpan ?? default,
                    ("name", component.Name),
                    ("port", kind.PortName(inletWired ? "out2" : "in2")));
            }

            var stated = component.Parameters;
            var profile = stated.ContainsKey("in2") || stated.ContainsKey("out2")
                || stated.ContainsKey("dt2") || stated.ContainsKey("flow2");

            if (!inletWired && !outletWired && !profile)
            {
                foreach (var name in RatingParameters)
                {
                    if (stated.TryGetValue(name, out var inert))
                    {
                        Report(
                            BinderDiagnostics.RatingWithoutASecondSide,
                            inert.Span,
                            ("name", component.Name),
                            ("param", name));
                    }
                }

                continue;
            }

            var terminals = stated.ContainsKey("in") && stated.ContainsKey("out")
                && stated.ContainsKey("in2") && stated.ContainsKey("out2") && stated.ContainsKey("power");

            string? size = stated.ContainsKey("ua") ? "ua"
                : stated.ContainsKey("u") && stated.ContainsKey("area") ? "area"
                : stated.ContainsKey("u") && stated.ContainsKey("plates") && stated.ContainsKey("plate_area") ? "plates"
                : null;

            if (terminals && size is not null)
            {
                Report(
                    BinderDiagnostics.ExchangerOverDetermined,
                    stated[size].Span,
                    ("name", component.Name),
                    ("param", size));
            }
        }
    }

    private void ReportIslands(HashSet<string> mentioned)
    {
        // FS1511 is about a cluster and FS1507 about a component on its own, and the two never both
        // fire for one component — which is why anything in no connection at all is excluded here.
        foreach (var circuit in _circuits)
        {
            var members = _components
                .Where(component =>
                    string.Equals(component.CircuitName, circuit.Name, StringComparison.Ordinal)
                    && mentioned.Contains(component.Name))
                .Select(static component => component.Name)
                .ToHashSet(StringComparer.Ordinal);

            var islands = Islands(members);

            if (islands.Count < 2)
            {
                continue;
            }

            // The largest island is "the rest of the circuit"; every other one is adrift from it. A
            // tie breaks on the first name, so the report does not move when an edit elsewhere
            // changes a count.
            var main = islands
                .OrderByDescending(static island => island.Count)
                .ThenBy(static island => island[0], StringComparer.Ordinal)
                .First();

            foreach (var island in islands.Where(island => island != main))
            {
                Report(
                    BinderDiagnostics.DisconnectedGraph,
                    SpanOfFirstMention(island[0]),
                    ("name", island[0]),
                    ("count", (island.Count - 1).ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private List<List<string>> Islands(HashSet<string> members)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var name in members)
        {
            adjacency[name] = [];
        }

        foreach (var connection in _sourceConnections)
        {
            if (members.Contains(connection.From.Component) && members.Contains(connection.To.Component))
            {
                adjacency[connection.From.Component].Add(connection.To.Component);
                adjacency[connection.To.Component].Add(connection.From.Component);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var islands = new List<List<string>>();

        // Walked in declaration order, so the island a diagnostic names is the one whose first
        // component the user wrote first rather than whichever the hash set happened to yield.
        foreach (var component in _components)
        {
            if (!members.Contains(component.Name) || !seen.Add(component.Name))
            {
                continue;
            }

            var island = new List<string> { component.Name };
            var stack = new Stack<string>();
            stack.Push(component.Name);

            while (stack.Count > 0)
            {
                foreach (var neighbour in adjacency[stack.Pop()])
                {
                    if (seen.Add(neighbour))
                    {
                        island.Add(neighbour);
                        stack.Push(neighbour);
                    }
                }
            }

            islands.Add(island);
        }

        return islands;
    }

    private void ReportDeadEnds()
    {
        // `L-44`. This used to exempt every node a subcircuit attaches to, because `supply N3` lowered to
        // a connection one stage *later* than this ran, so the node's second edge did not exist yet and
        // both of the header's headers were warned about (`F-12`). `BindAttachments` now runs before
        // inference, so that connection is already counted in `_degrees`, and the exemption was dead
        // code standing in for an edge that exists. Removing it changes no sample's diagnostics.
        foreach (var node in _components)
        {
            // A node with one connection that is not a boundary is a dead end: since D-115 only the
            // kind says mass crosses, so a `node p=` on a stub is a datum that passes nothing, and the
            // message says which word makes it a boundary. A node inferred by I3 is exempt — it *is*
            // the boundary that rule created, so it terminates a port rather than dead-ending on one.
            if (node.Kind?.HasUnlimitedPorts != true
                || node.Origin is Origin.Inferred { Rule: "I3" }
                || _degrees.GetValueOrDefault(node.Name) != 1
                || IsBoundary(node))
            {
                continue;
            }

            Report(
                BinderDiagnostics.DeadEndNode,
                node.DeclarationSpan ?? SpanOfFirstMention(node.Name),
                ("name", node.Name));
        }
    }

    private static bool IsBoundary(ComponentSymbol node) =>
        node.Kind?.Keyword is "inlet" or "outlet";

    private TextSpan SpanOfFirstMention(string component) =>
        _connections.FirstOrDefault(connection =>
            string.Equals(connection.From.Component, component, StringComparison.Ordinal)
            || string.Equals(connection.To.Component, component, StringComparison.Ordinal))
            ?.SourceSpan ?? default;
}
