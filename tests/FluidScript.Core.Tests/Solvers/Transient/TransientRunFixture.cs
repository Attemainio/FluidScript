using System.Globalization;
using System.Text;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>Runs a script in time and writes the whole run as a report, which is what a transient claim is measured on.</summary>
internal static class TransientRunFixture
{
    private static readonly Dictionary<string, string> Versions = new(StringComparer.Ordinal)
    {
        ["language"] = "1",
        ["catalogue"] = "test",
        ["properties"] = "test",
        ["contract"] = "test",
    };

    /// <summary>A run, every frame collected, and its report written under <c>diagnostics/transient/</c>.</summary>
    public sealed record Run(RunSnapshot Snapshot, IReadOnlyList<TransientFrame> Frames, string Report)
    {
        /// <summary>The differential state named like <c>PB#n4.h</c> at a frame, as a temperature in °C.</summary>
        public double Celsius(TransientFrame frame, string state)
        {
            var layout = Snapshot.System.Unknowns;
            var index = layout.Differential.ToList().FindIndex(s => string.Equals(s.Name, state, StringComparison.Ordinal));

            Assert.True(index >= 0, $"no differential state '{state}'");

            return Celsius(frame.Differential[index]);
        }

        /// <summary>An algebraic unknown named like <c>N2.h</c> at a frame, as a temperature in °C.</summary>
        public double NodeCelsius(TransientFrame frame, string unknown)
        {
            var layout = Snapshot.System.Unknowns;
            var declaration = Assert.Single(layout.Unknowns, u => string.Equals(u.Name, unknown, StringComparison.Ordinal));

            return Celsius(frame.State.Values[declaration.Index]);
        }

        /// <summary>An algebraic unknown's raw value at a frame.</summary>
        public double Value(TransientFrame frame, string unknown)
        {
            var declaration = Assert.Single(Snapshot.System.Unknowns.Unknowns, u => string.Equals(u.Name, unknown, StringComparison.Ordinal));

            return frame.State.Values[declaration.Index];
        }

        /// <summary>The steady solution of the post-schedule system, which a settled run must reproduce (<c>33</c> invariant 8).</summary>
        public async Task<StateVector> SteadyAfterAsync(CancellationToken cancellationToken)
        {
            var system = TransientSolver.SteadyAt(Snapshot, Snapshot.Settings.Horizon);
            var solve = await new NewtonSolver().SolveAsync(system, Frames[^1].State, null, cancellationToken);

            Assert.True(solve.Converged, solve.Termination.ToString());

            return solve.Solution;
        }

        /// <summary>The frame at a time, which must be one the run landed on.</summary>
        public TransientFrame At(double time) => Assert.Single(Frames, f => Math.Abs(f.Time - time) < 1e-9);

        private static double Celsius(double enthalpy)
        {
            var state = Water.Instance.FromPressureEnthalpy(Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(enthalpy, Dimension.Enthalpy));

            Assert.True(state.IsSuccess, state.Error?.Message);

            return state.Value.Temperature.SiValue - 273.15;
        }
    }

    public static async Task<Run> RunAsync(string name, string source, TransientSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var design = await loop.RunAsync(GraphFixture.Bind(source), Water.Instance, name, cancellationToken);

        Assert.True(design.IsSuccess, design.Error?.Message);
        Assert.True(design.Value.Solve.Converged, design.Value.Solve.Termination.ToString());

        var snapshot = RunSnapshot.Create(
            design.Value.Graph,
            WellPosedness.Check(design.Value.Graph),
            design.Value.Solve.Solution,
            settings ?? new TransientSettings(),
            "sha256:" + new string('0', 64),
            Versions);

        Assert.True(snapshot.IsSuccess, $"{snapshot.Error?.Code}: {snapshot.Error?.Message}");

        var frames = new List<TransientFrame>();
        var started = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var frame in new TransientSolver(new NewtonSolver()).RunAsync(snapshot.Value, cancellationToken))
        {
            frames.Add(frame);
        }

        started.Stop();

        var run = new Run(snapshot.Value, frames, string.Empty);
        var report = Report(name, run, started.Elapsed);
        var directory = Path.Combine(RepositoryLayout.Diagnostics, "transient");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".run.txt"), report, cancellationToken);

        return run with { Report = report };
    }

    public static async Task<Run> RunSampleAsync(string sample, TransientSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Samples, sample), cancellationToken);

        return await RunAsync(Path.GetFileNameWithoutExtension(sample), source, settings, cancellationToken);
    }

    private static string Report(string name, Run run, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        var layout = run.Snapshot.System.Unknowns;
        var settings = run.Snapshot.Settings;

        text.AppendLine(CultureInfo.InvariantCulture, $"=== {name}: horizon {settings.Horizon} s, interval {settings.FrameInterval} s, {run.Frames.Count} frames, {run.Frames.Sum(f => f.Steps)} steps, {elapsed.TotalMilliseconds:0} ms wall");
        text.AppendLine(CultureInfo.InvariantCulture, $"differential: {string.Join(", ", layout.Differential.Select((s, i) => $"{s.Name} ({run.Snapshot.ReferenceMasses[i]:0.###} kg)"))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"promotions frozen: {string.Join(", ", run.Snapshot.Posedness.Counting.Promotions.Select((p, i) => $"{p.Label}={run.Snapshot.PromotionInitial[i]:0.####}"))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"schedule: {string.Join("; ", run.Snapshot.Schedule.Select(c => $"{c.Component}.{c.Parameter} {c.From}..{c.To} s {c.FromValue?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-"}..{c.ToValue:0.###}"))}");
        text.AppendLine();
        text.Append("t s | min step s | steps | settled | drift");

        foreach (var state in layout.Differential)
        {
            text.Append(CultureInfo.InvariantCulture, $" | {state.Name} °C");
        }

        foreach (var unknown in layout.Unknowns.Where(u => u.Kind is UnknownKind.NodeEnthalpy && u.Index < layout.ComponentUnknownOffset && !layout.Differential.Any(d => d.Column == u.Index)))
        {
            text.Append(CultureInfo.InvariantCulture, $" | {unknown.Name} °C");
        }

        foreach (var unknown in layout.Unknowns.Where(u => u.Kind is UnknownKind.BranchFlow))
        {
            text.Append(CultureInfo.InvariantCulture, $" | {unknown.Name}");
        }

        text.AppendLine();

        foreach (var frame in run.Frames)
        {
            text.Append(CultureInfo.InvariantCulture, $"{frame.Time,6:0.##} | {frame.StepTaken,6:0.###} | {frame.Steps,3} | {(frame.Settled ? "yes" : "no "),3} | {frame.EnergyDrift,8:0.0e0}");

            for (var index = 0; index < frame.Differential.Length; index++)
            {
                text.Append(CultureInfo.InvariantCulture, $" | {run.Celsius(frame, layout.Differential[index].Name),7:0.00}");
            }

            foreach (var unknown in layout.Unknowns.Where(u => u.Kind is UnknownKind.NodeEnthalpy && u.Index < layout.ComponentUnknownOffset && !layout.Differential.Any(d => d.Column == u.Index)))
            {
                text.Append(CultureInfo.InvariantCulture, $" | {run.NodeCelsius(frame, unknown.Name),7:0.00}");
            }

            foreach (var unknown in layout.Unknowns.Where(u => u.Kind is UnknownKind.BranchFlow))
            {
                text.Append(CultureInfo.InvariantCulture, $" | {frame.State.Values[unknown.Index],8:0.0000}");
            }

            foreach (var diagnostic in frame.Diagnostics)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {diagnostic.Code}: {diagnostic.Message}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}
