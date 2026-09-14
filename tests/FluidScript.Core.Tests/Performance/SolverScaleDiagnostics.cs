using System.Diagnostics;
using System.Globalization;
using System.Text;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>
/// The solver-scale baselines <c>D-45</c> deferred from M0: 200 solver unknowns, and how the 800-unknown
/// limit behaves — which in Core is <em>support</em>, because the refusal is an input limit the API
/// returns by metadata (<c>07</c>) and ships with tier 40.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is generated rather than kept as a file: the distribution header with <c>n</c> pumped
/// consumers on one supply/return pair, each drawing 24 kW at 50 → 30 °C through its own mixing valve and
/// pump. Every consumer adds four nodes, four branches and one promotion — thirteen unknowns — so the
/// unknown count is a function of <c>n</c> and the report says which <c>n</c> it measured.
/// </para>
/// <para>
/// Traited <c>Diagnostic</c>: it writes <c>diagnostics/solver-scale.md</c> and takes seconds. The numbers
/// go into <c>benchmarks/reference-environment.json</c> by a reviewed edit, never by this test.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class SolverScaleDiagnostics
{
    private const int Warmup = 2;
    private const int Runs = 5;

    private sealed record Row(
        int Consumers,
        int Declarations,
        int Unknowns,
        int Equations,
        double PrepareMs,
        double SolveMs,
        int Iterations,
        int Passes,
        string Termination,
        double Residual);

    [Fact]
    public async Task HowTheSolverScales()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var rows = new List<Row>();

        foreach (var consumers in new[] { 2, 8, 15, 30, 61 })
        {
            rows.Add(await Measure(consumers, resolved.Value.Catalog));
        }

        var report = Path.Combine(RepositoryLayout.Diagnostics, "solver-scale.md");

        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(report, Render(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.All(rows, static row => Assert.Equal("Converged", row.Termination));
    }

    /// <summary>The header with <paramref name="consumers"/> pumped consumers, as a script.</summary>
    public static string Header(int consumers)
    {
        var text = new StringBuilder()
            .AppendLine("fluidscript 1")
            .AppendLine(CultureInfo.InvariantCulture, $"project static scale_{consumers}")
            .AppendLine()
            .AppendLine("circuit heating 100")
            .AppendLine("fluid water")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"HS1 heat_exchanger power={24 * consumers} kW out=60")
            .AppendLine()
            .AppendLine("connections")
            .AppendLine("N1 - HS1 - N3")
            .AppendLine("N5 - N1")
            .AppendLine()
            .AppendLine("N1 node p=250");

        for (var i = 1; i <= consumers; i++)
        {
            text.AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"circuit c{i} {100 + i}")
                .AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"HE{i} load in=50 out=30 power=24 kW")
                .AppendLine(CultureInfo.InvariantCulture, $"TV{i} three_way_valve")
                .AppendLine(CultureInfo.InvariantCulture, $"PU{i} pump")
                .AppendLine(CultureInfo.InvariantCulture, $"PA{i} pipe length=12 dn=25")
                .AppendLine(CultureInfo.InvariantCulture, $"PB{i} pipe length=12 dn=25")
                .AppendLine()
                .AppendLine("connections")
                .AppendLine(CultureInfo.InvariantCulture, $"N3 - PA{i} - TV{i}.a")
                .AppendLine(CultureInfo.InvariantCulture, $"NM{i} - TV{i}.b")
                .AppendLine(CultureInfo.InvariantCulture, $"TV{i}.ab - PU{i} - HE{i} - NM{i}")
                .AppendLine(CultureInfo.InvariantCulture, $"NM{i} - PB{i} - N5");
        }

        return text.ToString();
    }

    private static async Task<Row> Measure(int consumers, ICatalog<PipeSpec> catalog)
    {
        var source = Header(consumers);
        var model = GraphFixture.Bind(source);
        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog), 10);

        var prepared = loop.Prepare(model, Water.Instance, "scale");
        var counting = WellPosedness.Check(prepared.Lowered.Graph).Counting;

        var prepare = Time(() => loop.Prepare(model, Water.Instance, "scale"));

        OuterLoopResult? last = null;
        var solve = Time(() =>
        {
            var run = loop.RunAsync(model, Water.Instance, "scale", TestContext.Current.CancellationToken)
                .GetAwaiter().GetResult();

            Assert.True(run.IsSuccess, run.Error?.Message);
            last = run.Value;
        });

        await Task.CompletedTask;

        return new Row(
            consumers,
            model.Components.Count(static c => c.Origin is FluidScript.Core.Binding.Origin.Declared),
            counting.Unknowns,
            counting.Equations,
            prepare,
            solve,
            last!.Solve.Iterations,
            last.Passes,
            last.Solve.Termination.ToString(),
            last.Solve.ResidualNorm);
    }

    private static double Time(Action action)
    {
        for (var index = 0; index < Warmup; index++)
        {
            action();
        }

        var clock = Stopwatch.StartNew();

        for (var index = 0; index < Runs; index++)
        {
            action();
        }

        return clock.Elapsed.TotalMilliseconds / Runs;
    }

    private static string Render(List<Row> rows)
    {
        var text = new StringBuilder()
            .AppendLine("# Solver scale")
            .AppendLine()
            .AppendLine("Generated by `SolverScaleDiagnostics`. **Numbers here are bound to the machine and the")
            .AppendLine("build below and mean nothing without them.**")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: .NET {Environment.Version}")
            .AppendLine("- Build: Debug — a release build is materially faster; do not quote these as production numbers")
            .AppendLine(CultureInfo.InvariantCulture, $"- Sampling: {Warmup} warm-up passes, then {Runs} timed")
            .AppendLine()
            .AppendLine("The fixture is the distribution header with *n* pumped consumers, each 24 kW at 50 → 30 °C")
            .AppendLine("through its own mixing valve and pump: thirteen unknowns per consumer. `prepare` is the")
            .AppendLine("bootstrap, closure and first sizing pass; `solve` is the whole outer loop from the bound model.")
            .AppendLine()
            .AppendLine("| Consumers | Decls | Unknowns | Equations | Prepare ms | Solve ms | Iters | Passes | Termination | Residual |")
            .AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|");

        foreach (var row in rows)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {row.Consumers} | {row.Declarations} | {row.Unknowns} | {row.Equations} | {row.PrepareMs:F1} | {row.SolveMs:F1} "
                + $"| {row.Iterations} | {row.Passes} | {row.Termination} | {row.Residual:E2} |");
        }

        return text.ToString();
    }
}
