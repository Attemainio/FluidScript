using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Translation;

internal sealed partial class TranslationRun
{
    /// <summary>The port inferred for one end of one link, by the endpoint's position and the direction of flow at it.</summary>
    private readonly Dictionary<(int Start, bool Inflow), string> _inferred = [];

    /// <summary>The circuit each component is declared in, by index in file order.</summary>
    private readonly Dictionary<string, int> _declaredIn = new(StringComparer.Ordinal);

    /// <summary>Each component's kind as written, for what a spelling asserts: <c>mixing_valve</c>, <c>diverting_valve</c>.</summary>
    private readonly Dictionary<string, string> _writtenKinds = new(StringComparer.Ordinal);

    /// <summary>One end of one link: where a stream enters or leaves a component.</summary>
    /// <param name="Endpoint">The endpoint as written.</param>
    /// <param name="Inflow">Whether the stream enters the component here; a chain reads in the direction of flow.</param>
    /// <param name="Circuit">The circuit the line is written in, by index.</param>
    /// <param name="Order">Where the end falls in the file, for the rules that go by the order written.</param>
    /// <param name="Peer">The name at the link's other end, for the information message.</param>
    private sealed record End(EndpointSyntax Endpoint, bool Inflow, int Circuit, int Order, string Peer)
    {
        public (int, bool) Key => (Endpoint.Span.Start, Inflow);
    }

    /// <summary>Settles the port of every end the script left unnamed, by the direction of flow (<c>19</c> §Connections, <c>D-166</c>).</summary>
    /// <remarks>
    /// After every line of the file is read, because a three-way valve's function and an exchanger's sides are known
    /// only from all of its connections. An explicit port always wins (rule 5) and is taken out before the rest are
    /// given theirs; what the rules cannot settle is <c>FS1804</c> (rule 6), and that end stays without a port.
    /// </remarks>
    private void InferPorts(List<BlockSyntax> circuits)
    {
        var ends = new Dictionary<string, List<End>>(StringComparer.Ordinal);
        var order = 0;

        for (var circuit = 0; circuit < circuits.Count; circuit++)
        {
            foreach (var chain in circuits[circuit].Body.Select(Chain).OfType<ConnectionSyntax>())
            {
                var endpoints = chain.Endpoints;
                for (var i = 0; i + 1 < endpoints.Length; i++)
                {
                    Add(endpoints[i], inflow: false, endpoints[i + 1]);
                    Add(endpoints[i + 1], inflow: true, endpoints[i]);
                }

                void Add(EndpointSyntax endpoint, bool inflow, EndpointSyntax peer)
                {
                    var name = endpoint.Component.Text;
                    if (!ends.TryGetValue(name, out var list))
                    {
                        ends[name] = list = [];
                    }

                    list.Add(new End(endpoint, inflow, circuit, order++, peer.Component.Text));
                }
            }
        }

        foreach (var (name, list) in ends)
        {
            if (!_kinds.TryGetValue(name, out var kind) || kind is null || kind.HasUnlimitedPorts || kind.Ports.IsEmpty)
            {
                continue;
            }

            var claimed = list
                .Where(static end => end.Endpoint.Port is not null)
                .Select(end => kind.ResolvePort(PortName(end.Endpoint.Port!).Text, out _, out _))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            var free = list.Where(static end => end.Endpoint.Port is null).ToList();
            if (free.Count == 0)
            {
                continue;
            }

            if (kind.Keyword == "three_way_valve")
            {
                Valve(name, list, free, claimed);
            }
            else if (kind.Ports.Any(static port => port.Key == "in2"))
            {
                Exchanger(name, kind, free, claimed);
            }
            else if (!kind.PortFamilies.IsEmpty)
            {
                Tank(name, kind, free, claimed);
            }
            else
            {
                TwoPort(name, kind, free, claimed);
            }
        }
    }

    /// <summary>Rule 1: the stream flowing in takes the inlet, the one flowing out the outlet.</summary>
    private void TwoPort(string name, ComponentKindInfo kind, List<End> free, HashSet<string> claimed)
    {
        foreach (var inflow in new[] { true, false })
        {
            var role = inflow ? PortRole.Inlet : PortRole.Outlet;
            var ports = kind.Ports
                .Where(port => port.Role == role && !claimed.Contains(port.Key))
                .Select(static port => port.Name)
                .ToList();

            foreach (var end in free.Where(end => end.Inflow == inflow))
            {
                // The one port this stream could take is written on another line: that is two streams on one port,
                // which the binder reports once, naming the other line (FS1506). FS1804's fix, naming a port, is the
                // one thing that cannot help, so the stream takes the port and the binder says what is wrong.
                if (ports.Count == 0
                    && kind.Ports.Where(port => port.Role == role).ToList() is [var only]
                    && claimed.Contains(only.Key))
                {
                    Give(end, only.Name);
                    continue;
                }

                if (ports.Count == 0)
                {
                    NotInferred(
                        name,
                        end,
                        string.Create(CultureInfo.InvariantCulture, $"a {kind.Keyword} has one {(inflow ? "inlet" : "outlet")}, and it is already connected"),
                        kind.Ports.Select(static port => port.Name));
                    continue;
                }

                Give(end, ports[0]);
                ports.RemoveAt(0);
            }
        }
    }

    /// <summary>Rule 2: two streams in and one out is mixing, one in and two out is diverting; the first written is <c>a</c>.</summary>
    /// <remarks>
    /// The function is counted over every connection, named ports included, since a port written on one leg does not
    /// change how many streams meet. A valve with one stream in and one out takes the function its kind asserts; a bare
    /// <c>valve3</c> with two legs says neither, and is <c>FS1804</c>.
    /// </remarks>
    private void Valve(string name, List<End> all, List<End> free, HashSet<string> claimed)
    {
        var ins = all.Count(static end => end.Inflow);
        var outs = all.Count - ins;

        var asserted = _writtenKinds.TryGetValue(name, out var written)
            ? NameResolution.Normalize(written) switch
            {
                "mixingvalve" => "mixing",
                "divertingvalve" => "diverting",
                _ => null,
            }
            : null;

        var function = (ins, outs) switch
        {
            (2, 1) => "mixing",
            (1, 2) => "diverting",
            (1, 1) => asserted,
            _ => null,
        };

        if (function is null)
        {
            NotInferred(
                name,
                free[0],
                string.Create(CultureInfo.InvariantCulture, $"a three-way valve takes two streams in and one out, or one in and two out, and this one has {ins} in and {outs} out"),
                ["a", "b", "ab"]);
            return;
        }

        if (asserted is not null && asserted != function)
        {
            Report(
                Language2Diagnostics.ValveFunctionContradicted,
                free[0].Endpoint.Span,
                ("component", name),
                ("asserted", asserted),
                ("actual", function),
                ("inflows", ins.ToString(CultureInfo.InvariantCulture)),
                ("outflows", outs.ToString(CultureInfo.InvariantCulture)));
        }

        // The common port takes the one stream on its side: the outflow of a mixing valve, the inflow of a diverting one.
        var commonIsInflow = function == "diverting";
        var legs = new List<string> { "a", "b" };
        legs.RemoveAll(claimed.Contains);

        var wiring = new List<string>();

        foreach (var end in free)
        {
            string? port = end.Inflow == commonIsInflow
                ? (claimed.Contains("ab") ? null : "ab")
                : legs.Count > 0 ? legs[0] : null;

            if (port is null)
            {
                NotInferred(name, end, "every port on that side is already connected", ["a", "b", "ab"]);
                continue;
            }

            if (port != "ab")
            {
                legs.RemoveAt(0);
            }
            else
            {
                claimed.Add("ab");
            }

            Give(end, port);
            wiring.Add(Wired(port, end));
        }

        Report(
            Language2Diagnostics.PortsInferred,
            free[0].Endpoint.Span,
            ("component", name),
            ("wiring", $"a {function} valve: {string.Join(", ", wiring)}"));
    }

    /// <summary>Rule 3: the side wired in the declaring circuit is primary, a side wired from another circuit secondary.</summary>
    /// <remarks>
    /// A pass is an inlet and an outlet of one side. A chain through the exchanger, <c>S - HX1 - R</c>, is one pass;
    /// ends on separate lines pair within their circuit in the order written. The passes of the declaring circuit come
    /// first, then the others in file order: the first takes <c>in</c>/<c>out</c>, the second <c>in[2]</c>/<c>out[2]</c>.
    /// A side with one port already written is given only to a pass that needs its other port, so
    /// <c>HX1.secondary.out - TV1</c> and <c>NR - HX1</c> make one secondary side between them.
    /// </remarks>
    private void Exchanger(string name, ComponentKindInfo kind, List<End> free, HashSet<string> claimed)
    {
        var passes = new List<List<End>>();

        foreach (var circuit in free.GroupBy(static end => end.Circuit))
        {
            var inflows = circuit.Where(static end => end.Inflow).ToList();
            var outflows = circuit.Where(static end => !end.Inflow).ToList();

            // A chain through the component is its own pass: the same occurrence carries both ends.
            foreach (var inflow in inflows.ToArray())
            {
                if (outflows.FirstOrDefault(outflow => ReferenceEquals(outflow.Endpoint, inflow.Endpoint)) is { } through)
                {
                    passes.Add([inflow, through]);
                    inflows.Remove(inflow);
                    outflows.Remove(through);
                }
            }

            for (var i = 0; i < Math.Max(inflows.Count, outflows.Count); i++)
            {
                passes.Add([.. new[] { inflows.ElementAtOrDefault(i), outflows.ElementAtOrDefault(i) }.OfType<End>()]);
            }
        }

        var home = _declaredIn.GetValueOrDefault(name, -1);
        var ordered = passes
            .OrderBy(pass => pass[0].Circuit == home ? 0 : 1)
            .ThenBy(static pass => pass.Min(static end => end.Order))
            .ToList();

        var sides = new List<(string In, string Out, string Label)> { ("in", "out", "primary"), ("in[2]", "out[2]", "secondary") };
        bool Free(string port) => !claimed.Contains(Key(kind, port));

        var wiring = new List<string>();

        foreach (var pass in ordered)
        {
            var needsIn = pass.Exists(static end => end.Inflow);
            var needsOut = pass.Exists(static end => !end.Inflow);
            var index = sides.FindIndex(side => (!needsIn || Free(side.In)) && (!needsOut || Free(side.Out)));
            if (index < 0)
            {
                NotInferred(name, pass[0], "an exchanger has two sides, and both are already wired", ["primary.in", "primary.out", "secondary.in", "secondary.out"]);
                continue;
            }

            var side = sides[index];
            sides.RemoveAt(index);

            foreach (var end in pass)
            {
                Give(end, end.Inflow ? side.In : side.Out);
            }

            var from = pass.FirstOrDefault(static end => end.Inflow)?.Peer;
            var to = pass.FirstOrDefault(static end => !end.Inflow)?.Peer;
            wiring.Add($"{side.Label}{(from is null ? string.Empty : $" from {from}")}{(to is null ? string.Empty : $" to {to}")}");
        }

        if (wiring.Count > 1)
        {
            Report(Language2Diagnostics.PortsInferred, free[0].Endpoint.Span, ("component", name), ("wiring", string.Join(", ", wiring)));
        }
    }

    /// <summary>Rule 4: a tank's inflows take <c>in</c>, <c>in[2]</c>, … and its outflows <c>out</c>, <c>out[2]</c>, … in the order written.</summary>
    private void Tank(string name, ComponentKindInfo kind, List<End> free, HashSet<string> claimed)
    {
        var wiring = new List<string>();

        foreach (var inflow in new[] { true, false })
        {
            var prefix = inflow ? "in" : "out";
            var family = kind.PortFamilies.FirstOrDefault(family => family.Prefix == prefix);
            var max = family?.MaxIndex ?? 1;
            var index = 1;

            foreach (var end in free.Where(end => end.Inflow == inflow))
            {
                while (index <= max && claimed.Contains(Key(kind, Spell(prefix, index))))
                {
                    index++;
                }

                if (index > max)
                {
                    NotInferred(name, end, string.Create(CultureInfo.InvariantCulture, $"a tank has {max} {prefix} ports, and all are connected"), [prefix]);
                    continue;
                }

                var port = Spell(prefix, index++);
                Give(end, port);
                wiring.Add(Wired(port, end));
            }
        }

        // Only a side with more than one stream had a choice made for it.
        if (free.Count(static end => end.Inflow) > 1 || free.Count(static end => !end.Inflow) > 1)
        {
            Report(Language2Diagnostics.PortsInferred, free[0].Endpoint.Span, ("component", name), ("wiring", string.Join(", ", wiring)));
        }

        static string Spell(string prefix, int index) =>
            index == 1 ? prefix : string.Create(CultureInfo.InvariantCulture, $"{prefix}[{index}]");
    }

    private static string Key(ComponentKindInfo kind, string port) =>
        kind.ResolvePort(port, out _, out _) ?? port;

    private static string Wired(string port, End end) => end.Inflow ? $"{port} from {end.Peer}" : $"{port} to {end.Peer}";

    private void Give(End end, string port) => _inferred[end.Key] = port;

    private void NotInferred(string name, End end, string reason, IEnumerable<string> ports) =>
        Report(
            Language2Diagnostics.PortNotInferred,
            end.Endpoint.Span,
            ("component", name),
            ("reason", reason),
            ("example", $"{name}.{ports.First()}"));

    /// <summary>The endpoint with a port the rule settled, written as language 1's explicit port.</summary>
    private EndpointSyntax WithInferredPort(EndpointSyntax endpoint, bool inflow)
    {
        if (endpoint.Port is not null || !_inferred.TryGetValue((endpoint.Span.Start, inflow), out var port))
        {
            return endpoint;
        }

        var at = endpoint.Component.Span.End;
        return endpoint with
        {
            Dot = Made(TokenKind.Dot, ".", new TextSpan(at, 0)),
            Port = new QualifiedNameSyntax(PortSyntax(port, at), []),
        };
    }

    /// <summary>A port name made here, <c>ab</c> or <c>in[2]</c>, placed where the endpoint's name ends.</summary>
    private static IndexedNameSyntax PortSyntax(string port, int at)
    {
        var open = port.IndexOf('[', StringComparison.Ordinal);
        if (open < 0)
        {
            return new IndexedNameSyntax(Identifier(port, new TextSpan(at, 0)), null);
        }

        var digits = port[(open + 1)..^1];
        var number = new Token
        {
            Kind = TokenKind.NumberLiteral,
            Text = digits,
            NumberText = digits,
            Value = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
            Span = new TextSpan(at, 0),
        };

        return new IndexedNameSyntax(
            Identifier(port[..open], new TextSpan(at, 0)),
            new IndexSyntax(Made(TokenKind.OpenBracket, "[", new TextSpan(at, 0)), number, Made(TokenKind.CloseBracket, "]", new TextSpan(at, 0))));
    }
}
