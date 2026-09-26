using System.Globalization;
using System.Text;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Tests.Language.Conversion;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Corpus;

/// <summary>
/// The frozen reference for binding language 2 directly (<c>D-178</c>, <c>19</c> §Binding directly, step 6c): every
/// converted corpus item that binds without a gap, committed as its language 2 text, and a golden of what it binds to.
/// </summary>
/// <remarks>
/// The goldens were written from the translated path, which the binder's entry still delegates to; the direct front
/// end replaces that path and must bind every item to the same golden. A golden changes only with its reason stated
/// in the commit, item by item: a span the translation made up, a message <c>L-66</c> names, or a defect fixed.
/// </remarks>
[Trait("Category", "Golden")]
public sealed class Language2CorpusTests
{
    private const string UpdateVariable = "FLUIDSCRIPT_UPDATE_GOLDENS";

    /// <summary>Gets the folder holding each item's <c>.fluid</c> text and <c>.bound</c> golden.</summary>
    public static string Folder { get; } =
        Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Language", "Corpus");

    /// <summary>Gets every frozen item, by its file name.</summary>
    public static TheoryData<string> Items() =>
        [.. Directory.GetFiles(Folder, "*.fluid").Select(static path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal)];

    /// <summary>Names an item by where it came from: <c>samples/m2-cooling-loop.fluid</c> is <c>samples.m2-cooling-loop</c>, a block at line 88 of <c>docs/functions/node.md</c> is <c>docs.functions.node.L88</c>.</summary>
    /// <param name="name">The corpus name, a file's relative path or a block's <c>path:line</c>.</param>
    /// <returns>The file name, without extension.</returns>
    public static string Id(string name) =>
        name.Replace("tests/FluidScript.Core.Tests/Layout/", "layout/", StringComparison.Ordinal)
            .Replace(".fluid", string.Empty, StringComparison.Ordinal)
            .Replace(".md:", ".L", StringComparison.Ordinal)
            .Replace('/', '.');

    [Theory]
    [MemberData(nameof(Items))]
    public void AnItemBindsToItsGolden(string item)
    {
        var actual = Render(Bind(File.ReadAllText(Path.Combine(Folder, item + ".fluid"))));
        var path = Path.Combine(Folder, item + ".bound");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No golden for '{item}'. Run once with {UpdateVariable}=1 to create it, then review it.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>Binds language 2 text the way the pipeline's binder receives it: the parser's own tree (<c>D-177</c>).</summary>
    private static BindResult Bind(string text)
    {
        var source = new SourceText(text);
        return new Binder(ComponentRegistry.Default).Bind(
            MajorParser.ParseDirect(source, ScriptCompatibility.Inspect(source).DetectedMajor));
    }

    /// <summary>
    /// The golden: the model's shape, then what <see cref="ModelShape"/> leaves out because it compares two languages
    /// and this compares one -- every <c>let</c>, every deferred expression, every <c>show</c> line -- then every
    /// diagnostic as a user meets it.
    /// </summary>
    private static string Render(BindResult result)
    {
        var model = result.Model;
        var text = new StringBuilder(ModelShape.Of(model, withRun: !model.Runs.IsEmpty));

        foreach (var binding in model.Bindings)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"let {binding.Name} = {Format(binding.Value)} dimension {binding.Dimension}");
        }

        foreach (var deferred in model.Deferred)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"deferred {deferred.Target} = {deferred.Source?.ToString(deferred.Expression.Span).Trim()} clock {deferred.FollowsTheClock}");
        }

        foreach (var show in model.Visualizations)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"show [{string.Join(", ", show.Properties.Select(static p => p.Name))}] scale {show.Scale}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{diagnostic.Code} [{diagnostic.Span?.Start}..{diagnostic.Span?.End}] {diagnostic.Message}");
        }

        return text.ToString();
    }

    private static string Format(Quantity? quantity) =>
        quantity is { } q ? $"{q.SiValue.ToString("R", CultureInfo.InvariantCulture)} {q.Dimension}" : "-";
}
