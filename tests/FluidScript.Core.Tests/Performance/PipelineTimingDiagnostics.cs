using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>Where the time goes between a script and a solved circuit.</summary>
/// <remarks>
/// <para>
/// Two questions the corpus could not answer before. **How long does a script take to compile**, which
/// <c>07</c> budgets at 150 ms p95 for a 200-declaration draft and which nothing had ever measured;
/// and **how long does the Newton solve take**, which turns out to be almost entirely a question about
/// the fluid backend rather than about the linear algebra.
/// </para>
/// <para>
/// <strong>The run counts are set by the property call, not by the stopwatch.</strong>
/// <c>fluid-state-timings.md</c> puts one real <c>Water</c> state at about 2.2 ms, so a residual
/// evaluation costs milliseconds and a Jacobian costs <c>N+1</c> of them. A first version of this file
/// used 2000 residual evaluations per sample per substance — sized for a microsecond call, which is
/// minutes at 2.2 ms, and it was killed by the test timeout twice before the arithmetic was checked.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class PipelineTimingDiagnostics
{
    /// <summary>Untimed passes before each measurement, past tiered compilation's promotion threshold.</summary>
    private const int Warmup = 3;

    /// <summary>Timed passes for a stage measured end to end.</summary>
    private const int StageRuns = 5;

    /// <summary>Timed passes for one residual evaluation against constant properties.</summary>
    private const int CheapRuns = 200;

    /// <summary>Timed passes for one residual evaluation against the real backend.</summary>
    /// <remarks>Twenty, because each one is milliseconds. Two hundred here is three minutes.</remarks>
    private const int CostlyRuns = 20;

    private readonly List<Stage> _stages = [];
    private readonly List<Step> _steps = [];

    private sealed record Stage(
        string Sample,
        int Declarations,
        double Parse,
        double Bind,
        double Lower,
        double SolveConstant,
        double SolveWater,
        string Iterations,
        string Passes,
        string Termination);

    private sealed record Step(
        string Sample,
        int Unknowns,
        int Rows,
        int NodeColumns,
        double Constant,
        double Water,
        double JacobianConstant,
        double JacobianWater,
        double Lu);

    [Fact]
    public async Task WhereDoesTheTimeGo()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        foreach (var path in Directory.EnumerateFiles(RepositoryLayout.Samples, "*.fluid")
            .OrderBy(static candidate => candidate, StringComparer.Ordinal))
        {
            await MeasureStages(path, resolved.Value);
            MeasureStep(path, resolved.Value);
        }

        var report = Path.Combine(RepositoryLayout.Diagnostics, "pipeline-timings.md");

        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(report, Render(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(report));
        Assert.NotEmpty(_stages);
        Assert.All(_stages, stage => Assert.True(stage.Parse > 0));
    }

    private async Task MeasureStages(string path, ResolvedCatalog<PipeSpec> catalog)
    {
        var name = Path.GetFileName(path);
        var source = File.ReadAllText(path);
        var text = new SourceText(source);
        var parse = Time(() => FluidScriptParser.Parse(text), StageRuns);
        var syntax = FluidScriptParser.Parse(text);
        var bind = Time(() => new Binder(ComponentRegistry.Default).Bind(syntax, name), StageRuns);
        var model = new Binder(ComponentRegistry.Default).Bind(syntax, name).Model;
        var loop = Loop(catalog);

        var lower = Time(() => loop.Prepare(model, ConstantPropertyWater.Instance), StageRuns);
        var constant = await TimeAsync(
            () => loop.RunAsync(model, ConstantPropertyWater.Instance, name, CancellationToken.None),
            StageRuns);
        var water = await TimeAsync(
            () => loop.RunAsync(model, Water.Instance, name, CancellationToken.None), StageRuns);

        var run = await loop.RunAsync(model, Water.Instance, name, CancellationToken.None);

        _stages.Add(new Stage(
            name,
            model.Components.Length,
            parse,
            bind,
            lower,
            constant,
            water,
            run.IsSuccess ? run.Value.Solve.Iterations.ToString(CultureInfo.InvariantCulture) : "—",
            run.IsSuccess ? run.Value.Passes.ToString(CultureInfo.InvariantCulture) : "—",
            run.IsSuccess ? run.Value.Solve.Termination.ToString() : "refused"));
    }

    private void MeasureStep(string path, ResolvedCatalog<PipeSpec> catalog)
    {
        var name = Path.GetFileName(path);
        var source = File.ReadAllText(path);
        var constant = Residual(source, catalog, ConstantPropertyWater.Instance, CheapRuns);
        var water = Residual(source, catalog, Water.Instance, CostlyRuns);

        if (constant is not { } cheap || water is not { } costly)
        {
            return;
        }

        _steps.Add(new Step(
            name,
            cheap.Unknowns,
            cheap.Rows,
            cheap.NodeColumns,
            cheap.PerCall,
            costly.PerCall,
            cheap.Jacobian,
            costly.Jacobian,
            Lu(cheap.Unknowns)));
    }

    /// <summary>One residual evaluation, which is the unit a Jacobian is <c>N+1</c> of.</summary>
    private static (int Unknowns, int Rows, int NodeColumns, double PerCall, double Jacobian)? Residual(
        string source, ResolvedCatalog<PipeSpec> catalog, ISubstance substance, int runs)
    {
        // Lowered with the substance under test: `GraphFixture.Lower` pins constant properties, and what
        // the real backend costs is the whole question here.
        var graph = Loop(catalog).Prepare(GraphFixture.Bind(source), substance).Lowered.Graph;
        var posedness = WellPosedness.Check(graph);
        var layout = SystemLayout.Build(graph, posedness.Counting);
        var seed = SolutionSeed.Build(graph, layout);
        EquationSystem system;

        try
        {
            system = EquationSystem.Build(graph, posedness, seed);
        }
#pragma warning disable CA1031 // A sample the count refuses assembles or it does not; either is a row or no row.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }

        var residuals = new double[system.Rows];
        var x = seed.Values.ToArray();
        var perCall = Time(() => system.TryEvaluateResiduals(x, residuals), runs);

        // The Jacobian as `NewtonSolver.Jacobian` actually builds it: the scaled base residual once, then
        // one `TryEvaluateScaledAt` per column, which re-fixes only the node the column moves (S-2). The
        // first version of this table multiplied the base residual by N+1 instead, which is the naive
        // cost and not the one the solver pays (C-68's re-measurement).
        var scaled = new double[system.Rows];
        var perturbed = new double[system.Rows];
        var trial = new double[system.Columns];
        var nodeColumns = Enumerable.Range(0, system.Columns).Count(column => system.NodeOfUnknown(column) >= 0);

        Assert.True(system.TryEvaluateScaled(x, scaled));

        var jacobian = Time(
            () =>
            {
                Array.Copy(x, trial, x.Length);

                for (var column = 0; column < system.Columns; column++)
                {
                    var scale = system.UnknownScales[column];
                    var delta = Tolerances.NewtonFiniteDifferenceStep * Math.Max(Math.Abs(x[column]), scale);
                    trial[column] = x[column] + delta;
                    Assert.True(system.TryEvaluateScaledAt(trial, column, perturbed));
                    trial[column] = x[column];
                }
            },
            Math.Max(1, runs / 4));

        return (system.Columns, system.Rows, nodeColumns, perCall, jacobian);
    }

    /// <summary>A dense LU of the same order, which is the other half of a Newton step.</summary>
    private static double Lu(int order)
    {
        var matrix = new double[order * order];
        var scratch = new double[matrix.Length];
        var rhs = new double[order];
        var random = new Random(7);

        for (var index = 0; index < matrix.Length; index++)
        {
            matrix[index] = random.NextDouble();
        }

        // Diagonally dominant, so the factorisation is not measuring a pivot search that never converges.
        for (var index = 0; index < order; index++)
        {
            matrix[(index * order) + index] += order;
        }

        return Time(
            () =>
            {
                matrix.CopyTo(scratch, 0);
                DenseLu.Factor(scratch, order).Solve(rhs);
            },
            CheapRuns);
    }

    private static OuterLoop Loop(ResolvedCatalog<PipeSpec> catalog) => new(
        new NewtonSolver(),
        new CatalogBoreLookup(catalog),
        OuterLoop.Rules(catalog.Catalog),
        10);

    private static double Time(Action action, int runs)
    {
        for (var index = 0; index < Warmup; index++)
        {
            action();
        }

        var clock = Stopwatch.StartNew();

        for (var index = 0; index < runs; index++)
        {
            action();
        }

        return clock.Elapsed.TotalMilliseconds / runs;
    }

    private static async Task<double> TimeAsync(Func<Task> action, int runs)
    {
        for (var index = 0; index < Warmup; index++)
        {
            await action();
        }

        var clock = Stopwatch.StartNew();

        for (var index = 0; index < runs; index++)
        {
            await action();
        }

        return clock.Elapsed.TotalMilliseconds / runs;
    }

    private string Render()
    {
        var text = new StringBuilder();

        text.AppendLine("# Pipeline timings")
            .AppendLine()
            .AppendLine("Generated by `PipelineTimingDiagnostics`. **Numbers here are bound to the machine")
            .AppendLine("and the build below and mean nothing without them.**")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: {RuntimeInformation.FrameworkDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- OS: {RuntimeInformation.OSDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Architecture: {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores")
            .AppendLine(CultureInfo.InvariantCulture, $"- Build: {Configuration}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Sampling: {Warmup} warm-up passes, then {StageRuns} timed for a stage and {CheapRuns}/{CostlyRuns} for a residual")
            .AppendLine()
            .AppendLine("## Stages, mean ms per pass")
            .AppendLine()
            .AppendLine("`solve` is the whole outer loop — sizing passes and Newton solves together — run")
            .AppendLine("twice over the same circuit with only the substance swapped. A sample the counting")
            .AppendLine("check refuses still parses, binds and lowers; it has no iterations to report.")
            .AppendLine()
            .AppendLine("| Sample | Decls | Parse | Bind | Lower | Solve (constant) | Solve (water) | Iters | Passes | Termination |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var stage in _stages)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{stage.Sample}` | {stage.Declarations} | {stage.Parse:F2} | {stage.Bind:F2} "
                + $"| {stage.Lower:F2} | {stage.SolveConstant:F2} | {stage.SolveWater:F1} "
                + $"| {stage.Iterations} | {stage.Passes} | {stage.Termination} |");
        }

        text.AppendLine()
            .AppendLine("## Inside one Newton step, mean ms")
            .AppendLine()
            .AppendLine("`Residual` is one full `TryEvaluateResiduals`, which fixes a state at every node.")
            .AppendLine("`Jacobian` is the finite-difference sweep as `NewtonSolver` builds it: one")
            .AppendLine("`TryEvaluateScaledAt` per column, re-fixing only the node that column moves --")
            .AppendLine("`Node cols` of the N columns are a node pressure or enthalpy and fix one state each;")
            .AppendLine("the rest fix none. `Naive (water)` is the base residual times N+1, what a sweep that")
            .AppendLine("re-fixed every node per column would cost, kept for comparison with the earlier")
            .AppendLine("editions of this table that reported it as the Jacobian. `LU` factors and solves a")
            .AppendLine("dense system of the same order, and is the only part of a step that is linear algebra.")
            .AppendLine()
            .AppendLine("| Sample | N | Rows | Node cols | Residual (constant) | Residual (water) | Jacobian (constant) | Jacobian (water) | Naive (water) | LU |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var step in _steps)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{step.Sample}` | {step.Unknowns} | {step.Rows} | {step.NodeColumns} | {step.Constant:F3} | {step.Water:F2} "
                + $"| {step.JacobianConstant:F2} | {step.JacobianWater:F1} | {step.Water * (step.Unknowns + 1):F1} "
                + $"| {step.Lu:F3} |");
        }

        return text.ToString();
    }

    private static string Configuration =>
#if DEBUG
        "Debug — a release build is materially faster; do not quote these as production numbers";
#else
        "Release";
#endif
}
