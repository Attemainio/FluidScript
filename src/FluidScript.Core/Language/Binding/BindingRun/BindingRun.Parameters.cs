using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    private ParameterInfo? ResolveParameter(
        ComponentKindInfo kind, string written, ParameterSyntax parameter)
    {
        // A node has one state and no ports (`D-120` rule 4): `in.t=` on one is the one shape the
        // registry cannot explain by listing what it accepts, because the quantity is right and the
        // port is the mistake.
        if (kind.HasUnlimitedPorts && parameter.Name.Parts.Length > 0)
        {
            Report(
                BinderDiagnostics.PortStateOnNode,
                parameter.Name.Span,
                ("kind", kind.Keyword),
                ("quantity", parameter.Name.Parts[^1].Name.Text),
                ("written", written));
            return null;
        }

        // Name, alias, the spelling `D-120` retired, or an indexed family member -- `layer[3].t`,
        // `in[2].level`, the old `t3` -- all before similarity, so a tank's fortieth layer is an index
        // error rather than an unknown parameter, and an old spelling is a suggestion rather than a
        // near miss.
        if (kind.ResolveParameter(written, out var suggestion, out var outside) is { } resolved)
        {
            if (suggestion is not null)
            {
                ReportLegacySpelling(parameter.Name.Span, written, suggestion);
            }

            return resolved;
        }

        if (outside is { } family)
        {
            Report(
                BinderDiagnostics.IndexOutsideFamily,
                parameter.Span,
                ("written", written),
                ("kind", kind.Keyword),
                ("min", Math.Min(1, family.MinIndex).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("max", (family.MaxIndex ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return null;
        }

        // A port state whose quantity the table names is never a near miss: `in.p` is one edit from
        // `in.t` and was read as it, so a stated pressure became a temperature of 300 °C under an
        // information notice (FS1512). The answer is what this port takes.
        if (parameter.Name.Parts.Length > 0
            && written.LastIndexOf('.') is var dot and > 0
            && PropertyTable.Find(written[(dot + 1)..]) is { } quantity)
        {
            var port = written[..dot];
            var takes = kind.Parameters.Values
                .SelectMany(static info => info.Aliases.Prepend(info.Name))
                .Concat(kind.IndexedParameterFamilies.Select(static family => family.Pattern))
                .Where(name => name.StartsWith(port + ".", StringComparison.Ordinal))
                .Select(name => name[(port.Length + 1)..])
                .Order(StringComparer.Ordinal)
                .ToArray();

            Report(
                takes.Length == 0 ? BinderDiagnostics.UnknownParameter : BinderDiagnostics.UnknownPortQuantity,
                parameter.Name.Span,
                ("kind", kind.Keyword),
                ("parameter", written),
                ("port", port),
                ("quantity", quantity.Symbol),
                ("available", string.Join(", ", takes.Length == 0 ? kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal) : takes)));
            return null;
        }

        var index2 = kind.Parameters.Values.ToImmutableDictionary(
            static info => NameResolution.Normalize(info.Name),
            static info => info,
            StringComparer.Ordinal);

        var match = NameResolution.Match(written, index2);

        // `D-15`'s first stage: `HEAD`, `Kp` and `in_t` are the names they normalise to, and a known spelling is
        // not a guess, so it binds without a word. The registry's own lookup is ordinal, which is why this was
        // reported as FS1503 until 2026-09-25 -- a case variant fell past the near-miss branch below.
        if (match.IsExact && match.Best is not null)
        {
            return match.Best;
        }

        // A diagnostic about a name underlines the name (44), so a quick fix that replaces the range
        // replaces the name and keeps the value (L-53).
        if (!match.IsExact && match.Best is not null && match.BestScore >= NameResolution.ResolveThreshold
            && match.IsClear && parse.Language == 2)
        {
            // `D-170`: in language 2 a near miss binds nothing -- `haed = 15` would otherwise be a stated head.
            // The spelling it was near is the one-click fix, on the name alone (L-53).
            Report(
                BinderDiagnostics.UnknownParameter,
                parameter.Name.Span,
                new Suggestion($"Change it to '{match.Best.Name}'", parameter.Name.Span, match.Best.Name),
                ("kind", kind.Keyword),
                ("parameter", written),
                ("available", string.Join(", ", kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal))));
            return null;
        }

        if (!match.IsExact && match.Best is not null && match.BestScore >= NameResolution.ResolveThreshold
            && match.IsClear)
        {
            Report(
                BinderDiagnostics.ResolvedBySimilarity,
                parameter.Name.Span,
                ("written", written),
                ("canonical", match.Best.Name));
            return match.Best;
        }

        Report(
            BinderDiagnostics.UnknownParameter,
            parameter.Name.Span,
            ("kind", kind.Keyword),
            ("parameter", written),
            ("available", string.Join(", ", kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal))));
        return null;
    }

    private ParameterValue? BindParameterValue(
        ComponentKindInfo kind,
        ParameterInfo info,
        ParameterSyntax parameter,
        string componentName,
        string written)
    {
        var span = parameter.Span;

        switch (info.ValueKind)
        {
            case ParameterValueKind.Symbol:
            {
                var name = (parameter.Value as ReferenceSyntax)?.Head.Token.Text;

                if (name is null || !info.AcceptedSymbols.Contains(name, StringComparer.Ordinal))
                {
                    Report(
                        BinderDiagnostics.UnacceptedSymbol,
                        span,
                        ("parameter", info.Name),
                        ("available", string.Join(", ", info.AcceptedSymbols)),
                        ("written", name ?? parse.Source.ToString(parameter.Value.Span).Trim()));
                    return null;
                }

                return new ParameterValue
                {
                    WrittenName = written,
                    Symbol = name,
                    Expression = parameter.Value,
                    Span = span,
                };
            }

            case ParameterValueKind.Reference:
            {
                if (parameter.Value is not ReferenceSyntax { Parts.Length: > 0 } reference)
                {
                    Report(BinderDiagnostics.ExpectedReference, span, ("parameter", info.Name));
                    return null;
                }

                return new ParameterValue
                {
                    WrittenName = written,
                    Reference = new PropertyReference(
                        reference.Head.Token.Text,
                        reference.PropertyPath()),
                    Expression = parameter.Value,
                    Span = span,
                };
            }

            default:
            {
                if (parameter.Value is ScenarioListSyntax list)
                {
                    return BindScenarioList(kind, info, parameter, list, componentName, written);
                }

                var id = new ValueId.ComponentParameter(componentName, info.Key);
                _graph.Add(id);
                _pending[id] = new PendingValue(parameter.Value, id, span, new ParameterTarget(componentName, kind, info));

                return new ParameterValue
                {
                    WrittenName = written,
                    Expression = parameter.Value,
                    Span = span,
                };
            }
        }
    }

    /// <summary>Binds one value per declared scenario (<c>D-143</c>).</summary>
    /// <param name="kind">The component's kind.</param>
    /// <param name="info">The parameter's registry row.</param>
    /// <param name="parameter">The whole <c>name=[...]</c>.</param>
    /// <param name="list">Its bracketed values.</param>
    /// <param name="componentName">The component's name.</param>
    /// <param name="written">The spelling the file used.</param>
    /// <returns>The bound parameter, or <see langword="null"/> when the list binds to nothing.</returns>
    /// <remarks>
    /// <para>
    /// Each element becomes its own node in the dependency graph, because each may read a curve or a
    /// <c>let</c> the others do not. The design scenario's element also takes the ordinary
    /// <see cref="ValueId.ComponentParameter"/> id, which is what keeps <see cref="ParameterValue.Value"/>
    /// a scalar filled by the path that already existed -- so nothing downstream of the binder learns
    /// that scenarios exist, and projecting to another case is one rewrite of that one field.
    /// </para>
    /// <para>
    /// <strong>Length is checked before anything binds, and nothing is padded.</strong> A list of the
    /// wrong length binds no value at all rather than a partial one: a parameter half-bound across
    /// cases would size a plant from cases the file never stated.
    /// </para>
    /// </remarks>
    private ParameterValue? BindScenarioList(
        ComponentKindInfo kind,
        ParameterInfo info,
        ParameterSyntax parameter,
        ScenarioListSyntax list,
        string componentName,
        string written)
    {
        var span = parameter.Span;
        var declared = _scenarios.Count;

        if (declared == 0)
        {
            Report(BinderDiagnostics.ScenarioListWithoutScenarios, span, ("written", written));
            return null;
        }

        if (list.Elements.Length != declared)
        {
            Report(
                BinderDiagnostics.ScenarioCountMismatch,
                span,
                ("written", written),
                ("given", list.Elements.Length.ToString(CultureInfo.InvariantCulture)),
                ("count", declared.ToString(CultureInfo.InvariantCulture)),
                ("names", string.Join(", ", _scenarios)));
            return null;
        }

        // Which element fills `Value`. Already reported as FS1542 or FS1543 when it is not settled,
        // and the first element stands in so the rest of the bind has a scalar to work from rather
        // than a second failure on every parameter in the file.
        var design = _designScenario is { } named
            ? Math.Max(0, _scenarios.IndexOf(named.Name))
            : 0;

        var target = new ParameterTarget(componentName, kind, info);
        var elements = ImmutableArray.CreateBuilder<ParameterValue>(declared);

        for (var index = 0; index < declared; index++)
        {
            var element = list.Elements[index].Value;
            ValueId id = index == design
                ? new ValueId.ComponentParameter(componentName, info.Key)
                : new ValueId.ScenarioParameter(componentName, info.Key, index);

            _graph.Add(id);
            _pending[id] = new PendingValue(element, id, element.Span, target);

            elements.Add(new ParameterValue
            {
                WrittenName = written,
                Expression = element,
                Span = element.Span,
            });
        }

        return new ParameterValue
        {
            WrittenName = written,
            Expression = parameter.Value,
            Span = span,
            Scenarios = elements.MoveToImmutable(),
        };
    }

    /// <summary>The key a stated parameter spelled like a property is stored under, or null when no parameter is spelled so.</summary>
    /// <remarks>
    /// Names, aliases and legacy spellings, and the indexed families -- the same walk
    /// <see cref="ResolveParameter"/> makes, without its diagnostics, because this is a read and the
    /// declaration already said what it had to.
    /// </remarks>
    private static string? StatedParameterKey(ComponentKindInfo kind, string written) =>
        kind.ResolveParameter(written, out _, out _)?.Key;
}
