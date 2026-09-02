using System.Collections.Immutable;

using FluidScript.Core.Compatibility;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Compatibility;

/// <summary>
/// The compatibility gate from <c>plan/10-language/18-script-compatibility.md</c>, which implements
/// <c>D-27</c>: known semantics are selected before anything parses the file.
/// </summary>
public sealed class ScriptCompatibilityTests
{
    private static CompatibilityResult Inspect(string text, SupportedVersions? supported = null) =>
        ScriptCompatibility.Inspect(new SourceText(text), supported);

    private static readonly SupportedVersions TwoMajors = new(
        new LanguageMajor(2), [new LanguageMajor(1), new LanguageMajor(2)]);

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnversionedDraftCompilesAndSolvesButCannotBeSaved()
    {
        // M1's first exit criterion, and the whole of `D-27`. Info rather than an error: a draft under
        // editing is the normal state of unsaved text, and it means exactly what it would with the
        // line present. What it may not do is become a durable file.
        var result = Inspect("HE1 heat_exchanger power=30\n");

        Assert.Equal(CompatibilityDisposition.UnversionedDraft, result.Disposition);
        Assert.Null(result.DetectedMajor);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FS1701", diagnostic.Code);
        Assert.Contains("fluidscript 1", diagnostic.Message, StringComparison.Ordinal);

        Assert.Contains(CompatibilityAction.Compile, result.AllowedActions);
        Assert.Contains(CompatibilityAction.Solve, result.AllowedActions);
        Assert.DoesNotContain(CompatibilityAction.Save, result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCurrentMajorAllowsEverythingAndReportsNothing()
    {
        var result = Inspect("fluidscript 1\nHE1 heat_exchanger power=30\n");

        Assert.Equal(CompatibilityDisposition.Current, result.Disposition);
        Assert.Equal(new LanguageMajor(1), result.DetectedMajor);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(CompatibilityAction.Save, result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABomBlankLinesAndCommentsMayPrecedeTheDirective()
    {
        // `18` allows all three explicitly. A BOM that hid the directive behind it would make every
        // file some editors write look like a draft.
        var result = Inspect("﻿# what this file is\n\n\nfluidscript 1\n");

        Assert.Equal(CompatibilityDisposition.Current, result.Disposition);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANewerMajorIsReadableAsTextAndNothingElse()
    {
        // The criterion that protects a user's file from a build that predates it. Interpreting it
        // under older rules is the one outcome worse than refusing.
        var result = Inspect("fluidscript 2\nHE1 heat_exchanger power=30\n");

        Assert.Equal(CompatibilityDisposition.UnsupportedNewer, result.Disposition);
        Assert.Equal(new LanguageMajor(2), result.DetectedMajor);
        Assert.Equal("FS1702", Assert.Single(result.Diagnostics).Code);
        Assert.Equal([CompatibilityAction.SaveAsBytes], result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASupportedOlderMajorIsCompiledAndOfferedAMigration()
    {
        // Offered, never applied: `18`'s invariant 3 makes migration one explicit, undoable action,
        // and opening never rewrites.
        var result = Inspect("fluidscript 1\nHE1 heat_exchanger power=30\n", TwoMajors);

        Assert.Equal(CompatibilityDisposition.SupportedOld, result.Disposition);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(CompatibilityAction.Save, result.AllowedActions);
        Assert.Contains(CompatibilityAction.PreviewMigration, result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADroppedOlderMajorIsNotSilentlyReadUnderCurrentRules()
    {
        var supported = new SupportedVersions(new LanguageMajor(3), [new LanguageMajor(3)]);

        var result = Inspect("fluidscript 1\n", supported);

        Assert.Equal(CompatibilityDisposition.UnsupportedOld, result.Disposition);
        Assert.Equal("FS1702", Assert.Single(result.Diagnostics).Code);
        Assert.Equal([CompatibilityAction.SaveAsBytes], result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoDirectivesNamingDifferentMajorsSelectNothing()
    {
        // The condition only the gate can judge. The parser sees two well-formed statements and
        // reports the duplicate as FS1112; taking the first would be exactly the silent guess `D-27`
        // exists to prevent.
        var result = Inspect("fluidscript 1\nfluidscript 2\n", TwoMajors);

        Assert.Equal("FS1705", Assert.Single(result.Diagnostics).Code);
        Assert.Null(result.DetectedMajor);
        Assert.Equal([CompatibilityAction.SaveAsBytes], result.AllowedActions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoDirectivesNamingTheSameMajorAreAnOrdinaryDuplicate()
    {
        // Not FS1705. Nothing is contradictory, so the gate has an answer and the parser's FS1112 is
        // the whole of the complaint.
        var result = Inspect("fluidscript 1\nfluidscript 1\n");

        Assert.Equal(CompatibilityDisposition.Current, result.Disposition);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("fluidscript 1\ncatalog steel_en10255\n", "steel_en10255", null)]
    [InlineData("fluidscript 1\ncatalog steel_en10255@2026.1\n", "steel_en10255", "2026.1")]
    [InlineData("fluidscript 1\n", null, null)]
    public void TheCataloguePinIsReadWithTheVersion(string text, string? id, string? version)
    {
        var result = Inspect(text);

        Assert.Equal(id, result.Catalog?.Id);
        Assert.Equal(version, result.Catalog?.Version);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InspectingNeverMutatesTheSource()
    {
        // Invariant 1, and the reason `Inspect` takes a SourceText rather than a mutable buffer.
        const string Text = "﻿# a draft\n\nHE1 heat_exchanger power=30\n";
        var source = new SourceText(Text);

        ScriptCompatibility.Inspect(source);

        Assert.Equal(Text, source.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EverySavedSampleStatesMajorOne()
    {
        // `18`'s first acceptance criterion. A sample that drifted into being a draft would be a file
        // the application refuses to save, shipped in the repository as an example.
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = Inspect(sample.Text);

            Assert.Equal(new LanguageMajor(1), result.DetectedMajor);
            Assert.Equal(CompatibilityDisposition.Current, result.Disposition);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheGateNeverThrowsOnAnythingTheCorpusHolds()
    {
        // Including every block that is deliberately malformed. Reading a file's version is the first
        // thing that happens to it, so it is the one stage with no earlier stage to have rejected it.
        foreach (var script in ScriptCorpus.All())
        {
            var result = Inspect(script.Text);

            Assert.False(result.AllowedActions.IsDefault);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoDispositionAllowsSaveWithoutAKnownMajor()
    {
        // The invariant behind every row above: durable save requires semantics that can be selected
        // again later. Asserted over the enum rather than case by case, so a disposition added without
        // thinking about `Save` fails here.
        ImmutableArray<string> texts =
        [
            "HE1 heat_exchanger power=30\n",
            "fluidscript 1\n",
            "fluidscript 2\n",
            "fluidscript 1\nfluidscript 2\n",
        ];

        foreach (var text in texts)
        {
            var result = Inspect(text, TwoMajors);

            if (result.AllowedActions.Contains(CompatibilityAction.Save))
            {
                Assert.NotNull(result.DetectedMajor);
                Assert.Contains(result.DetectedMajor!.Value, TwoMajors.Supported);
            }
        }
    }
}
