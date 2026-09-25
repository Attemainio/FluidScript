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
    private static readonly string[] LayoutFolders = ["Ladder", "Variants", "Stress"];

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
    /// The scripts whose three-way valve language 1 labelled by order and sized by geometry (<c>L-68</c>). Each now
    /// states the ports the plant's shape gives (<c>D-175</c>: <c>a</c> the control path, <c>b</c> the bypass), which
    /// is what language 2 infers unwritten.
    /// </summary>
    private static readonly string[] ThreeWayLabelling =
    [
        "samples/m2-cooling-loop.fluid",
        "samples/m4-demand-step.fluid",
        "Ladder/step-06-cooling.fluid",
        "Ladder/step-06c-cooling-load.fluid",
        "Ladder/step-11a-two-loops.fluid",
        "Variants/step-06-cooling-controls.fluid",
        "Variants/step-06c-cooling-load-controls.fluid",
        "Variants/step-11a-two-loops-controls.fluid",
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

    /// <summary>The language 1 scripts that are files: the samples, and the layout ladder with its variants and stress cases.</summary>
    public static TheoryData<string> Files()
    {
        var layout = Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout");
        var files = ScriptCorpus.EnumerateSampleFiles()
            .Concat(LayoutFolders.SelectMany(folder => Directory.GetFiles(Path.Combine(layout, folder), "*.fluid")))
            .Select(RepositoryLayout.ToRelative)
            .Order(StringComparer.Ordinal);
        return [.. files];
    }

    [Theory]
    [MemberData(nameof(Files))]
    [Trait("Category", "Unit")]
    public void AScriptConvertsToTheSameModel(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root, file));
        var conversion = Language1Converter.Convert(text);
        var converted = Language1Converter.BindLanguage2(conversion.Text);

        if (Environment.GetEnvironmentVariable("FLUIDSCRIPT_CONVERT_OUT") is { Length: > 0 } dir)
        {
            var target = Path.Combine(dir, file.Replace('/', '_'));
            File.WriteAllText(target, conversion.Text);
            File.WriteAllText(target + ".gaps.txt", string.Join('\n', conversion.Gaps));
        }

        if (Rewritten(conversion))
        {
            return;
        }

        var original = new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(text)), "script");
        Assert.Equal(Errors(original.Diagnostics), Errors(converted.Diagnostics));

        var model = converted.Model.Runs.IsEmpty
            ? converted.Model
            : RunProjection.Project(converted.Model, converted.Model.Runs[0]);
        Assert.Equal(ModelShape.Of(conversion.Original, withRun: !converted.Model.Runs.IsEmpty), ModelShape.Of(model, withRun: !converted.Model.Runs.IsEmpty));
    }

    /// <summary>
    /// The converter writes a port only where language 2's inference would choose another, so a converted valve with no
    /// port written is one whose legs language 2 labels from the plant exactly as the original states them.
    /// </summary>
    [Theory]
    [MemberData(nameof(PlantLabelledValves))]
    [Trait("Category", "Unit")]
    public void AThreeWayValveIsLabelledByThePlantWithNothingWritten(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root, file));
        var conversion = Language1Converter.Convert(text);
        var valve = text.Contains("TV_C ", StringComparison.Ordinal) ? "TV_C" : "3WV";

        Assert.DoesNotContain($"{valve}.a", conversion.Text, StringComparison.Ordinal);
        Assert.DoesNotContain($"{valve}.b", conversion.Text, StringComparison.Ordinal);
        Assert.DoesNotContain($"{valve}.ab", conversion.Text, StringComparison.Ordinal);
    }

    public static TheoryData<string> PlantLabelledValves =>
    [
        .. ((IEnumerable<ITheoryDataRow>)Files()).Select(static row => (string)row.GetData()[0]!)
            .Where(static file => ThreeWayLabelling.Any(name => file.EndsWith(name, StringComparison.Ordinal))),
    ];

    [Theory]
    [MemberData(nameof(Files))]
    [Trait("Category", "Unit")]
    public async Task AScriptConvertsToTheSameSolve(string file)
    {
        // The whole report -- seeds, iterations, every node's state, every size chosen -- not just convergence.
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root, file));
        var conversion = Language1Converter.Convert(text);
        Assert.SkipUnless(conversion.Gaps.IsEmpty, "Says what language 2 does not (D-175); rewritten by hand at the switch.");
        var converted = Language1Converter.BindLanguage2(conversion.Text).Model;
        var model = converted.Runs.IsEmpty ? converted : RunProjection.Project(converted, converted.Runs[0]);

        var before = await Solved(conversion.Original);
        var after = await Solved(model);
        if (Environment.GetEnvironmentVariable("FLUIDSCRIPT_CONVERT_OUT") is { Length: > 0 } dir)
        {
            var target = Path.Combine(dir, file.Replace('/', '_'));
            File.WriteAllText(target + ".solve1.txt", before);
            File.WriteAllText(target + ".solve2.txt", after);
        }

        Assert.Equal(before, after);
    }

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
        // The proof above is worth only what the shape can see: a duty one kilowatt off, and an exchanger wired
        // with its sides the other way round, must each read as a different model.
        var conversion = Language1Converter.Convert(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-substation.fluid")));
        var shape = ModelShape.Of(Language1Converter.BindLanguage2(conversion.Text).Model, withRun: false);
        Assert.Equal(ModelShape.Of(conversion.Original, withRun: false), shape);

        var duty = conversion.Text.Replace("power = 150  primary", "power = 151  primary", StringComparison.Ordinal);
        var sides = conversion.Text
            .Replace("PCV - HX1.secondary.in", "PCV - HX1.primary.in", StringComparison.Ordinal)
            .Replace("SP - HX1.primary.in", "SP - HX1.secondary.in", StringComparison.Ordinal);

        Assert.NotEqual(duty, conversion.Text);
        Assert.NotEqual(sides, conversion.Text);
        Assert.NotEqual(shape, ModelShape.Of(Language1Converter.BindLanguage2(duty).Model, withRun: false));
        Assert.NotEqual(shape, ModelShape.Of(Language1Converter.BindLanguage2(sides).Model, withRun: false));
    }

    private static async Task<string> Solved(SemanticModel model)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        var run = await new OuterLoop(
                new NewtonSolver(),
                new CatalogBoreLookup(resolved.Value),
                OuterLoop.Rules(resolved.Value.Catalog),
                10)
            .RunAsync(model, Water.Instance, "script", TestContext.Current.CancellationToken);

        return run.IsSuccess
            ? Timing().Replace(SolveExplanation.Render(run.Value, "script"), "~ms")
            : "failed: " + run.Error?.Message;
    }

    [GeneratedRegex(@"[\d.]+ ms\b")]
    private static partial Regex Timing();

    private static string Errors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(", ", diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Code).Order(StringComparer.Ordinal));
}

/// <summary>A model as text, blind to spans and to what the two languages spell differently by design.</summary>
/// <remarks>
/// Left out: every span; a curve's driver, whose kind and name change by design (<c>curve heating outdoor</c> reads
/// <c>design tout=-26</c> through the role both name, and in language 2 names the <c>let tout</c> it became) and
/// whose effect is compared in every parameter that reads the curve; the
/// lets and design values themselves, whose effect is in every parameter that reads them; a component's origin. A
/// circuit's mode and the schedule are compared only when the conversion wrote a run, against the run's projection.
/// </remarks>
public static class ModelShape
{
    /// <summary>Renders a model.</summary>
    /// <param name="model">The model.</param>
    /// <param name="withRun">Whether to include the circuits' modes and the schedule.</param>
    /// <returns>One line per fact, in a stable order.</returns>
    public static string Of(SemanticModel model, bool withRun)
    {
        ArgumentNullException.ThrowIfNull(model);
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"project {model.Project.Name ?? "project"} cases [{string.Join(",", model.Project.Scenarios)}] design {model.Project.DesignScenario ?? model.Project.Scenarios.FirstOrDefault()} start {model.Project.Start}");
        text.AppendLine(CultureInfo.InvariantCulture, $"style spacing {model.Style.Spacing} default {model.Style.Default}");

        foreach (var circuit in model.Circuits)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"circuit {circuit.Name} {circuit.Number} {circuit.Substance} {circuit.Role.CanonicalName}{(withRun ? " " + circuit.Mode : string.Empty)}");
        }

        foreach (var component in model.Components.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"component {component.Name} {component.Kind?.Keyword} in {component.CircuitName} at {component.AttachedTo} tag {component.Tag} style {component.Style}");
            foreach (var (key, point) in component.SizingPoint.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  sized at {key} = {point.Number:G6}");
            }

            foreach (var (key, capacity) in component.Capacities.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  capacity {key} = {capacity.SiValue:G6}");
            }

            foreach (var (key, value) in component.Parameters.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {key} = {Value(value)}{(value.Scenarios.IsDefaultOrEmpty ? string.Empty : " [" + string.Join(" | ", value.Scenarios.Select(Value)) + "]")}");
            }
        }

        foreach (var line in model.Connections.Select(static c => $"connection {c.From.Component}.{c.From.Port} -> {c.To.Component}.{c.To.Port}").Order(StringComparer.Ordinal))
        {
            text.AppendLine(line);
        }

        foreach (var control in model.ControlBindings.OrderBy(static c => c.Controller.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"control {control.Controller.Name} moves {control.Actuator} reads {control.Measurement} setpoint {Quantity(control.Setpoint)} [{string.Join(" | ", control.Setpoints.Select(Quantity))}] band {Quantity(control.Band)} differential {Quantity(control.Differential)} output {Quantity(control.OutputLow)}..{Quantity(control.OutputHigh)} curve {control.Curve}");
        }

        foreach (var curve in model.Curves.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"curve {curve.Name} extrapolated {curve.IsExtrapolated} [{string.Join(" ", curve.Points.Select(static p => $"{p.X.ToString("R", CultureInfo.InvariantCulture)}:{p.Y.ToString("R", CultureInfo.InvariantCulture)}"))}]");
        }

        if (withRun)
        {
            foreach (var disturbance in model.Disturbances)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"event {disturbance.Circuit} {disturbance.Target} {Quantity(disturbance.From)}..{Quantity(disturbance.To)} {Quantity(disturbance.FromValue)} -> {Quantity(disturbance.ToValue)}");
            }
        }

        return text.ToString();
    }

    private static string Value(ParameterValue value) =>
        $"{Quantity(value.Value)} {value.Symbol} {value.Reference}";

    private static string Quantity(Quantity? quantity) =>
        quantity is { } q ? $"{q.SiValue.ToString("R", CultureInfo.InvariantCulture)} {q.Dimension}" : "-";
}
