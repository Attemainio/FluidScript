using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>Step 5b: the component checks that need more than one parameter to see.</summary>
/// <remarks>
/// <para>
/// Everything here runs after <c>Evaluate</c> and before topology, because each check reads a
/// <em>set</em> of a component's parameters and their values. A per-parameter check cannot see that
/// four values were stated where three would do, and a check that ran before evaluation would see
/// expressions rather than the numbers it has to count and compare.
/// </para>
/// <para>
/// <strong>Counting is the whole of it.</strong> Whether the stated values <em>agree</em> with each
/// other — whether an exchanger's fourth number is the one the other three imply, whether a stated
/// head matches a stated pressure rise — needs a fluid, and the binder has none: it holds a fluid's
/// name, and the substance behind that name is resolved at lowering. Those codes are <c>P3.4</c>'s.
/// </para>
/// </remarks>
internal sealed partial class BindingRun
{
    private void ReviewComponents()
    {
        foreach (var component in _components)
        {
            // An unresolved kind already carries its own error, and its parameters were kept as
            // written with nothing to check them against.
            if (component.Kind is not { } kind)
            {
                continue;
            }

            ReviewParameterGroups(component, kind);
            ReviewLayerTemperatures(component, kind);
        }
    }

    /// <summary>Reports a relation given more stated values than it has freedoms (<c>FS2101</c>).</summary>
    /// <param name="component">The component to check.</param>
    /// <param name="kind">Its resolved kind.</param>
    /// <remarks>
    /// The span is the <em>last</em> stated member's, because that is the one a fix removes: reporting
    /// on the component name would put the caret on something correct and leave the user to work out
    /// which of four assignments to delete.
    /// </remarks>
    private void ReviewParameterGroups(ComponentSymbol component, ComponentKindInfo kind)
    {
        foreach (var group in kind.ParameterGroups)
        {
            var stated = group.Parameters.Where(component.Parameters.ContainsKey).ToImmutableArray();

            if (stated.Length <= group.Freedoms)
            {
                continue;
            }

            var arguments = new List<(string Name, string Value)>(stated.Length + 3)
            {
                ("name", component.Name),
                ("parameters", string.Join(", ", stated)),
                ("count", group.Freedoms.ToString(CultureInfo.InvariantCulture)),
            };

            // Every stated member under its own parameter name, so a message that wants to quote one
            // of them -- FS2103's "using kv=6.3" -- needs no second argument list.
            foreach (var name in stated)
            {
                arguments.Add((name, Display(kind, name, component.Parameters[name])));
            }

            Report(group.Descriptor, component.Parameters[stated[^1]].Span, [.. arguments]);
        }
    }

    /// <summary>Reports a stratification profile that is mixed or partial (<c>FS2113</c>).</summary>
    /// <param name="component">The component to check.</param>
    /// <param name="kind">Its resolved kind.</param>
    /// <remarks>
    /// <para>
    /// Driven by the indexed family rather than by the keyword <c>tank</c>: the rule is a property of
    /// a parameter family whose element has a bulk form, and a second kind that grew one would
    /// otherwise need this method copied.
    /// </para>
    /// <para>
    /// <strong>A layer count that is itself invalid reports nothing here.</strong> It has already
    /// produced <c>FS2114</c>, and a second message saying the profile does not have that many entries
    /// would be the same mistake counted twice.
    /// </para>
    /// </remarks>
    private void ReviewLayerTemperatures(ComponentSymbol component, ComponentKindInfo kind)
    {
        var family = kind.IndexedParameterFamilies.FirstOrDefault(
            static candidate => candidate.Element.Dimension == Dimension.Temperature);

        if (family is null || LayerCount(component, kind, family) is not { } layers)
        {
            return;
        }

        var indexed = new SortedSet<int>();
        TextSpan? earliest = null;

        foreach (var (name, value) in component.Parameters)
        {
            if (!Indexed.Matches(family.Pattern, name, out var index))
            {
                continue;
            }

            indexed.Add(index);

            if (earliest is null || value.Span.Start < earliest.Value.Start)
            {
                earliest = value.Span;
            }
        }

        if (indexed.Count == 0 || earliest is not { } first)
        {
            return;
        }

        // A sorted set of distinct indices is 1..layers exactly when it has that many entries and
        // those two endpoints, so contiguity needs no further pass.
        var complete = indexed.Count == layers && indexed.Min == 1 && indexed.Max == layers;
        var bulk = component.Parameters.TryGetValue(family.Element.Name, out var whole)
            ? whole.Span
            : (TextSpan?)null;

        if (bulk is null && complete)
        {
            return;
        }

        Report(
            BinderDiagnostics.MixedTankTemperatures,
            bulk ?? first,
            ("name", component.Name),
            ("layers", layers.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>How many members an indexed family has on this component.</summary>
    /// <param name="component">The component the family belongs to.</param>
    /// <param name="kind">Its resolved kind.</param>
    /// <param name="family">The family whose extent is wanted.</param>
    /// <returns>
    /// The count, or <see langword="null"/> when the parameter that supplies it was stated as
    /// something that is not a layer count.
    /// </returns>
    private static int? LayerCount(
        ComponentSymbol component, ComponentKindInfo kind, IndexedParameterFamilyInfo family)
    {
        if (family.MaxIndexParameter is not { } key)
        {
            return family.MaxIndex;
        }

        if (component.Parameters.TryGetValue(key, out var stated))
        {
            return stated.Value is { SiValue: var value } && double.IsInteger(value) && value >= 1
                ? (int)value
                : null;
        }

        // Not stated, so the family is as long as the kind's visible default says (D-02): a tank with
        // no `layers` has five, and `t1 t2 t3` on it is a partial profile rather than a complete one.
        return kind.Parameters.TryGetValue(key, out var info)
            && info.DefaultLiteral is { } literal
            && double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var fallback)
                ? (int)fallback
                : null;
    }

    /// <summary>Renders a stated parameter the way the script wrote it, for a message.</summary>
    /// <param name="kind">The component's kind, which owns the parameter's dimension.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <param name="value">What binding stored for it.</param>
    /// <returns>The value in its canonical unit, or the source text when it did not evaluate.</returns>
    private string Display(ComponentKindInfo kind, string parameter, ParameterValue value)
    {
        if (value.Value is not { } quantity)
        {
            return parse.Source.ToString(value.Expression.Span).Trim();
        }

        var unit = kind.Parameters.TryGetValue(parameter, out var info)
            ? UnitTable.CanonicalUnitFor(info.Dimension)
            : null;

        return Format(unit is null ? quantity.SiValue : quantity.ValueIn(unit), null);
    }
}
