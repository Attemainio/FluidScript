using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using FluidScript.Fixtures;

using SharpProp;

using UnitsNet;

namespace FluidScript.Core.Tests.Performance;

/// <summary>
/// Which input pairs the property backend supports, across every fluid family it offers, and what
/// each one costs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A discovery, not an assertion.</strong> CoolProp's supported input pairs are not one set:
/// HEOS, INCOMP, a multi-component mixture and humid air each refuse a different subset, and this
/// repository had been generalising from a single data point — <c>(T, h)</c> reported as "not yet
/// supported" while writing <c>P3.1</c>. This enumerates the whole grid and reports the backend's own
/// message for every pair it turns down.
/// </para>
/// <para>
/// <strong>A pair that returns a state is not the same as a pair that returns the right one.</strong>
/// Every cell is fixed from values read off one reference state, so a correct answer reproduces that
/// reference. The <c>ΔT</c> column is there because a pair that silently lands somewhere else is worse
/// than one that refuses.
/// </para>
/// <para>
/// <strong>The families come from SharpProp's own metadata, not from a list written here.</strong>
/// <c>FluidsListExtensions</c> exposes each fluid's backend and its fraction range, so "incompressible
/// solution" means a fluid whose fraction range is an interval rather than a name someone recognised.
/// The one split the metadata cannot make is pure against pseudo-pure — <c>Pure()</c> is documented as
/// true for both — so that group is named by hand and the report says so.
/// </para>
/// <para>
/// It names SharpProp directly, as <c>SharpPropSpikeTests</c> does and for the same reason: it probes
/// the backend rather than using it as a property source, and the architecture rule allowing one
/// referencing type is scoped to <c>src/</c>. No production code grows for glycol, mixtures or
/// refrigerants, all of which are post-v1 by <c>D-28</c>.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class BackendPairDiagnostics
{
    /// <summary>How many timed samples each cell reports.</summary>
    /// <remarks>
    /// Five, not ten. A single mixture flash can take seconds, and the question every cell answers is
    /// which order of magnitude it lives in — a tighter median would cost minutes and change no
    /// conclusion.
    /// </remarks>
    private const int Samples = 5;

    /// <summary>How long the whole probe may run before it stops measuring.</summary>
    /// <remarks>
    /// The backstop, and the only bound that does not depend on guessing what a call costs. Cells past
    /// it are reported as unmeasured rather than dropped, so the report says what it did not do.
    /// </remarks>
    private static readonly TimeSpan RunBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long one backend call may take before the probe stops waiting for it.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Some flashes never return.</strong> Measured: <c>(h, s)</c> on a water-ethanol HEOS
    /// mixture iterates without converging and without a limit of its own, and it took an append-as-you-go
    /// log to find that, because a run that never finishes writes no report.
    /// </para>
    /// <para>
    /// The call cannot be cancelled — it is native, and there is no token to pass it. What this does is
    /// stop <em>waiting</em>: the call runs on a background thread, so the probe records the timeout and
    /// moves on, and the orphaned thread cannot keep the process alive. The risk it accepts is an
    /// abandoned call still holding a backend lock, which would make later cells time out too; the
    /// report shows that plainly as a run of timeouts rather than one.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Roughly how long one sample should take, in microseconds.</summary>
    /// <remarks>
    /// The batch size is chosen per cell to hit this rather than fixed, because the whole question here
    /// is a spread of two or three orders of magnitude between families. One batch size for every cell
    /// would drown the fast rows in timer noise or make the slow ones take minutes.
    /// </remarks>
    private const double SampleTargetMicroseconds = 2000;

    /// <summary>Roughly how long the untimed warm-up before the samples should take, in microseconds.</summary>
    private const double WarmupTargetMicroseconds = 10_000;

    /// <summary>Above this cost per call, a cell drops to three samples instead of ten.</summary>
    /// <remarks>
    /// A coarser median on a slow cell is the right trade: the question it answers is "which order of
    /// magnitude", and ten samples of a 100 ms flash buys a third decimal place for a second per cell.
    /// </remarks>
    private const double SlowCallMicroseconds = 20_000;

    /// <summary>Above this cost per call, a cell is reported from its cold call alone.</summary>
    private const double UnsamplableMicroseconds = 250_000;

    /// <summary>How many fluids each metadata-derived family contributes.</summary>
    /// <remarks>
    /// One. This is a sub-selection on purpose: the grid is fluids × ten pairs, a slow cell costs
    /// seconds, and probing four fluids per family to learn the same thing about the family is time
    /// spent for nothing. Taken in enum order, so the same fluid appears on every machine.
    /// </remarks>
    private const int PerFamily = 1;

    /// <summary>Names CoolProp models with a pseudo-pure equation of state.</summary>
    /// <remarks>
    /// By hand, because SharpProp's <c>Pure()</c> is documented as true for pure <em>and</em>
    /// pseudo-pure alike. Anything here this SharpProp version does not know is reported as not probed
    /// rather than skipped silently.
    /// </remarks>
    private static readonly string[] PseudoPure = ["Air"];

    /// <summary>Names the pure fluids the grid is run against.</summary>
    /// <remarks>Chosen for spread — a liquid, an alcohol, a refrigerant, a gas — not for plant relevance.</remarks>
    private static readonly string[] Pure = ["Water"];

    private enum Property
    {
        Temperature,
        Pressure,
        Enthalpy,
        Entropy,
        Density,
    }

    private enum AirProperty
    {
        Temperature,
        Humidity,
        RelativeHumidity,
        Enthalpy,
        Entropy,
        WetBulbTemperature,
        DewTemperature,
    }

    private sealed record Cell(
        string Family,
        string Fluid,
        string Pair,
        int Batch,
        double Cold,
        double[] PerCall,
        double TemperatureError,
        string? Failure);

    private sealed record Candidate(
        string Family,
        string Label,
        Func<IKeyedInput<CoolProp.parameters>, IKeyedInput<CoolProp.parameters>, IFluidState> Fix);

    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly List<Cell> _cells = [];
    private readonly List<string> _unavailable = [];

    [Fact]
    public void WhichInputPairsDoesEachFluidFamilySupportAndWhatDoTheyCost()
    {
        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(
            LogPath,
            $"# Backend pair probe log{Environment.NewLine}{Environment.NewLine}"
            + $"Started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC. A line without a matching result is"
            + $" the call the run did not come back from.{Environment.NewLine}{Environment.NewLine}");

        // Humid air first, and the candidate list is ordered cheapest family first, because the budget
        // can only stop work between calls. A single mixture flash is not interruptible, so the way to
        // guarantee the cheap families are measured is to measure them before anything slow starts.
        ProbeHumidAir();

        foreach (var candidate in Candidates())
        {
            Probe(candidate);
        }

        Log($"{Environment.NewLine}Finished in {_elapsed.Elapsed.TotalSeconds:F1} s.");

        var report = Path.Combine(RepositoryLayout.Diagnostics, "backend-pair-matrix.md");
        File.WriteAllText(report, Render(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(report));

        // The only real assertion: the pure-fluid baseline has to work, or the run measured nothing and
        // every "refused" in the report is an artefact of a broken harness rather than a fact about
        // CoolProp.
        Assert.Contains(
            _cells,
            cell => cell.Fluid == "Water" && cell.Pair == "(T, p)" && cell.Failure is null);
    }

    private List<Candidate> Candidates()
    {
        var all = Enum.GetValues<FluidsList>();
        var candidates = new List<Candidate>();

        foreach (var name in Pure.Concat(PseudoPure))
        {
            var family = Pure.Contains(name, StringComparer.Ordinal) ? "Pure (HEOS)" : "Pseudo-pure";

            if (Named(all, name) is { } fluid)
            {
                Add(candidates, family, fluid, null);
            }
            else
            {
                _unavailable.Add($"`{name}` ({family}) — this SharpProp version has no such fluid.");
            }
        }

        foreach (var fluid in Family(all, solution: false))
        {
            Add(candidates, "Incompressible, pure", fluid, null);
        }

        foreach (var fluid in Family(all, solution: true))
        {
            // Mid-range, so the concentration is inside the correlation's stated interval whatever it is.
            Add(
                candidates,
                "Incompressible solution",
                fluid,
                (fluid.FractionMin().DecimalFractions + fluid.FractionMax().DecimalFractions) / 2);
        }

        foreach (var (first, second, percent) in new[]
                 {
                     ("Water", "Ethanol", 60.0),
                 })
        {
            AddMixture(candidates, all, first, second, percent);
        }

        return candidates;
    }

    private static FluidsList? Named(FluidsList[] all, string name) =>
        all.Where(fluid => string.Equals(fluid.ToString(), name, StringComparison.Ordinal))
            .Cast<FluidsList?>()
            .FirstOrDefault();

    /// <summary>Selects incompressible fluids, split into solutions and substances.</summary>
    /// <param name="all">Every fluid this SharpProp version exposes.</param>
    /// <param name="solution">Whether to take the ones that need a concentration.</param>
    /// <returns>At most <see cref="PerFamily"/> of them, in enum order.</returns>
    /// <remarks>
    /// <strong>Split on <c>Pure()</c>, not on the fraction range.</strong> The first version of this
    /// used <c>FractionMax() &gt; FractionMin()</c>, and the census it produced said every one of the
    /// 305 HEOS fluids and all 120 INCOMP ones takes a fraction — which cannot be true of pure water.
    /// The range is 0 to 1 by default and says nothing; the flag is the real discriminator.
    /// </remarks>
    private static IEnumerable<FluidsList> Family(FluidsList[] all, bool solution) =>
        all.Where(fluid => Safe(() => fluid.CoolPropBackend() == "INCOMP" && !fluid.Pure() == solution))
            .Take(PerFamily);

    private void Add(List<Candidate> candidates, string family, FluidsList fluid, double? fraction)
    {
        var label = fraction is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"{fluid} at {value:P0}")
            : fluid.ToString();

        try
        {
            var instance = new Fluid(fluid, fraction is { } f ? Ratio.FromDecimalFractions(f) : null);

            candidates.Add(new Candidate(family, label, instance.WithState));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _unavailable.Add($"`{label}` ({family}) — could not be constructed: {Trim(exception.Message)}");
        }
    }

    private void AddMixture(
        List<Candidate> candidates, FluidsList[] all, string first, string second, double percent)
    {
        var label = string.Create(CultureInfo.InvariantCulture, $"{first} + {second} {percent:F0}/{100 - percent:F0}");
        var components = new[] { Named(all, first), Named(all, second) };

        if (components.Any(component => component is null))
        {
            _unavailable.Add($"`{label}` (HEOS mixture) — a component is unknown to this SharpProp version.");

            return;
        }

        try
        {
            var instance = new SharpProp.Mixture(
                components.Select(component => component!.Value),
                [Ratio.FromPercent(percent), Ratio.FromPercent(100 - percent)]);

            candidates.Add(new Candidate("HEOS mixture", label, instance.WithState));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _unavailable.Add($"`{label}` (HEOS mixture) — could not be constructed: {Trim(exception.Message)}");
        }
    }

    private static bool Safe(Func<bool> predicate)
    {
        try
        {
            return predicate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private void Probe(Candidate candidate)
    {
        // Every pair is fixed from values read off ONE reference state, so a pair that works must
        // reproduce it. Fixing (h, s) from numbers chosen by hand would land nowhere in particular, and
        // a refusal would then say nothing about the pair.
        IFluidState reference;

        if (_elapsed.Elapsed > RunBudget)
        {
            _unavailable.Add($"`{candidate.Label}` ({candidate.Family}) — not probed, run budget spent.");

            return;
        }

        Log($"- reference state for {candidate.Label} ({candidate.Family}) at 20 °C, 3 bar");

        IFluidState? fixedState = null;

        var outcome = Invoke(
            () =>
            {
                var started = Stopwatch.GetTimestamp();
                fixedState = candidate.Fix(
                    Input.Temperature(Temperature.FromDegreesCelsius(20)),
                    Input.Pressure(Pressure.FromBars(3)));
                Log($"  - fixed in {Microseconds(Stopwatch.GetTimestamp() - started) / 1000:F1} ms");
            },
            out var message);

        if (outcome != CallOutcome.Completed || fixedState is null)
        {
            Log($"  - {message ?? "produced nothing"}");
            _unavailable.Add(
                $"`{candidate.Label}` ({candidate.Family}) — no reference state at 20 °C, 3 bar: "
                + (message ?? "produced nothing"));

            return;
        }

        reference = fixedState;

        var values = new Dictionary<Property, double>
        {
            [Property.Temperature] = reference.Temperature.Kelvins,
            [Property.Pressure] = reference.Pressure.Pascals,
            [Property.Enthalpy] = reference.Enthalpy.JoulesPerKilogram,
            [Property.Entropy] = reference.Entropy.JoulesPerKilogramKelvin,
            [Property.Density] = reference.Density.KilogramsPerCubicMeter,
        };

        var properties = Enum.GetValues<Property>();

        for (var first = 0; first < properties.Length; first++)
        {
            for (var second = first + 1; second < properties.Length; second++)
            {
                var a = properties[first];
                var b = properties[second];

                Measure(
                    candidate.Family,
                    candidate.Label,
                    $"({Symbol(a)}, {Symbol(b)})",
                    () => candidate.Fix(InputFor(a, values[a]), InputFor(b, values[b])).Temperature.Kelvins,
                    reference.Temperature.Kelvins);
            }
        }
    }

    private void ProbeHumidAir()
    {
        // Humid air takes THREE inputs, not two: it is a two-component mixture at a stated pressure, so
        // no (T, p) fixes it. Pressure is held and the pairs are drawn from the rest, which is why it
        // cannot share the grid above.
        var air = new HumidAir();
        IHumidAirState reference;

        Log("- reference state for humid air at 101.325 kPa, 24 °C, 50 % RH");

        try
        {
            reference = air.WithState(
                InputHumidAir.Pressure(Pressure.FromPascals(101_325)),
                InputHumidAir.Temperature(Temperature.FromDegreesCelsius(24)),
                InputHumidAir.RelativeHumidity(RelativeHumidity.FromPercent(50)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _unavailable.Add($"`Humid air` — no reference state: {Trim(exception.Message)}");

            return;
        }

        var values = new Dictionary<AirProperty, double>
        {
            [AirProperty.Temperature] = reference.Temperature.Kelvins,
            [AirProperty.Humidity] = reference.Humidity.DecimalFractions,
            [AirProperty.RelativeHumidity] = reference.RelativeHumidity.Percent,
            [AirProperty.Enthalpy] = reference.Enthalpy.JoulesPerKilogram,
            [AirProperty.Entropy] = reference.Entropy.JoulesPerKilogramKelvin,
            [AirProperty.WetBulbTemperature] = reference.WetBulbTemperature.Kelvins,
            [AirProperty.DewTemperature] = reference.DewTemperature.Kelvins,
        };

        var pressure = InputHumidAir.Pressure(Pressure.FromPascals(101_325));
        var properties = Enum.GetValues<AirProperty>();

        for (var first = 0; first < properties.Length; first++)
        {
            for (var second = first + 1; second < properties.Length; second++)
            {
                var a = properties[first];
                var b = properties[second];

                Measure(
                    "Humid air",
                    "Humid air at 101.325 kPa",
                    $"(p, {Symbol(a)}, {Symbol(b)})",
                    () => air.WithState(pressure, InputFor(a, values[a]), InputFor(b, values[b]))
                        .Temperature.Kelvins,
                    reference.Temperature.Kelvins);
            }
        }
    }

    private void Measure(
        string family, string fluid, string pair, Func<double> fix, double referenceKelvins)
    {
        double error;
        double cold;

        if (_elapsed.Elapsed > RunBudget)

        {
            _cells.Add(new Cell(family, fluid, pair, 0, 0, [], double.NaN, "not measured — run budget spent"));

            return;
        }

        Log($"- {fluid} {pair}");

        var kelvins = 0.0;
        var measured = 0.0;

        var outcome = Invoke(
            () =>
            {
                var started = Stopwatch.GetTimestamp();
                kelvins = fix();
                measured = Microseconds(Stopwatch.GetTimestamp() - started);
            },
            out var message);

        if (outcome != CallOutcome.Completed)
        {
            Log($"  - {message}");
            _cells.Add(new Cell(family, fluid, pair, 0, 0, [], double.NaN, message));

            return;
        }

        cold = measured;
        error = Math.Abs(kelvins - referenceKelvins);

        Log($"  - first call {cold / 1000:F1} ms");

        // The cold call doubles as the estimate every budget below is derived from. Both the warm-up
        // and the batch are wall-clock budgets rather than call counts, because the families here differ
        // by four orders of magnitude: a fixed floor of ten warm-up calls is nothing for a 200 µs
        // pure-fluid flash and five seconds per cell for a mixture, which is how the first run of this
        // file passed a ten-minute timeout without finishing.
        var batch = Budget(SampleTargetMicroseconds, cold, 200);
        var warmup = Budget(WarmupTargetMicroseconds, cold, 400);
        var samples = cold > SlowCallMicroseconds ? 3 : Samples;

        if (cold > UnsamplableMicroseconds)
        {
            // One call took longer than the entire budget for a cell. Report what it cost and move on:
            // a median over three calls of something this slow says nothing the cold number does not.
            _cells.Add(new Cell(family, fluid, pair, 0, cold, [cold], error, null));

            return;
        }

        for (var i = 0; i < warmup; i++)
        {
            fix();
        }

        var perCall = new double[samples];

        for (var sample = 0; sample < samples; sample++)
        {
            var started = Stopwatch.GetTimestamp();

            for (var iteration = 0; iteration < batch; iteration++)
            {
                fix();
            }

            perCall[sample] = Microseconds(Stopwatch.GetTimestamp() - started) / batch;
        }

        _cells.Add(new Cell(family, fluid, pair, batch, cold, perCall, error, null));
        Log($"  - median {Median(perCall):F1} µs over {samples} samples of {batch}");
    }

    private static Input InputFor(Property property, double value) => property switch
    {
        Property.Temperature => Input.Temperature(Temperature.FromKelvins(value)),
        Property.Pressure => Input.Pressure(Pressure.FromPascals(value)),
        Property.Enthalpy => Input.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(value)),
        Property.Entropy => Input.Entropy(SpecificEntropy.FromJoulesPerKilogramKelvin(value)),
        Property.Density => Input.Density(Density.FromKilogramsPerCubicMeter(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static InputHumidAir InputFor(AirProperty property, double value) => property switch
    {
        AirProperty.Temperature => InputHumidAir.Temperature(Temperature.FromKelvins(value)),
        AirProperty.Humidity => InputHumidAir.Humidity(Ratio.FromDecimalFractions(value)),
        AirProperty.RelativeHumidity => InputHumidAir.RelativeHumidity(RelativeHumidity.FromPercent(value)),
        AirProperty.Enthalpy => InputHumidAir.Enthalpy(SpecificEnergy.FromJoulesPerKilogram(value)),
        AirProperty.Entropy => InputHumidAir.Entropy(SpecificEntropy.FromJoulesPerKilogramKelvin(value)),
        AirProperty.WetBulbTemperature => InputHumidAir.WetBulbTemperature(Temperature.FromKelvins(value)),
        AirProperty.DewTemperature => InputHumidAir.DewTemperature(Temperature.FromKelvins(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static string Symbol(Property property) => property switch
    {
        Property.Temperature => "T",
        Property.Pressure => "p",
        Property.Enthalpy => "h",
        Property.Entropy => "s",
        Property.Density => "d",
        _ => "?",
    };

    private static string Symbol(AirProperty property) => property switch
    {
        AirProperty.Temperature => "T",
        AirProperty.Humidity => "w",
        AirProperty.RelativeHumidity => "RH",
        AirProperty.Enthalpy => "h",
        AirProperty.Entropy => "s",
        AirProperty.WetBulbTemperature => "Twb",
        AirProperty.DewTemperature => "Tdp",
        _ => "?",
    };

    private static string Trim(string message)
    {
        var single = message.ReplaceLineEndings(" ").Replace('|', '/');

        return single.Length <= 130 ? single : single[..130] + "…";
    }

    /// <summary>Turns a time budget into a call count, given what one call costs.</summary>
    /// <param name="budgetMicroseconds">How long the whole run of calls should take.</param>
    /// <param name="callMicroseconds">What one call was measured to cost.</param>
    /// <param name="cap">The most calls to allow, however cheap they are.</param>
    /// <returns>At least one call, and never more than <paramref name="cap"/>.</returns>
    private static int Budget(double budgetMicroseconds, double callMicroseconds, int cap) =>
        (int)Math.Clamp(Math.Round(budgetMicroseconds / Math.Max(callMicroseconds, 1)), 1, cap);

    /// <summary>Gets the running log the probe appends to as it goes.</summary>
    /// <value>
    /// A second file beside the report. The report is written once, at the end, and so exists only if
    /// the run finished; this exists from the first call onward.
    /// </value>
    private static string LogPath => Path.Combine(RepositoryLayout.Diagnostics, "backend-pair-log.md");

    /// <summary>Appends one line to the running log and flushes it.</summary>
    /// <param name="line">What just happened, or what is about to be attempted.</param>
    /// <remarks>
    /// <para>
    /// Opened and closed per line on purpose. A buffered writer loses exactly the lines that matter
    /// when the process is killed, and the whole point of this file is to survive that: a cell logs
    /// what it is <em>about</em> to attempt before attempting it, so a run that never returns leaves a
    /// dangling line naming the call it is stuck in.
    /// </para>
    /// <para>
    /// It costs an open and a close per line against a property call of hundreds of microseconds, and
    /// only once per cell rather than per call, so it does not disturb what is being measured.
    /// </para>
    /// </remarks>
    private static void Log(string line) =>
        File.AppendAllText(LogPath, line + Environment.NewLine);

    /// <summary>How a guarded backend call ended.</summary>
    private enum CallOutcome
    {
        /// <summary>It returned.</summary>
        Completed,

        /// <summary>The backend refused it, and said why.</summary>
        Refused,

        /// <summary>It had not returned when the probe stopped waiting.</summary>
        TimedOut,
    }

    /// <summary>Runs a backend call, giving up on it after <see cref="CallTimeout"/>.</summary>
    /// <param name="call">The call, which is expected to record its own result by closure.</param>
    /// <param name="message">Why it did not complete, or <see langword="null"/> when it did.</param>
    /// <returns>How the call ended.</returns>
    /// <remarks>
    /// The thread is a background one so an abandoned call cannot keep the test host alive. Its start
    /// cost is not inside anything being timed — the caller starts its own stopwatch within the
    /// delegate.
    /// </remarks>
    private static CallOutcome Invoke(Action call, out string? message)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                call();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();

        if (!thread.Join(CallTimeout))
        {
            message = $"did not return within {CallTimeout.TotalSeconds:F0} s";

            return CallOutcome.TimedOut;
        }

        message = failure is null ? null : Trim(failure.Message);

        return failure is null ? CallOutcome.Completed : CallOutcome.Refused;
    }

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
        var mean = values.Average();

        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1));
    }

    private string Render()
    {
        var text = new StringBuilder();

        text.AppendLine("# Backend input-pair matrix")
            .AppendLine()
            .AppendLine("Generated by `BackendPairDiagnostics`. **Timings are bound to the machine and")
            .AppendLine("build below; the support and consistency columns are not.**")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: {RuntimeInformation.FrameworkDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- OS: {RuntimeInformation.OSDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Build: {Configuration}")
            .AppendLine(CultureInfo.InvariantCulture, $"- SharpProp: {Version}")
            .AppendLine("- Reference state: 20 °C and 3 bar absolute; humid air at 101.325 kPa, 24 °C, 50 % RH.")
            .AppendLine("  Every pair is fed values read off it.")
            .AppendLine(CultureInfo.InvariantCulture, $"- Sampling: {Samples} samples per cell, batch chosen per cell to take about {SampleTargetMicroseconds:F0} µs")
            .AppendLine(CultureInfo.InvariantCulture, $"- Fluids: a sub-selection — {PerFamily} per metadata-derived family, plus the named pure, pseudo-pure and mixture cases")
            .AppendLine(CultureInfo.InvariantCulture, $"- Run budget: {RunBudget.TotalSeconds:F0} s, of which this run used {_elapsed.Elapsed.TotalSeconds:F1} s")
            .AppendLine()
            .AppendLine("`ΔT` is how far the state a pair produced sits from the reference it was derived")
            .AppendLine("from. Anything but ~0 means the pair returned a state, and the wrong one.")
            .AppendLine()
            .AppendLine(Census());

        foreach (var family in _cells.GroupBy(static cell => cell.Family))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"## {family.Key}").AppendLine();

            foreach (var fluid in family.GroupBy(static cell => cell.Fluid))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"### {fluid.Key}")
                    .AppendLine()
                    .AppendLine("| Pair | Cold µs | Median µs | SD µs | Batch | ΔT K | Result |")
                    .AppendLine("|---|---:|---:|---:|---:|---:|---|");

                foreach (var cell in fluid)
                {
                    text.AppendLine(Row(cell));
                }

                text.AppendLine();
            }
        }

        if (_unavailable.Count > 0)
        {
            text.AppendLine("## Not probed").AppendLine();

            foreach (var line in _unavailable)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Renders one cell as a table row.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>A markdown table row.</returns>
    /// <remarks>
    /// Three shapes, not two. A cell whose single call cost more than the budget for a whole cell is
    /// never sampled, so it has one measurement and no deviation — rendering it like a sampled row
    /// printed a batch size of zero and a standard deviation of <c>NaN</c>, which reads as a bug rather
    /// than as the deliberate absence it is.
    /// </remarks>
    private static string Row(Cell cell)
    {
        var verdict = cell.TemperatureError < 1e-3 ? "ok" : "**wrong state**";

        if (cell.Failure is not null)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"| {cell.Pair} | — | — | — | — | — | refused: {cell.Failure} |");
        }

        if (cell.Batch == 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"| {cell.Pair} | {cell.Cold:F1} | — | — | — | {cell.TemperatureError:F4} "
                + $"| {verdict}, one call only — too slow to sample |");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"| {cell.Pair} | {cell.Cold:F1} | {Median(cell.PerCall):F2} "
            + $"| {StandardDeviation(cell.PerCall):F2} | {cell.Batch} | {cell.TemperatureError:F4} "
            + $"| {verdict} |");
    }

    private static string Census()
    {
        var text = new StringBuilder()
            .AppendLine("## What the backend offers")
            .AppendLine()
            .AppendLine("Every fluid this SharpProp version exposes, by its declared backend. A fluid")
            .AppendLine("the metadata does not call pure needs a concentration, and is a solution rather")
            .AppendLine("than a substance. The fraction range is *not* the discriminator: it is 0 to 1 by")
            .AppendLine("default on everything, pure water included.")
            .AppendLine()
            .AppendLine("| Backend | Fluids | Solutions |")
            .AppendLine("|---|---:|---:|");

        var groups = Enum.GetValues<FluidsList>()
            .Select(fluid => (Fluid: fluid, Backend: SafeString(() => fluid.CoolPropBackend())))
            .GroupBy(entry => entry.Backend, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count());

        foreach (var group in groups)
        {
            var solutions = group.Count(entry => Safe(() => !entry.Fluid.Pure()));

            text.AppendLine(CultureInfo.InvariantCulture,
                $"| `{group.Key}` | {group.Count()} | {solutions} |");
        }

        return text.ToString();
    }

    /// <summary>Gets the property package's own version string.</summary>
    /// <value>
    /// Its informational version, which is the package version. The assembly version is pinned at
    /// 1.0.0.0, and reporting that would date every report to a release that does not exist.
    /// </value>
    private static string Version =>
        typeof(Fluid).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
        ?? typeof(Fluid).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static string SafeString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return "unavailable";
        }
    }

    private static string Configuration =>
#if DEBUG
        "Debug — a release build is materially faster; do not quote these as production numbers";
#else
        "Release";
#endif
}
