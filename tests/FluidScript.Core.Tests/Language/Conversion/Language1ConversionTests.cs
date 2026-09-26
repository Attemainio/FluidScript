using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Diagnostics.Explanations;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Conversion;

/// <summary>
/// `D-174` rule 3.1: every language 1 script the project owns converts to language 2, and the converted script binds
/// to the same model through today's translated path (<c>P6.11</c> package 5).
/// </summary>
public sealed partial class Language1ConversionTests
{
    /// <summary>
    /// What language 1 says and language 2 does not, each dropped or respelled by <c>D-175</c>: a script whose gaps are all
    /// of these is rewritten by hand at the switch (package 7) or with the docs (package 9), not converted. Any other gap
    /// fails.
    /// </summary>
    private static readonly string[] Dropped =
    [
        "is driven by another curve",
        "named style",
        "style for the components that follow it",
        "a second show line",
        "attachment '",
        "is not the first case",
        "controls nothing",

        // Documentation fragments, rewritten with the docs (package 9) rather than converted.
        "which nothing states",
    ];

    /// <summary>
    /// The fenced language 1 blocks in <c>plan/</c> and <c>docs/</c> that language 1 binds without an error; a block that
    /// shows a mistake (<c>expects=</c>) is rewritten with the docs, not converted.
    /// </summary>
    public static TheoryData<string> Blocks() =>
        [.. ScriptCorpus.MarkdownBlocks()
            .Where(static block => block.Language == 1 && block.Expected.IsEmpty)
            .Where(static block => !new Binder(ComponentRegistry.Default)
                .Bind(FluidScriptParser.Parse(new SourceText(block.Text)), "script")
                .Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            .Select(static block => block.Name)];

    [Theory]
    [MemberData(nameof(Blocks))]
    [Trait("Category", "Unit")]
    public void AMarkdownBlockConvertsToTheSameModel(string block)
    {
        var text = ScriptCorpus.MarkdownBlocks().Single(b => b.Name == block).Text;
        var conversion = Language1Converter.Convert(text);
        if (Rewritten(conversion))
        {
            return;
        }

        var converted = Language1Converter.BindLanguage2(conversion.Text);
        var run = !converted.Model.Runs.IsEmpty;
        var model = run ? RunProjection.Project(converted.Model, converted.Model.Runs[0]) : converted.Model;

        Assert.Equal("", Errors(converted.Diagnostics));
        Assert.Equal(ModelShape.Of(conversion.Original, run), ModelShape.Of(model, run));
    }

    /// <summary>Whether a conversion's gaps are all open decisions; fails on any gap that is not one.</summary>
    private static bool Rewritten(Conversion conversion)
    {
        var unexpected = conversion.Gaps.Where(gap => !Dropped.Any(decision => gap.Contains(decision, StringComparison.Ordinal))).ToList();
        Assert.True(unexpected.Count == 0, "Gaps that are no open decision:\n" + string.Join('\n', unexpected));
        return !conversion.Gaps.IsEmpty;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheShapeSeesAChangedParameterAndSwappedSides()
    {
        // The proofs that compare shapes are worth only what the shape can see: a duty one kilowatt off, and an exchanger wired
        // with its sides the other way round, must each read as a different model.
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-substation.fluid"));
        var shape = ModelShape.Of(Language1Converter.BindLanguage2(text).Model, withRun: false);

        var duty = text.Replace("power = 150  primary", "power = 151  primary", StringComparison.Ordinal);
        var sides = text
            .Replace("PCV - HX1.secondary.in", "PCV - HX1.primary.in", StringComparison.Ordinal)
            .Replace("SP - HX1.primary.in", "SP - HX1.secondary.in", StringComparison.Ordinal);

        Assert.NotEqual(duty, text);
        Assert.NotEqual(sides, text);
        Assert.NotEqual(shape, ModelShape.Of(Language1Converter.BindLanguage2(duty).Model, withRun: false));
        Assert.NotEqual(shape, ModelShape.Of(Language1Converter.BindLanguage2(sides).Model, withRun: false));
    }

    private static string Errors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(", ", diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Code).Order(StringComparer.Ordinal));
}
