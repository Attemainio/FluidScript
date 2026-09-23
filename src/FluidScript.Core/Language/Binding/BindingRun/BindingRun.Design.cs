using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    /// <summary>Records the declared cases, refusing a repeated name (<c>D-143</c>).</summary>
    /// <param name="scenarios">The directive.</param>
    /// <remarks>
    /// Order is kept exactly as written, because that order is the whole binding: element <c>i</c> of
    /// every list belongs to the name at position <c>i</c>. A duplicate is refused rather than
    /// collapsed -- two cases called <c>summer</c> would give a size a basis naming a case that
    /// cannot be looked up -- and the repeat is skipped so the positions of the rest do not shift.
    /// </remarks>
    private void DeclareScenarios(ScenariosDirectiveSyntax scenarios)
    {
        _scenarioSpan ??= scenarios.Span;

        foreach (var name in scenarios.Names)
        {
            if (_scenarios.Contains(name.Token.Text, StringComparer.Ordinal))
            {
                Report(BinderDiagnostics.DuplicateScenario, name.Span, ("name", name.Token.Text));
                continue;
            }

            _scenarios.Add(name.Token.Text);
        }
    }

    private void DeclareDesign(DesignDirectiveSyntax design)
    {
        // `design winter` names the operating case and gives no driver a value (`D-143`). Recorded
        // here and checked after the whole file is read, because the `scenarios` line may follow it.
        if (design.Scenario is { } named)
        {
            _designScenario = (named.Token.Text, named.Span);
            return;
        }

        foreach (var argument in design.Arguments)
        {
            var written = argument.Name.Text;
            var role = ScheduleRoleRegistry.Resolve(written);

            // Keyed by the role rather than the spelling, which is the whole of `D-59`: `design
            // tout=-26` and `design outdoor=-26` are the same design point, and a curve driven by
            // either name finds it.
            var key = role?.CanonicalName ?? written;

            if (_design.TryGetValue(key, out var existing))
            {
                Report(
                    BinderDiagnostics.DuplicateBinding,
                    argument.Span,
                    ("name", written),
                    ("line", LineOf(existing.Span)));
                continue;
            }

            var id = new ValueId.Design(key);
            _graph.Add(id);
            _pending[id] = new PendingValue(argument.Value, id, argument.Span, null)
            {
                DesignRole = role,
                IsDesign = true,
            };

            _design[key] = new DesignValue(written, role, null, null, argument.Span);
        }
    }

    /// <summary>Finds the solve mode of the circuit a value was written in.</summary>
    /// <returns>Static when nothing places the value, which is the language's own default.</returns>
    private FluidMode ModeOf(ValueId id)
    {
        var circuit = id switch
        {
            ValueId.ComponentParameter parameter
                when _componentsByName.TryGetValue(parameter.Component, out var slot) =>
                _components[slot.Index].CircuitName,
            ValueId.Let let => _bindingCircuits.GetValueOrDefault(let.Name),
            _ => null,
        };

        return _circuits
            .FirstOrDefault(candidate => string.Equals(candidate.Name, circuit, StringComparison.Ordinal))
            ?.Mode ?? FluidMode.Static;
    }

    /// <summary>Settles which case the file operates at, once the whole file has been read (<c>D-143</c>).</summary>
    /// <returns>The scenario name, or <see langword="null"/> when no scenarios are declared.</returns>
    /// <remarks>
    /// Two errors and no repair. A <c>design</c> naming a case that does not exist is <c>FS1542</c>;
    /// scenarios with no <c>design</c> at all is <c>FS1543</c>, and **the first name is not taken as a
    /// default** -- it is a position, and reading a position as a choice would make reordering the
    /// <c>scenarios</c> line silently change which case the canvas draws. A <c>design</c> naming a
    /// case in a file with no scenarios is left alone: it binds nothing, and the file is a `D-58` file
    /// whose driver form this is not.
    /// </remarks>
    private string? SettleDesignScenario()
    {
        if (_scenarios.Count == 0)
        {
            return null;
        }

        if (_designScenario is not { } named)
        {
            Report(
                BinderDiagnostics.DesignScenarioMissing,
                _scenarioSpan ?? default,
                ("count", _scenarios.Count.ToString(CultureInfo.InvariantCulture)),
                ("first", _scenarios[0]));
            return null;
        }

        if (!_scenarios.Contains(named.Name, StringComparer.Ordinal))
        {
            Report(
                BinderDiagnostics.UnknownDesignScenario,
                named.Span,
                ("name", named.Name),
                ("names", string.Join(", ", _scenarios)));
            return null;
        }

        return named.Name;
    }

    /// <summary>Writes each design value's evaluated result back, for the model to carry.</summary>
    private ImmutableDictionary<string, DesignValue> PublishDesign()
    {
        var published = ImmutableDictionary.CreateBuilder<string, DesignValue>(StringComparer.Ordinal);

        foreach (var (key, entry) in _design)
        {
            _pending.TryGetValue(new ValueId.Design(key), out var pending);

            published[key] = entry with { Value = pending?.Value, Number = DesignNumber(key) };
        }

        return published.ToImmutable();
    }
}
