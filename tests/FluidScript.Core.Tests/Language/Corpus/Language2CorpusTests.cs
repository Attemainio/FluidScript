using System.Globalization;
using System.Text;

using FluidScript.Core.Diagnostics;
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
/// The goldens were written from the translated path: the translation at the parse, then the binder. The direct front
/// end replaces it in the binder and must bind every item to the same golden, the translation's diagnostics included. A golden changes only with its reason stated
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

    /// <summary>Parses and binds language 2 text as the pipeline does, whichever path that is.</summary>
    /// <returns>The model, and the parse's diagnostics with the binder's: the translation raises its own at the parse, and the direct path in the binder.</returns>
    private static (SemanticModel Model, IEnumerable<Diagnostic> Diagnostics) Bind(string text)
    {
        var source = new SourceText(text);
        var parse = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse);

        return (bound.Model, parse.Diagnostics.Concat(bound.Diagnostics)
            .OrderBy(static d => d.Span?.Start ?? 0)
            .ThenBy(static d => d.Code, StringComparer.Ordinal)
            .ThenBy(static d => d.Message, StringComparer.Ordinal));
    }

    /// <summary>
    /// The golden: the model's shape, then what <see cref="ModelShape"/> leaves out because it compares two languages
    /// and this compares one -- every <c>let</c>, every deferred expression, every <c>show</c> line -- then every
    /// diagnostic as a user meets it.
    /// </summary>
    private static string Render((SemanticModel Model, IEnumerable<Diagnostic> Diagnostics) result)
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
