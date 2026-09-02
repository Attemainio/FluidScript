using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using FluidScript.Core.Fluids;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>Measures what it costs to fix a fluid state, per substance and per property pair.</summary>
/// <remarks>
/// <para>
/// <strong>A diagnostic, not a budget.</strong> It asserts that every measurement ran and wrote a
/// report; it asserts nothing about how long anything took. A wall-clock threshold here would fail on
/// a loaded CI machine and pass on a fast one while measuring neither, and this repository already
/// knows its own numbers are environment-bound — <c>plan/00-foundation/defects.md</c> records that
/// measuring on WSL over <c>/mnt/c</c> distorts every timing. The report says which machine and which
/// build configuration produced it so a number is never read without them.
/// </para>
/// <para>
/// It exists because the property call count is <c>P3.6</c>'s budget: <c>21</c>'s per-solve cache and
/// CoolProp's IF97 backend are both levers whose value is a ratio between these numbers and the
/// number of calls a Newton iteration makes. Guessing at that ratio is how a cache gets built for a
/// cost that was never there.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class StateTimingDiagnostics
{
    /// <summary>How many timed samples each row reports.</summary>
    private const int Samples = 10;

    /// <summary>How many calls one sample averages over.</summary>
    /// <remarks>
    /// A single backend call is a few hundred microseconds — far above the stopwatch's resolution, so
    /// batching is not needed to *see* it. It is here to make the deviation mean something: ten single
    /// calls measure the scheduler as much as the code, and ten batches of twenty do not.
    /// </remarks>
    private const int Batch = 20;

    /// <summary>How many untimed calls run before the first sample.</summary>
    /// <remarks>
    /// Past tiered compilation's default promotion threshold, deliberately. At 20 the real backend's
    /// <c>(p, T)</c> row came back with a median of 239 µs, a minimum of 107 and a standard deviation of
    /// 71 — a spread that is the JIT still rewriting the method, not the property call varying.
    /// </remarks>
    private const int Warmup = 200;

    private static readonly Quantity Atmospheric = Quantity.FromSi(0, Dimension.Pressure);

    private readonly List<Row> _rows = [];

    private sealed record Row(string Substance, string Operation, double Cold, double[] PerCall);

    [Fact]
    public void HowLongDoesItTakeToFixAState()
    {
        var water = Water.Instance;
        var air = HumidAirSubstance.Instance;

        // Enthalpies are taken from the substance rather than written down, so a (p, h) row is fixing
        // the same state its (p, T) row did and the two are comparable.
        var waterEnthalpy = water.FromPressureTemperature(Atmospheric, Celsius(60)).Value.Enthalpy;
        var airEnthalpy = air.FromPressureTemperatureRelativeHumidity(
            Atmospheric, Celsius(24), Fraction(0.5)).Value.DryAirBasisEnthalpy;

        foreach (var substance in new ISubstance[]
                 {
                     water, ConstantPropertyWater.Instance, LinearPropertyWater.Instance, air,
                 })
        {
            var enthalpy = ReferenceEquals(substance, air) ? airEnthalpy : waterEnthalpy;

            // Each call walks the state a little so nothing is answered from a repeat of its input.
            Measure(substance, "(p, T)", i => substance.FromPressureTemperature(
                Atmospheric, Celsius(40 + (i % 20))));
            Measure(substance, "(p, h)", i => substance.FromPressureEnthalpy(
                Atmospheric, Quantity.FromSi(enthalpy.SiValue + (i % 20), Dimension.Enthalpy)));
            Measure(substance, "saturation p(T)", i => substance.SaturationPressure(
                Celsius(40 + (i % 20))));
        }

        Measure(air, "(p, T, RH)", i => air.FromPressureTemperatureRelativeHumidity(
            Atmospheric, Celsius(18 + (i % 12)), Fraction(0.5)));
        Measure(air, "(p, T, w)", i => air.FromPressureTemperatureHumidity(
            Atmospheric, Celsius(18 + (i % 12)), Fraction(0.008)));

        var report = Path.Combine(RepositoryLayout.Diagnostics, "fluid-state-timings.md");
        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(report, Render(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(report));
        Assert.All(_rows, row => Assert.All(row.PerCall, sample => Assert.True(sample > 0)));
    }

    private static Quantity Celsius(double value) =>
        Quantity.FromSi(value + 273.15, Dimension.Temperature);

    private static Quantity Fraction(double value) =>
        Quantity.FromSi(value, Dimension.Dimensionless);

    private void Measure(ISubstance substance, string operation, Action<int> call)
    {
        // The cold call is reported on its own because it is a different question. CoolProp loads its
        // tables lazily, so the first call through a pair carries that load and is not the number a
        // solver would ever see again -- but it *is* the number the very first keystroke after an
        // edit pays, which is why it is kept rather than warmed away silently.
        var cold = Stopwatch.GetTimestamp();
        call(0);
        var coldMicroseconds = Microseconds(Stopwatch.GetTimestamp() - cold);

        for (var warmup = 0; warmup < Warmup; warmup++)
        {
            call(warmup);
        }

        var perCall = new double[Samples];

        for (var sample = 0; sample < Samples; sample++)
        {
            var started = Stopwatch.GetTimestamp();

            for (var iteration = 0; iteration < Batch; iteration++)
            {
                call(iteration);
            }

            perCall[sample] = Microseconds(Stopwatch.GetTimestamp() - started) / Batch;
        }

        _rows.Add(new Row(Label(substance), operation, coldMicroseconds, perCall));
    }

    /// <summary>Names a substance in the report.</summary>
    /// <param name="substance">The substance measured.</param>
    /// <returns>Its type name, and its script name where the two differ.</returns>
    /// <remarks>
    /// <see cref="ISubstance.Name"/> alone is useless here: all three water implementations answer to
    /// <c>water</c> — deliberately, so a fixture reads identically whichever registry it runs against —
    /// and a table with three rows called <c>water</c> hides the one comparison this report exists to
    /// make.
    /// </remarks>
    private static string Label(ISubstance substance) =>
        substance.GetType().Name is var type && type == substance.Name
            ? type
            : $"{type} ({substance.Name})";

    private static double Microseconds(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);

        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static double StandardDeviation(double[] values)
    {
        // Sample standard deviation, n-1. Over ten samples it is a coarse instrument; it is here to
        // separate "steady" from "all over the place", not to support an interval.
        var mean = values.Average();
        var sum = values.Sum(value => (value - mean) * (value - mean));

        return Math.Sqrt(sum / (values.Length - 1));
    }

    private string Render()
    {
        var text = new StringBuilder();

        text.AppendLine("# Fluid state timings")
            .AppendLine()
            .AppendLine("Generated by `StateTimingDiagnostics`. **Numbers here are bound to the machine")
            .AppendLine("and the build below and mean nothing without them.**")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: {RuntimeInformation.FrameworkDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- OS: {RuntimeInformation.OSDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Architecture: {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores")
            .AppendLine(CultureInfo.InvariantCulture, $"- Build: {Configuration}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Sampling: {Samples} samples of {Batch} calls each, reported per call")
            .AppendLine()
            .AppendLine("`Cold` is the very first call, which carries the backend's lazy table load.")
            .AppendLine("It is excluded from every other column.")
            .AppendLine()
            .AppendLine("| Substance | Operation | Cold µs | Median µs | Mean µs | SD µs | Min µs | Max µs |")
            .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

        foreach (var row in _rows)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{row.Substance}` | {row.Operation} | {row.Cold:F1} | {Median(row.PerCall):F2} "
                + $"| {row.PerCall.Average():F2} | {StandardDeviation(row.PerCall):F2} "
                + $"| {row.PerCall.Min():F2} | {row.PerCall.Max():F2} |");
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
