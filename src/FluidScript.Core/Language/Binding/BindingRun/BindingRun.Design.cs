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
    /// <param name="scenarios">The line.</param>
    /// <remarks>
    /// Order is kept exactly as written, because that order is the whole binding: element <c>i</c> of
    /// every list belongs to the name at position <c>i</c>. A duplicate is refused rather than
    /// collapsed -- two cases called <c>summer</c> would give a size a basis naming a case that
    /// cannot be looked up -- and the repeat is skipped so the positions of the rest do not shift.
    /// </remarks>
    private void DeclareScenarios(CaseNames scenarios)
    {
        foreach (var (name, span) in scenarios.Names)
        {
            if (_scenarios.Contains(name, StringComparer.Ordinal))
            {
                Report(BinderDiagnostics.DuplicateScenario, span, ("name", name));
                continue;
            }

            _scenarios.Add(name);
        }
    }

    /// <summary>Records the case the file operates at: the first of <c>cases</c> (<c>D-143</c>, <c>19</c>).</summary>
    private void DeclareDesign(DesignLine design) => _designScenario = design.Case;

    /// <summary>The case the file operates at, or <see langword="null"/> when it declares no cases (<c>D-143</c>).</summary>
    private string? SettleDesignScenario()
    {
        if (_scenarios.Count == 0)
        {
            return null;
        }

        // The reader names the first case as the design case in the same breath as it declares the cases, so a
        // file with cases always has one, and it is always among them (`19` §The project block).
        return _designScenario?.Name;
    }
}
