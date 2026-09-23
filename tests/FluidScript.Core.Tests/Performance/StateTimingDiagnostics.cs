using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Fixtures;

using SharpProp;

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
    private readonly List<string> _agreement = [];
    private readonly List<string> _unavailable = [];

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

        MeasureBackendSplit(waterEnthalpy.SiValue);
        MeasureBackends(waterEnthalpy.SiValue);

        var report = Path.Combine(RepositoryLayout.Diagnostics, "fluid-state-timings.md");
        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(report, Render(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(report));
        Assert.All(_rows, row => Assert.All(row.PerCall, sample => Assert.True(sample > 0)));
        Assert.NotEmpty(_agreement);
    }

    /// <summary>Where a water fix's time goes below <c>ISubstance</c>: the backend's flash against the property reads that follow it.</summary>
    /// <remarks>
    /// <c>PropertyBackend</c> updates one <c>Fluid</c> in place and then reads seven properties off it, each
    /// read a native call of its own; <c>Water.Build</c> wraps the result in quantities. Timing the update
    /// alone, then the update with one read, then with all seven, attributes the (p, h) cost between the
    /// flash and the reads -- SharpProp clears its lazy property cache on every update, so a read cannot be
    /// timed without the update in front of it, and each row below is the update plus what it names.
    /// </remarks>
    private void MeasureBackendSplit(double enthalpy)
    {
        var fluid = new Fluid(FluidsList.Water);
        var pressure = Input.Pressure(UnitsNet.Pressure.FromPascals(101_325));

        static Input Enthalpy(double joulesPerKilogram) =>
            Input.Enthalpy(UnitsNet.SpecificEnergy.FromJoulesPerKilogram(joulesPerKilogram));

        static Input Temperature(double celsius) =>
            Input.Temperature(UnitsNet.Temperature.FromDegreesCelsius(celsius));

        Measure("SharpProp Fluid (water)", "(p, T) update only", i => fluid.Update(pressure, Temperature(40 + (i % 20))));
        Measure("SharpProp Fluid (water)", "(p, h) update only", i => fluid.Update(pressure, Enthalpy(enthalpy + (i % 20))));
        Measure("SharpProp Fluid (water)", "(p, h) update + Temperature", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Temperature.Kelvins;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + Density", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Density.KilogramsPerCubicMeter;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + Entropy", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Entropy.JoulesPerKilogramKelvin;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + SpecificHeat", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.SpecificHeat.JoulesPerKilogramKelvin;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + DynamicViscosity", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.DynamicViscosity?.PascalSeconds;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + Conductivity", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Conductivity?.WattsPerMeterKelvin;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + Phase", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Phase;
        });
        Measure("SharpProp Fluid (water)", "(p, h) update + all seven reads and Phase", i =>
        {
            fluid.Update(pressure, Enthalpy(enthalpy + (i % 20)));
            _ = fluid.Temperature.Kelvins;
            _ = fluid.Enthalpy.JoulesPerKilogram;
            _ = fluid.Entropy.JoulesPerKilogramKelvin;
            _ = fluid.Density.KilogramsPerCubicMeter;
            _ = fluid.DynamicViscosity?.PascalSeconds;
            _ = fluid.SpecificHeat.JoulesPerKilogramKelvin;
            _ = fluid.Conductivity?.WattsPerMeterKelvin;
            _ = fluid.Phase;
        });
        Measure("SharpProp Fluid (water)", "(p, T) update + all seven reads and Phase", i =>
        {
            fluid.Update(pressure, Temperature(40 + (i % 20)));
            _ = fluid.Temperature.Kelvins;
            _ = fluid.Enthalpy.JoulesPerKilogram;
            _ = fluid.Entropy.JoulesPerKilogramKelvin;
            _ = fluid.Density.KilogramsPerCubicMeter;
            _ = fluid.DynamicViscosity?.PascalSeconds;
            _ = fluid.SpecificHeat.JoulesPerKilogramKelvin;
            _ = fluid.Conductivity?.WattsPerMeterKelvin;
            _ = fluid.Phase;
        });
    }

    /// <summary>What the other CoolProp backends SharpProp ships charge for the same water state, and how far each sits from HEOS.</summary>
    /// <remarks>
    /// <para>
    /// <c>Water</c> measures through <c>HEOS::Water</c>, the IAPWS-95 Helmholtz equation, whose (p, h)
    /// flash is an iteration and costs what the split above shows. CoolProp also ships IF97, the
    /// industrial formulation with explicit backward equations; the incompressible fits; and tabular
    /// interpolation over HEOS gridded on (p, h), which is the form <c>D-79</c> allows. Each is timed on
    /// the two pairs a solve uses, and its answer at 60 °C and 300 kPa absolute compared with HEOS, so the
    /// speed and the price of it are read together. A backend that will not construct, or a table that
    /// will not build, is listed rather than silently skipped; a table's first use builds it, which is
    /// the cold call.
    /// </para>
    /// <para>
    /// SharpProp keeps CoolProp's own <c>AbstractState</c> internal, so this reaches it by reflection and
    /// compiles the calls into delegates once, which keeps the reflection out of the timed loop.
    /// </para>
    /// </remarks>
    private void MeasureBackends(double enthalpy)
    {
        if (CoolPropState.Bind() is not { } bind)
        {
            _unavailable.Add("CoolProp's `AbstractState` is not reachable in this SharpProp build; the backend comparison did not run.");

            return;
        }

        // One probe state per fluid, chosen where the model meets it: liquid water on a header; the
        // refrigerants as superheated vapour on the suction side of D-78's cycle, each safely above its
        // saturation temperature at that pressure and CO2 below its critical pressure; the glycol as a
        // 30 % ethylene-glycol brine at a chiller's supply temperature. INCOMP fluids have no phase
        // query and are read without it.
        var probes = new (string Fluid, string[] Backends, double Pressure, double Temperature)[]
        {
            ("Water", ["HEOS", "IF97", "BICUBIC&HEOS", "TTSE&HEOS"], 300_000, 273.15 + 60),
            ("Ammonia", ["HEOS", "BICUBIC&HEOS", "TTSE&HEOS"], 400_000, 273.15 + 20),
            ("Propane", ["HEOS", "BICUBIC&HEOS", "TTSE&HEOS"], 400_000, 273.15 + 20),
            ("CO2", ["HEOS", "BICUBIC&HEOS", "TTSE&HEOS"], 3_500_000, 273.15 + 20),
        };

        MeasureBrine();

        foreach (var (fluid, backends, pressure, temperature) in probes)
        {
            double[]? reference = null;

            foreach (var backend in backends)
            {
                CoolPropState state;

                try
                {
                    state = bind(backend, fluid);
                    state.UpdatePT(pressure, temperature);
                    var probe = state.ReadAll();
                    state.UpdateHP(probe[1], pressure);
                    var back = state.ReadAll();
                    reference ??= probe;
                    _agreement.Add(Agreement($"{backend}::{fluid}", probe, back, reference));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    var message = (exception.InnerException ?? exception).Message.Split('\n')[0];
                    _unavailable.Add($"`{backend}::{fluid}` — {message}");

                    continue;
                }

                var label = $"CoolProp {backend}::{fluid}";
                var h = ReferenceEquals(fluid, "Water") ? enthalpy : reference[1];

                Measure(label, "(p, T) update + all seven reads and Phase", i =>
                {
                    state.UpdatePT(pressure, temperature - 10 + (i % 20));
                    state.ReadAll();
                });
                Measure(label, "(p, h) update + all seven reads and Phase", i =>
                {
                    state.UpdateHP(h + (i % 20), pressure);
                    state.ReadAll();
                });
            }
        }
    }

    /// <summary>A 30 % ethylene-glycol brine through SharpProp's own incompressible path, which takes the concentration the raw factory string does not.</summary>
    private void MeasureBrine()
    {
        try
        {
            var brine = new Fluid(FluidsList.MEG, UnitsNet.Ratio.FromPercent(30));
            var pressure = Input.Pressure(UnitsNet.Pressure.FromPascals(300_000));

            brine.Update(pressure, Input.Temperature(UnitsNet.Temperature.FromDegreesCelsius(7)));
            var enthalpy = brine.Enthalpy.JoulesPerKilogram;

            Measure("SharpProp INCOMP::MEG 30 %", "(p, T) update + six reads (no phase)", i =>
            {
                brine.Update(pressure, Input.Temperature(UnitsNet.Temperature.FromDegreesCelsius(i % 20)));
                _ = brine.Temperature.Kelvins;
                _ = brine.Enthalpy.JoulesPerKilogram;
                _ = brine.Entropy.JoulesPerKilogramKelvin;
                _ = brine.Density.KilogramsPerCubicMeter;
                _ = brine.DynamicViscosity?.PascalSeconds;
                _ = brine.SpecificHeat.JoulesPerKilogramKelvin;
                _ = brine.Conductivity?.WattsPerMeterKelvin;
            });
            Measure("SharpProp INCOMP::MEG 30 %", "(p, h) update + six reads (no phase)", i =>
            {
                brine.Update(pressure, Input.Enthalpy(UnitsNet.SpecificEnergy.FromJoulesPerKilogram(enthalpy + (i % 20))));
                _ = brine.Temperature.Kelvins;
                _ = brine.Enthalpy.JoulesPerKilogram;
                _ = brine.Entropy.JoulesPerKilogramKelvin;
                _ = brine.Density.KilogramsPerCubicMeter;
                _ = brine.DynamicViscosity?.PascalSeconds;
                _ = brine.SpecificHeat.JoulesPerKilogramKelvin;
                _ = brine.Conductivity?.WattsPerMeterKelvin;
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _unavailable.Add($"`INCOMP::MEG` at 30 % — {(exception.InnerException ?? exception).Message.Split('\n')[0]}");
        }
    }

    private static string Agreement(string backend, double[] probe, double[] back, double[] reference)
    {
        static string Relative(double value, double against) =>
            against == 0 ? "—" : ((value - against) / against).ToString("E1", CultureInfo.InvariantCulture);

        return $"| `{backend}` | {Relative(probe[3], reference[3])} | {Relative(probe[1], reference[1])} | {Relative(probe[5], reference[5])} "
            + $"| {Relative(probe[4], reference[4])} | {Relative(probe[6], reference[6])} | {(back[0] - probe[0]).ToString("E1", CultureInfo.InvariantCulture)} K |";
    }

    /// <summary>CoolProp's <c>AbstractState</c> behind SharpProp, bound once by reflection into delegates.</summary>
    private sealed class CoolPropState
    {
        private readonly Action<double, double> _updatePT;
        private readonly Action<double, double> _updateHP;
        private readonly Func<double>[] _reads;

        private CoolPropState(Action<double, double> updatePT, Action<double, double> updateHP, Func<double>[] reads)
        {
            _updatePT = updatePT;
            _updateHP = updateHP;
            _reads = reads;
        }

        public static Func<string, string, CoolPropState>? Bind()
        {
            var assembly = typeof(Fluid).Assembly;
            var stateType = assembly.GetTypes().FirstOrDefault(static t => t.Name == "AbstractState");
            var pairsType = assembly.GetTypes().FirstOrDefault(static t => t.Name == "input_pairs");
            var factory = stateType?.GetMethod("factory", [typeof(string), typeof(string)]);
            var update = stateType?.GetMethod("update", [pairsType!, typeof(double), typeof(double)]);

            if (stateType is null || pairsType is null || factory is null || update is null)
            {
                return null;
            }

            var pt = Enum.Parse(pairsType, "PT_INPUTS");
            var hp = Enum.Parse(pairsType, "HmassP_INPUTS");
            var names = new[] { "T", "hmass", "smass", "rhomass", "viscosity", "cpmass", "conductivity" };
            var phase = stateType.GetMethod("phase", Type.EmptyTypes);

            return (backend, fluid) =>
            {
                var instance = factory.Invoke(null, [backend, fluid])!;
                var self = System.Linq.Expressions.Expression.Constant(instance);
                var a = System.Linq.Expressions.Expression.Parameter(typeof(double));
                var b = System.Linq.Expressions.Expression.Parameter(typeof(double));

                Action<double, double> Update(object pair) =>
                    System.Linq.Expressions.Expression.Lambda<Action<double, double>>(
                        System.Linq.Expressions.Expression.Call(self, update, System.Linq.Expressions.Expression.Constant(pair, pairsType), a, b), a, b).Compile();

                Func<double> Read(string name)
                {
                    var method = stateType.GetMethod(name, Type.EmptyTypes)
                        ?? throw new MissingMethodException(stateType.Name, name);

                    return System.Linq.Expressions.Expression.Lambda<Func<double>>(
                        System.Linq.Expressions.Expression.Call(self, method)).Compile();
                }

                var reads = names.Select(Read).ToList();

                if (phase is not null && !backend.StartsWith("INCOMP", StringComparison.Ordinal))
                {
                    reads.Add(System.Linq.Expressions.Expression.Lambda<Func<double>>(
                        System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Call(self, phase), typeof(double))).Compile());
                }

                return new CoolPropState(Update(pt), Update(hp), [.. reads]);
            };
        }

        public void UpdatePT(double pressure, double temperature) => _updatePT(pressure, temperature);

        public void UpdateHP(double enthalpy, double pressure) => _updateHP(enthalpy, pressure);

        public double[] ReadAll()
        {
            var values = new double[_reads.Length];

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = _reads[index]();
            }

            return values;
        }
    }

    private static Quantity Celsius(double value) =>
        Quantity.FromSi(value + 273.15, Dimension.Temperature);

    private static Quantity Fraction(double value) =>
        Quantity.FromSi(value, Dimension.Dimensionless);

    private void Measure(ISubstance substance, string operation, Action<int> call) =>
        Measure(Label(substance), operation, call);

    private void Measure(string label, string operation, Action<int> call)
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

        _rows.Add(new Row(label, operation, coldMicroseconds, perCall));
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

        text.AppendLine()
            .AppendLine("## The backends, against HEOS for the same fluid")
            .AppendLine()
            .AppendLine("Relative deviation of each backend's (p, T) answer from the same fluid's first backend")
            .AppendLine("(HEOS where it has one), and how far its own (p, h) round trip lands from the")
            .AppendLine("temperature it started at. Water at 60 °C and 300 kPa; the refrigerants as vapour at")
            .AppendLine("20 °C (ammonia and propane at 400 kPa, CO2 at 3.5 MPa); the brine at 7 °C and 300 kPa.")
            .AppendLine()
            .AppendLine("| Backend | ρ | h | cp | μ | k | (p, h) round trip |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|");

        foreach (var line in _agreement)
        {
            text.AppendLine(line);
        }

        if (_unavailable.Count > 0)
        {
            text.AppendLine().AppendLine("Not measured:").AppendLine();

            foreach (var line in _unavailable)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
            }
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
