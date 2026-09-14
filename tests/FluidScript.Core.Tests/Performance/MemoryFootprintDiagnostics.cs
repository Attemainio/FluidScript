using System.Globalization;
using System.Text;

using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>Where the memory goes between a script and a solved circuit, and whether any of it stays.</summary>
/// <remarks>
/// <para>
/// <strong>Two numbers per stage, because they answer different questions.</strong> Managed allocation
/// (<c>GC.GetTotalAllocatedBytes</c>) is what the code asked for and the GC will return; the working-set
/// delta is what the process actually holds afterwards. A stage whose working set grows far beyond
/// what it allocated is holding memory the GC cannot see — native, which in Core means the property
/// backend. <c>C-76</c> was exactly that shape: 18 MB allocated, 2.5 GB held.
/// </para>
/// <para>
/// <strong>The slope is the leak test.</strong> Solving the same script ten times over should hold no
/// more memory at the end than after the first; a per-solve growth is memory each solve keeps. The
/// report states it in MB per solve, and the test fails above <see cref="LeakBoundMbPerSolve"/> — a
/// bound loose enough for GC heap growth and tight enough that a single leaked native state per
/// residual sweep (hundreds of megabytes per solve) cannot pass.
/// </para>
/// <para>
/// Traited <c>Diagnostic</c>: it writes <c>diagnostics/memory-footprint.md</c>. The unit-tier guard
/// for the property backend itself is <c>Fluids/NativeMemoryTests</c>.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
[Collection(SoleOccupancy.Name)]
public sealed class MemoryFootprintDiagnostics
{
    private const int RepeatedSolves = 10;
    private const double LeakBoundMbPerSolve = 8;

    private sealed record Row(
        string Sample,
        int Declarations,
        int Unknowns,
        double BindAllocatedMb,
        double PrepareAllocatedMb,
        double SolveAllocatedMb,
        double SolveHeldMb,
        double GrowthMbPerSolve,
        double WorkingSetMb,
        double GcHeapMb,
        string Termination);

    private static IEnumerable<string> Samples() =>
        Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal).Select(Path.GetFileName)!;

    [Fact]
    public async Task WhereTheMemoryGoes()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var rows = new List<Row>();

        foreach (var sample in Samples())
        {
            rows.Add(await Measure(sample, resolved.Value.Catalog));
        }

        var report = Path.Combine(RepositoryLayout.Diagnostics, "memory-footprint.md");

        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(report, Render(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.All(
            rows,
            static row => Assert.True(
                row.GrowthMbPerSolve < LeakBoundMbPerSolve,
                $"{row.Sample}: each solve keeps {row.GrowthMbPerSolve:F1} MB; something holds memory across solves."));
    }

    private static async Task<Row> Measure(string sample, ICatalog<PipeSpec> catalog)
    {
        var text = new SourceText(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)));
        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog), 10);

        var (model, bindAllocated) = Allocated(() =>
            new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(text), sample).Model);

        var (prepared, prepareAllocated) = Allocated(() => loop.Prepare(model, Water.Instance, sample));
        var unknowns = Core.Topology.WellPosedness.Check(prepared.Lowered.Graph).Counting.Unknowns;

        // The first solve pays for everything lazily built -- JIT, the catalogue, the native fluid -- and
        // is measured on its own so that the repeated solves measure only what a solve keeps.
        var first = await Solve(loop, model, sample);
        var afterFirst = Native();

        var solveAllocated = 0.0;
        var termination = first.Termination;

        for (var index = 0; index < RepeatedSolves; index++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var run = await Solve(loop, model, sample);

            solveAllocated += (GC.GetTotalAllocatedBytes(precise: true) - before) / (1024.0 * 1024.0);
            termination = run.Termination;
        }

        var held = (Native() - afterFirst) / (1024.0 * 1024.0);

        return new Row(
            sample,
            model.Components.Count(static c => c.Origin is Origin.Declared),
            unknowns,
            bindAllocated,
            prepareAllocated,
            solveAllocated / RepeatedSolves,
            held,
            held / RepeatedSolves,
            Environment.WorkingSet / (1024.0 * 1024.0),
            GC.GetGCMemoryInfo().HeapSizeBytes / (1024.0 * 1024.0),
            termination);
    }

    private static async Task<(string Termination, bool Reached)> Solve(OuterLoop loop, SemanticModel model, string sample)
    {
        var run = await loop.RunAsync(model, Water.Instance, sample, TestContext.Current.CancellationToken);

        // A sample the counting check refuses is a legitimate measurement too: its memory is bind and
        // prepare, and the row says it never reached the solver.
        return run.IsSuccess ? (run.Value.Solve.Termination.ToString(), true) : ("refused", false);
    }

    /// <summary>What the process holds outside the managed heap: working set less what the GC has committed.</summary>
    private static long Native() => Environment.WorkingSet - GC.GetGCMemoryInfo().TotalCommittedBytes;

    private static (T Result, double AllocatedMb) Allocated<T>(Func<T> action)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = action();

        return (result, (GC.GetTotalAllocatedBytes(precise: true) - before) / (1024.0 * 1024.0));
    }

    private static string Render(List<Row> rows)
    {
        var text = new StringBuilder()
            .AppendLine("# Memory footprint")
            .AppendLine()
            .AppendLine("Generated by `MemoryFootprintDiagnostics`. **Numbers here are bound to the machine and the")
            .AppendLine("build below and mean nothing without them.**")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: .NET {Environment.Version}, GC {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}")
            .AppendLine("- Build: Debug")
            .AppendLine(CultureInfo.InvariantCulture, $"- Protocol: bind and prepare once; one solve to warm up; then {RepeatedSolves} solves, allocation averaged and working-set growth divided by the count")
            .AppendLine()
            .AppendLine("`Allocated` is managed allocation the GC will return. `Held after solves` is what the process")
            .AppendLine("still holds after the repeated solves, and `per solve` is that as a slope: memory each solve keeps.")
            .AppendLine("A `held` far above `allocated` is memory the GC cannot see -- native, which in Core is the")
            .AppendLine("property backend (`C-76`). `Working set` and `GC heap` are the process totals at the end of the")
            .AppendLine("row; their difference is everything native, the runtime included.")
            .AppendLine()
            .AppendLine("| Sample | Decls | Unknowns | Bind alloc MB | Prepare alloc MB | Solve alloc MB | Held after solves MB | per solve MB | Working set MB | GC heap MB | Termination |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var row in rows)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"| `{row.Sample}` | {row.Declarations} | {row.Unknowns} | {row.BindAllocatedMb:F1} | {row.PrepareAllocatedMb:F1} "
                + $"| {row.SolveAllocatedMb:F1} | {row.SolveHeldMb:F0} | {row.GrowthMbPerSolve:F1} | {row.WorkingSetMb:F0} | {row.GcHeapMb:F0} | {row.Termination} |");
        }

        return text.ToString();
    }
}
