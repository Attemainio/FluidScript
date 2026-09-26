using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Conversion;

/// <summary>
/// The binder's direct path against the translated one, on the converted corpus (<c>P6.11</c> package 6, <c>19</c>
/// §Binding directly): every file and markdown block the conversion proof converts, bound both ways, must give the same
/// model and the same diagnostics. The translation is the reference until package 7 deletes it, with this harness.
/// </summary>
public sealed class DirectBindingTests
{
    /// <summary>Binds language 2 text the way the pipeline does today: parsed, translated, then bound.</summary>
    private static BindResult Translated(string text)
    {
        var source = new SourceText(text);
        return new Binder(ComponentRegistry.Default).Bind(
            MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default));
    }

    /// <summary>Binds language 2 text on the direct path: the parser's own tree, read by the binder.</summary>
    private static BindResult Direct(string text)
    {
        var source = new SourceText(text);
        return new Binder(ComponentRegistry.Default).Bind(
            MajorParser.ParseDirect(source, ScriptCompatibility.Inspect(source).DetectedMajor));
    }

    /// <summary>Every diagnostic as a user meets it: the code, where it lands, and what it says.</summary>
    private static string Said(BindResult result) =>
        string.Join('\n', result.Diagnostics.Select(static d => $"{d.Code} [{d.Span?.Start}..{d.Span?.End}] {d.Message}"));

    private static void AssertTheSame(string language1)
    {
        var conversion = Language1Converter.Convert(language1);
        Assert.SkipUnless(conversion.Gaps.IsEmpty, "Says what language 2 does not (D-175); rewritten by hand at the switch.");

        var translated = Translated(conversion.Text);
        var direct = Direct(conversion.Text);
        var withRun = !translated.Model.Runs.IsEmpty;

        Assert.Equal(ModelShape.Of(translated.Model, withRun), ModelShape.Of(direct.Model, withRun));
        Assert.Equal(Said(translated), Said(direct));
    }

    [Theory]
    [MemberData(nameof(Language1ConversionTests.Files), MemberType = typeof(Language1ConversionTests))]
    [Trait("Category", "Unit")]
    public void AFileBindsDirectlyAsItDoesTranslated(string file) =>
        AssertTheSame(File.ReadAllText(Path.Combine(RepositoryLayout.Root, file)));

    [Theory]
    [MemberData(nameof(Language1ConversionTests.Blocks), MemberType = typeof(Language1ConversionTests))]
    [Trait("Category", "Unit")]
    public void ABlockBindsDirectlyAsItDoesTranslated(string block) =>
        AssertTheSame(ScriptCorpus.MarkdownBlocks().Single(b => b.Name == block).Text);

    /// <summary>
    /// Writes the frozen corpus (<c>D-178</c>): every item the conversion turns into language 2 without a gap, as its
    /// text. Runs only with <c>FLUIDSCRIPT_FREEZE_CORPUS=1</c>, once; the corpus is then the reference, and this goes
    /// with the converter at package 7.
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void TheCorpusIsFrozenFromTheConversion()
    {
        if (Environment.GetEnvironmentVariable("FLUIDSCRIPT_FREEZE_CORPUS") != "1")
        {
            return;
        }

        static IEnumerable<string> Names(TheoryData<string> rows) =>
            ((IEnumerable<ITheoryDataRow>)rows).Select(static row => (string)row.GetData()[0]!);

        var sources = Names(Language1ConversionTests.Files())
            .Select(static file => (Name: file, Text: File.ReadAllText(Path.Combine(RepositoryLayout.Root, file))))
            .Concat(Names(Language1ConversionTests.Blocks())
                .Select(static block => (Name: block, ScriptCorpus.MarkdownBlocks().Single(b => b.Name == block).Text)));

        Directory.CreateDirectory(Corpus.Language2CorpusTests.Folder);

        foreach (var (name, text) in sources)
        {
            if (Language1Converter.Convert(text) is { Gaps.IsEmpty: true } conversion)
            {
                File.WriteAllText(Path.Combine(Corpus.Language2CorpusTests.Folder, Corpus.Language2CorpusTests.Id(name) + ".fluid"), conversion.Text);
            }
        }
    }

    /// <summary>The direct path is taken only for a language 2 tree the parser produced, never for the translation's.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyAnUntranslatedLanguage2TreeTakesTheDirectPath()
    {
        const string Text = "fluidscript 2\ncircuit \"c\":\n  fluid = water\n  PU1 pump\n  N1 - PU1 - N1\n";
        var source = new SourceText(Text);

        var raw = MajorParser.ParseDirect(source, ScriptCompatibility.Inspect(source).DetectedMajor);
        var translated = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);

        Assert.Equal((2, false), (raw.Language, raw.Translated));
        Assert.Equal((2, true), (translated.Language, translated.Translated));
        Assert.DoesNotContain(Direct(Text).Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }
}
