using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// Binding step 0b and the curve half of step 5: <c>D-57</c>'s tables, <c>D-58</c>'s design point,
/// drivers as <c>let</c>s (<c>D-167</c>), timestamps (<c>D-60</c>) and <c>D-61</c>'s placed observers.
/// </summary>
public sealed class CurveBindingTests
{
    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(ScriptParse.Parse(new SourceText(text)));

    private static SemanticModel Model(string text)
    {
        var result = Bind(text);

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return result.Model;
    }

    private static string[] Codes(string text) =>
        [.. Bind(text).Diagnostics.Select(static d => d.Code)];

    private static CurveSymbol Curve(SemanticModel model, string name) =>
        Assert.Single(model.Curves, curve => curve.Name == name);

    /// <summary>The power of one heat exchanger, in watts, after the whole chain has run.</summary>
    private static double Power(SemanticModel model, string component) =>
        Assert.Single(model.Components, c => c.Name == component)
            .Parameters["power"].Value!.Value.SiValue;

    private const string HeatingCurve = """
        fluidscript 2

        let tout = -26 °C

        curve heating: tout
          -26  50
          -10  40
          20   0

        circuit "ahu":
          fluid = water
          number = 300
          role = ahu

          HX1  load  in.t = 50  out.t = 30  power = heating

        """;

    // ---- the table itself ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveIsSortedAndItsColumnsAreBare()
    {
        // Written out of order on purpose. A weather file is not obliged to arrive monotonic, and
        // sorting is cheaper than making the user do it.
        var model = Model("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  20 0\n  -26 50\n  -10 40\n");
        var heating = Curve(model, "heating");

        Assert.Equal([-26, -10, 20], heating.Points.Select(static point => point.X));
        Assert.Equal([50, 40, 0], heating.Points.Select(static point => point.Y));
        Assert.False(heating.IsExtrapolated);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(-26, 50)]
    [InlineData(-18, 45)]
    [InlineData(-10, 40)]
    [InlineData(5, 20)]
    [InlineData(20, 0)]
    public void ACurveInterpolatesLinearlyBetweenItsRows(double x, double expected)
    {
        // −18 sits halfway between −26 and −10, so it is halfway between 50 and 40. 5 is halfway
        // between −10 and 20, so it is halfway between 40 and 0.
        var curve = Curve(Model("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  -26 50\n  -10 40\n  20 0\n"), "heating");

        Assert.Equal(expected, curve.Evaluate(x), 9);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(-40, 50)]
    [InlineData(32, 0)]
    public void TheEndsHoldUnlessTheCurveSaysOtherwise(double x, double expected)
    {
        // Clamping is the default because it is the answer that cannot produce a nonsense number:
        // continuing a heating curve to −40 °C invents a duty from two points nobody validated there.
        var curve = Curve(Model("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  -26 50\n  -10 40\n  20 0\n"), "heating");

        Assert.Equal(expected, curve.Evaluate(x), 9);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(-40, 58.75)]
    [InlineData(32, -16)]
    public void ExtrapolatedEndsContinueTheSlopeOfTheOutermostPair(double x, double expected)
    {
        // Below: the first pair runs 50 → 40 over 16 degrees, so −40 is 14 degrees further at
        // 50 + 14 × 10/16 = 58.75. Above: the last pair runs 40 → 0 over 30 degrees, so 32 is 12
        // further at 0 − 12 × 40/30 = −16.
        var curve = Curve(
            Model("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout extrapolated\n  -26 50\n  -10 40\n  20 0\n"), "heating");

        Assert.True(curve.IsExtrapolated);
        Assert.Equal(expected, curve.Evaluate(x), 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoRowsAtOneXAreAStepAndTheLaterOneWins()
    {
        var result = Bind("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  -26 50\n  0 40\n  0 10\n  20 0\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1529");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);

        Assert.Equal(10, Curve(result.Model, "heating").Evaluate(0), 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveWithOneRowHasNothingToInterpolateBetween()
    {
        Assert.Contains("FS1530", Codes("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  -26 50\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARowWhoseColumnsDoNotReadIsReportedAndTheRestOfTheTableStands()
    {
        // Three columns, not two. The split takes the last whitespace run, so x reads as "12 34",
        // which is not a number.
        var result = Bind("fluidscript 2\n\nlet tout = -26 C\n\ncurve heating: tout\n  -26 50\n  12 34 56\n  20 0\n");

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1117");
        Assert.Equal(2, Curve(result.Model, "heating").Points.Length);
    }

    // ---- the driver ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADriverThatNamesNothingAnywhereIsReported()
    {
        // A driver is a `let` (`D-167`), so a curve driven by a name that is none has no source for
        // its number, and `FS1811` says what to write.
        var diagnostic = Assert.Single(
            Bind("fluidscript 2\n\ncurve heating: nothingKnown\n  -26 50\n  20 0\n").Diagnostics,
            static d => d.Code == "FS1811");

        Assert.Contains("nothingKnown", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnyLetIsADriverOnceItHasAValue()
    {
        // The other half of the same rule. A plant is full of drivers nobody registered; what makes
        // one usable is that something supplies its number, and that is a `let` (`D-167`).
        var model = Model(
            "fluidscript 2\n\nlet flueTemp = 180\n\ncurve recovery: flueTemp\n  100 5\n  200 20\n\n"
            + "circuit \"hr\":\n  number = 100\n\n  HX1  load  in.t = 50  out.t = 30  power = recovery\n");

        Assert.Equal(CurveDriverKind.Let, Curve(model, "recovery").DriverKind);
        Assert.Equal(17_000, Power(model, "HX1"), 6);
    }

    // ---- the design point ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignValueIsWhereAStaticCircuitReadsItsCurve()
    {
        // −26 is the first row, so the power is 50 kW exactly, in SI watts.
        Assert.Equal(50_000, Power(Model(HeatingCurve), "HX1"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveReferenceMayCarryTheUnitItsNumbersAreIn()
    {
        // `L-35`, the half of `D-57` that was deferred: the table's 50 is bare, `power=heating` reads it
        // through the parameter's canonical unit as 50 kW, and `power=heating W` says the table is in
        // watts, so the same row is 50 W. A slashed spelling is one unit too.
        var watts = Model(HeatingCurve.Edited("power = heating", "power = heating W"));
        Assert.Equal(50, Power(watts, "HX1"), 6);

        var kilowatts = Model(HeatingCurve.Edited("power = heating", "power = heating kW"));
        Assert.Equal(50_000, Power(kilowatts, "HX1"), 6);

        var flow = Model(
            "fluidscript 2\n\nlet tout = -26 C\n\ncurve demand: tout\n  -26 0.5\n  20 0.1\n\n"
            + "circuit \"ahu\":\n  fluid = water\n\n  HX1  load  in.t = 50  out.t = 30  flow = demand kg/s\n");
        Assert.Equal(0.5, Assert.Single(flow.Components, c => c.Name == "HX1").Parameters["flow"].Value!.Value.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AUnitThatContradictsTheParameterOrTheValueIsFS1304()
    {
        // `power=heating kPa` reads the table in kilopascals and hands a pressure to a power: the
        // ordinary mismatch. A unit after a value that already has a dimension may only agree with it.
        Assert.Contains("FS1304", Codes(HeatingCurve.Edited("power = heating", "power = heating kPa")));

        const string Referenced = "fluidscript 2\n\ncircuit \"demo\":\n  fluid = water\n\n  HE1  heat_exchanger  dp = 20\n  PU1  pump  dp = HE1.dp kW\n";
        Assert.Contains("FS1304", Codes(Referenced));
        Assert.DoesNotContain("FS1304", Codes(Referenced.Edited(" kW", " kPa")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStaticCircuitReadingACurveOfTimeIsAnError()
    {
        // An error rather than a default: guessing zero, or the table's first row, would put a number
        // in front of an engineer that nothing chose. A curve of time has a value only inside a run.
        var diagnostic = Assert.Single(
            Bind("""
                fluidscript 2

                curve heating: time
                  0     50
                  3600  0

                circuit "ahu":
                  fluid = water

                  HX1  load  in.t = 50  out.t = 30  power = heating
                """).Diagnostics,
            static d => d.Code == "FS1528");

        Assert.Contains("'heating'", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>A driver with a design value, handed a curve of time by a run (`D-169`).</summary>
    private const string Weather = """
        fluidscript 2

        let outdoor = -26 C

        curve weather: time
          0     -1
          3600  -3

        curve heating: outdoor
          -26  50
           20   0

        circuit "ahu":
          fluid = water

          HX1  load  in.t = 50  out.t = 30  power = heating

        run "Hour":
          duration = 1 h
          outdoor  = weather
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void ARunReadingACurveDefersItInsteadOfFailing()
    {
        // `D-58`: in a run a curve is a live function of time. Nothing is wrong here, and the reference
        // is recorded for the transient stage to read again at each step.
        var result = Bind(Weather);
        var run = RunProjection.Project(result.Model, Assert.Single(result.Model.Runs));

        Assert.DoesNotContain("FS1528", result.Diagnostics.Select(static d => d.Code));
        Assert.Contains(
            run.Deferred,
            deferred => deferred.Target is ValueId.ComponentParameter { Component: "HX1", Parameter: "power" });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignPointSizesEvenWhenARunPlaysInTime()
    {
        // The half of `D-58` that is easy to lose: the driver's own value is the sizing point in *every*
        // mode, so the model a run plays still carries the design-point value as the number it was sized
        // for.
        var result = Bind(Weather);
        var run = RunProjection.Project(result.Model, Assert.Single(result.Model.Runs));

        Assert.Equal(50_000, Power(result.Model, "HX1"), 6);
        Assert.Equal(50_000, Power(run, "HX1"), 6);
        Assert.Contains(run.Deferred, deferred => deferred.CurrentEstimate is not null);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OneCurveDrivesAPowerAndAPositionWithoutBeingTold()
    {
        // The whole point of a bare table (`D-57`): `D-14`'s rule reinterprets the same 0.5 as
        // kilowatts on one parameter and as a fraction on the other.
        var model = Model("""
            fluidscript 2

            let tout = 0 °C

            curve shared: tout
              -10  0.5
              10  0.5

            circuit "ahu":
              fluid = water
              number = 300
              role = ahu

              HX1  load  in.t = 50  out.t = 30  power = shared
              TV1  valve  position = shared
            """);

        Assert.Equal(500, Power(model, "HX1"), 6);
        Assert.Equal(
            0.5,
            Assert.Single(model.Components, c => c.Name == "TV1").Parameters["position"].Value!.Value.SiValue,
            9);
    }

    // ---- timestamps ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ATimeDrivenCurveReadsUnixSecondsAndIso8601WithoutBeingTold()
    {
        var model = Model("fluidscript 2\n\ncurve outdoor: time\n  0 -1\n  2026-01-01T01:00:00 -3\n");

        // 2026-01-01T01:00:00 is 1 767 229 200 seconds after the epoch.
        Assert.Equal([0, 1_767_229_200], Curve(model, "outdoor").Points.Select(static p => p.X));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UnreadableRowsAreMarkedFiveAtATimeAndTheRestCountedOnTheHeader()
    {
        // L-40's cap: a year of hourly data with one wrong column layout is one mistake, not 8 760.
        var rows = string.Concat(Enumerable.Range(0, 12).Select(static i => $"  2026-01-{i + 1:00} x y\n"));
        var result = Bind("fluidscript 2\n\ncurve outdoor: time\n  2026-01-20 -1\n  2026-01-21 -3\n" + rows);

        Assert.Equal(5, result.Diagnostics.Count(static d => d.Code == "FS1117"));
        var rest = Assert.Single(result.Diagnostics, static d => d.Code == "FS1535");
        Assert.Contains("7 more rows", rest.Message, StringComparison.Ordinal);
        Assert.Equal(2, Curve(result.Model, "outdoor").Points.Length);
    }

    // ---- observers, and the short control form ------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ASensorIsPlacedOnANodeAndStaysOutOfTheHydraulicGraph()
    {
        var model = Model("""
            fluidscript 2

            circuit "ahu":
              number = 300
              role = ahu

              HX1  load  in.t = 50  out.t = 30  power = 24
              TE1  t_sensor at N2

              N1 - HX1 - N2
            """);

        var sensor = Assert.Single(model.Components, c => c.Name == "TE1");

        Assert.Equal("N2", sensor.AttachedTo);
        Assert.Empty(sensor.Ports);
        Assert.DoesNotContain(
            model.Connections,
            connection => connection.From.Component == "TE1" || connection.To.Component == "TE1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnplacedInstrumentIsWarnedAboutRatherThanCalledUnconnected()
    {
        // An observer is exempt from FS1507 because it is never connected to anything; without its
        // own code it would bind in silence, and with FS1507 the advice would be wrong.
        var codes = Codes("fluidscript 2\n\ncircuit \"ahu\":\n  number = 300\n  role = ahu\n\n  TE1  t_sensor\n");

        Assert.Contains("FS1533", codes);
        Assert.DoesNotContain("FS1507", codes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyAnInstrumentIsPlacedWithAt()
    {
        Assert.Contains("FS1532", Codes("fluidscript 2\n\ncircuit \"ahu\":\n  number = 300\n  role = ahu\n\n  PU1  pump at N2\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnAtClauseNamingNoNodeIsReported()
    {
        Assert.Contains(
            "FS1404",
            Codes("fluidscript 2\n\ncircuit \"ahu\":\n  number = 300\n  role = ahu\n\n  TE1  t_sensor at N9\n\n  N1 - N2\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheShortControlFormResolvesThroughTheRegistrysSingleActuatorAndSensor()
    {
        // `D-61` amends `D-43`: of a valve's `position`, `kv` and `authority`, only `position` moves
        // during a solve, so the bare form is unambiguous by construction.
        var model = Model("""
            fluidscript 2

            circuit "ahu":
              number = 300
              role = ahu

              HX1  load  in.t = 50  out.t = 30  power = 24
              TV1  valve
              PID1 controller:
                kp = 3
                moves = TV1
                reads = TE1
                setpoint = 21
              TE1  t_sensor at N2

              N1 - HX1 - TV1 - N2
            """);

        var binding = Assert.Single(model.ControlBindings);

        Assert.Equal(new PropertyReference("TV1", "position"), binding.Actuator);
        Assert.Equal(new PropertyReference("TE1", "t"), binding.Measurement);
        Assert.Equal("PID1", binding.Controller.Name);
        Assert.Equal(Quantity.FromBareNumber(21, Dimension.Temperature), binding.Setpoint);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheQualifiedFormStaysLegalInTheShortShape()
    {
        var model = Model("""
            fluidscript 2

            circuit "ahu":
              number = 300
              role = ahu

              TV1  valve
              PID1 controller:
                kp = 3
                moves = TV1.position
                reads = TE1.t
                setpoint = 21
              TE1  t_sensor at N2

              N1 - TV1 - N2
            """);

        Assert.Equal(new PropertyReference("TV1", "position"), Assert.Single(model.ControlBindings).Actuator);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AKindWithNoSingleActuatorMustBeWrittenOut()
    {
        // Where the registry names none, the bare form is refused and the message carries an example
        // of the form that works.
        var diagnostic = Assert.Single(
            Bind("""
                fluidscript 2

                circuit "ahu":
                  number = 300
                  role = ahu

                  HX1  load  in.t = 50  out.t = 30  power = 24
                  PID1 controller:
                    kp = 3
                    moves = HX1
                    reads = TE1
                    setpoint = 21
                  TE1  t_sensor at N2

                  N1 - HX1 - N2
                """).Diagnostics,
            static d => d.Code == "FS1531");

        Assert.Contains("HX1.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AShortControlLineWithNoSetpointSaysSo()
    {
        Assert.Contains(
            "FS1521",
            Codes("""
                fluidscript 2

                circuit "ahu":
                  number = 300
                  role = ahu

                  TV1  valve
                  PID1 controller:
                    kp = 3
                    moves = TV1
                    reads = TE1
                  TE1  t_sensor at N2

                  N1 - TV1 - N2
                """));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointMayItselfBeACurve()
    {
        // The feature this was all asked for: a compensated setpoint. The curve yields a bare 45,
        // which `D-14` reinterprets in the measured property's dimension.
        var model = Model("""
            fluidscript 2

            let tout = -26 °C

            curve supplyTemp: tout
              -26  45
              20  20

            circuit "ahu":
              fluid = water
              number = 300
              role = ahu

              TV1  valve
              PID1 controller:
                kp = 3
                moves = TV1
                reads = TE1
                setpoint = supplyTemp
              TE1  t_sensor at N2

              N1 - TV1 - N2
            """);

        Assert.Equal(
            Quantity.FromBareNumber(45, Dimension.Temperature),
            Assert.Single(model.ControlBindings).Setpoint);
    }
}
