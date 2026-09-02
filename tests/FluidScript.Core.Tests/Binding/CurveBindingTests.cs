using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// Binding step 0b and the curve half of step 5: <c>D-57</c>'s tables, <c>D-58</c>'s design point,
/// <c>D-59</c>'s driver registry, <c>D-60</c>'s timestamps and <c>D-61</c>'s placed observers.
/// </summary>
public sealed class CurveBindingTests
{
    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(text)));

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
        fluidscript 1
        design tout=-26

        curve heating tout
        -26  50
        -10  40
         20   0

        circuit ahu 300
        fluid static water
        HX1 heat_exchanger in=50 out=30 power=heating

        """;

    // ---- the table itself ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveIsSortedAndItsColumnsAreBare()
    {
        // Written out of order on purpose. A weather file is not obliged to arrive monotonic, and
        // sorting is cheaper than making the user do it.
        var model = Model("fluidscript 1\ncurve heating tout\n20 0\n-26 50\n-10 40\n");
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
        var curve = Curve(Model("fluidscript 1\ncurve heating tout\n-26 50\n-10 40\n20 0\n"), "heating");

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
        var curve = Curve(Model("fluidscript 1\ncurve heating tout\n-26 50\n-10 40\n20 0\n"), "heating");

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
            Model("fluidscript 1\ncurve heating tout extrapolated\n-26 50\n-10 40\n20 0\n"), "heating");

        Assert.True(curve.IsExtrapolated);
        Assert.Equal(expected, curve.Evaluate(x), 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoRowsAtOneXAreAStepAndTheLaterOneWins()
    {
        var result = Bind("fluidscript 1\ncurve heating tout\n-26 50\n0 40\n0 10\n20 0\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1529");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);

        Assert.Equal(10, Curve(result.Model, "heating").Evaluate(0), 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveWithOneRowHasNothingToInterpolateBetween()
    {
        Assert.Contains("FS1530", Codes("fluidscript 1\ncurve heating tout\n-26 50\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARowWhoseColumnsDoNotReadIsReportedAndTheRestOfTheTableStands()
    {
        // Three columns, not two. The split takes the last whitespace run, so x reads as "12 34",
        // which is not a number.
        var result = Bind("fluidscript 1\ncurve heating tout\n-26 50\n12 34 56\n20 0\n");

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1117");
        Assert.Equal(2, Curve(result.Model, "heating").Points.Length);
    }

    // ---- the driver ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADriverResolvesThroughTheRoleRegistryLikeACircuitName()
    {
        // `D-59`: `tout`, `t_out`, `outdoor` and `outdoorTemperature` are one driver, and the registry
        // is what buys that without touching the grammar.
        foreach (var spelling in new[] { "tout", "t_out", "outdoor", "outdoorTemperature", "oat" })
        {
            var model = Model($"fluidscript 1\ncurve heating {spelling}\n-26 50\n20 0\n");

            Assert.Equal(CurveDriverKind.Role, Curve(model, "heating").DriverKind);
            Assert.Equal("tout", Curve(model, "heating").DriverRole!.CanonicalName);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADriverThatNamesNothingAnywhereIsReported()
    {
        // `D-59` says an unregistered name is not an error and `FS1527` says a driver naming nothing
        // is. Both hold: a driver has to supply a number, and this one has no source for it.
        var diagnostic = Assert.Single(
            Bind("fluidscript 1\ncurve heating nothingKnown\n-26 50\n20 0\n").Diagnostics,
            static d => d.Code == "FS1527");

        Assert.Contains("nothingKnown", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnregisteredDriverWithADesignValueBehindItWorks()
    {
        // The other half of the same rule. A plant is full of drivers nobody registered; what makes
        // one usable is that something supplies its number.
        var model = Model(
            "fluidscript 1\ndesign flueTemp=180\ncurve recovery flueTemp\n100 5\n200 20\n"
            + "circuit hr 100\nHX1 heat_exchanger in=50 out=30 power=recovery\n");

        Assert.Equal(CurveDriverKind.DesignOnly, Curve(model, "recovery").DriverKind);
        Assert.Equal(17_000, Power(model, "HX1"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveMayDriveAnotherAndTheChainIsNumericThroughout()
    {
        // `outdoor` maps 3600 seconds to −3; `heating` reads −3 against (−26, 50) and (20, 0), which
        // is exactly halfway, for 25; the assignment makes that 25 kW.
        var model = Model("""
            fluidscript 1
            curve outdoor time
            0     -1
            3600  -3

            curve heating outdoor
            -26  50
             20   0

            circuit ahu 300
            fluid static water
            HX1 heat_exchanger in=50 out=30 power=heating

            design time=3600
            """);

        Assert.Equal(CurveDriverKind.Curve, Curve(model, "heating").DriverKind);
        Assert.Equal(25_000, Power(model, "HX1"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoCurvesThatDriveEachOtherAreTheSameCycleALetWouldBe()
    {
        Assert.Contains(
            "FS1402",
            Codes("fluidscript 1\ncurve a b\n0 1\n1 2\n\ncurve b a\n0 1\n1 2\n"));
    }

    // ---- the design point ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignValueIsWhereAStaticCircuitReadsItsCurve()
    {
        // −26 is the first row, so the power is 50 kW exactly, in SI watts.
        Assert.Equal(50_000, Power(Model(HeatingCurve), "HX1"), 6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("design tout=-26")]
    [InlineData("design tout=-26 C")]
    [InlineData("design outdoor=-26")]
    public void TheRoleIsWhatMakesADesignValueCheckableAndSpellingIndependent(string design)
    {
        // `D-59`: the role's dimension is the only place a curve meets one. A bare −26 and −26 °C are
        // the same point on a table whose own x column says −26, and `outdoor` is the same driver.
        var model = Model(
            $"fluidscript 1\n{design}\ncurve heating tout\n-26 50\n20 0\n"
            + "circuit ahu 300\nfluid static water\nHX1 heat_exchanger in=50 out=30 power=heating\n");

        Assert.Equal(50_000, Power(model, "HX1"), 6);
        Assert.Equal(-26, model.Project.Design["tout"].Number);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignValueInTheWrongDimensionIsCaught()
    {
        Assert.Contains(
            "FS1304",
            Codes("fluidscript 1\ndesign tout=3 bar\ncurve heating tout\n-26 50\n20 0\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignValueShortCircuitsTheCurveOfTheSameDriver()
    {
        // `D-58`'s worked example, and the reason a file carrying a year of weather data still solves
        // statically: with `design tout=-26` the chain time → outdoor → heating is not walked, and
        // `heating` is read at −26 directly for 50 kW rather than at outdoor's own −1.
        var model = Model("""
            fluidscript 1
            design tout=-26

            curve outdoor time
            0     -1
            3600  -3

            curve heating outdoor
            -26  50
             20   0

            circuit ahu 300
            fluid static water
            HX1 heat_exchanger in=50 out=30 power=heating
            """);

        Assert.Equal(50_000, Power(model, "HX1"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStaticCircuitReadingACurveWithNoDesignValueIsAnError()
    {
        // An error rather than a default: guessing zero, or the table's first row, would put a number
        // in front of an engineer that nothing chose.
        var diagnostic = Assert.Single(
            Bind("""
                fluidscript 1
                curve outdoor time
                0     -1
                3600  -3

                curve heating outdoor
                -26  50
                 20   0

                circuit ahu 300
                fluid static water
                HX1 heat_exchanger in=50 out=30 power=heating
                """).Diagnostics,
            static d => d.Code == "FS1528");

        // The curve and driver named are the ones written on the line, so the `design` line it
        // suggests is one the user can write as it stands.
        Assert.Contains("'heating'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("outdoor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADynamicCircuitReadingACurveDefersItInsteadOfFailing()
    {
        // `D-58`: in a dynamic solve a curve is a live function of time. Nothing is wrong here, and
        // the reference is recorded for the transient stage to read again at each step.
        var result = Bind("""
            fluidscript 1
            curve outdoor time
            0     -1
            3600  -3

            curve heating outdoor
            -26  50
             20   0

            circuit ahu 300
            fluid dynamic water
            HX1 heat_exchanger in=50 out=30 power=heating
            """);

        Assert.DoesNotContain("FS1528", result.Diagnostics.Select(static d => d.Code));
        Assert.Contains(
            result.Model.Deferred,
            deferred => deferred.Target is ValueId.ComponentParameter { Component: "HX1", Parameter: "power" });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignPointSizesEvenWhenTheCircuitSolvesInTime()
    {
        // The half of `D-58` that is easy to lose: `design` is the sizing point in *every* mode, so a
        // dynamic circuit still carries the design-point value as the number it was sized for.
        var result = Bind(
            "fluidscript 1\ndesign tout=-26\ncurve heating tout\n-26 50\n20 0\n"
            + "circuit ahu 300\nfluid dynamic water\nHX1 heat_exchanger in=50 out=30 power=heating\n");

        Assert.Equal(50_000, Power(result.Model, "HX1"), 6);
        Assert.Contains(result.Model.Deferred, deferred => deferred.CurrentEstimate is not null);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OneCurveDrivesAPowerAndAPositionWithoutBeingTold()
    {
        // The whole point of a bare table (`D-57`): `D-14`'s rule reinterprets the same 0.5 as
        // kilowatts on one parameter and as a fraction on the other.
        var model = Model("""
            fluidscript 1
            design tout=0

            curve shared tout
            -10  0.5
             10  0.5

            circuit ahu 300
            fluid static water
            HX1 heat_exchanger in=50 out=30 power=shared
            TV1 valve position=shared
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
        var model = Model("fluidscript 1\ncurve outdoor time\n0 -1\n2026-01-01T01:00:00 -3\n");

        // 2026-01-01T01:00:00 is 1 767 229 200 seconds after the epoch.
        Assert.Equal([0, 1_767_229_200], Curve(model, "outdoor").Points.Select(static p => p.X));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatedFormatIsDotNetsAndItsCaseMatters()
    {
        // `D-60`: `MM` is the month and `mm` the minute. The proposal wrote `dd/mm/yyyy hh:mm:ss`,
        // which taken literally is day / minute / year on a 12-hour clock.
        var model = Model(
            "fluidscript 1\ncurve outdoor time format=\"dd/MM/yyyy HH:mm:ss\"\n"
            + "01/01/2026 00:00:00 -1\n01/01/2026 01:00:00 -3\n");

        Assert.Equal("dd/MM/yyyy HH:mm:ss", Curve(model, "outdoor").TimeFormat);
        Assert.Equal(3600, Curve(model, "outdoor").Points[1].X - Curve(model, "outdoor").Points[0].X);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATimestampTheStatedFormatDoesNotFitIsReportedPerRow()
    {
        var result = Bind(
            "fluidscript 1\ncurve outdoor time format=\"yyyy-MM-dd\"\n"
            + "2026-01-01 -1\n01/02/2026 -3\n2026-01-03 -2\n");

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1117");
        Assert.Equal(2, Curve(result.Model, "outdoor").Points.Length);
    }

    // ---- observers, and the short control form ------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ASensorIsPlacedOnANodeAndStaysOutOfTheHydraulicGraph()
    {
        var model = Model("""
            fluidscript 1
            circuit ahu 300
            HX1 heat_exchanger in=50 out=30 power=24
            TE1 t_sensor at N2

            connections
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
        var codes = Codes("fluidscript 1\ncircuit ahu 300\nTE1 t_sensor\n");

        Assert.Contains("FS1533", codes);
        Assert.DoesNotContain("FS1507", codes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnlyAnInstrumentIsPlacedWithAt()
    {
        Assert.Contains("FS1532", Codes("fluidscript 1\ncircuit ahu 300\nPU1 pump at N2\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnAtClauseNamingNoNodeIsReported()
    {
        Assert.Contains(
            "FS1404",
            Codes("fluidscript 1\ncircuit ahu 300\nTE1 t_sensor at N9\n\nconnections\nN1 - N2\n"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheShortControlFormResolvesThroughTheRegistrysSingleActuatorAndSensor()
    {
        // `D-61` amends `D-43`: of a valve's `position`, `kv` and `authority`, only `position` moves
        // during a solve, so the bare form is unambiguous by construction.
        var model = Model("""
            fluidscript 1
            circuit ahu 300
            HX1  heat_exchanger in=50 out=30 power=24
            TV1  valve
            PID1 pid kp=3
            TE1  t_sensor at N2

            connections
            N1 - HX1 - TV1 - N2

            control TV1 with TE1 by PID1 setpoint=21
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
            fluidscript 1
            circuit ahu 300
            TV1  valve
            PID1 pid kp=3
            TE1  t_sensor at N2

            connections
            N1 - TV1 - N2

            control TV1.position with TE1.t by PID1 setpoint=21
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
                fluidscript 1
                circuit ahu 300
                HX1  heat_exchanger in=50 out=30 power=24
                PID1 pid kp=3
                TE1  t_sensor at N2

                connections
                N1 - HX1 - N2

                control HX1 with TE1 by PID1 setpoint=21
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
                fluidscript 1
                circuit ahu 300
                TV1  valve
                PID1 pid kp=3
                TE1  t_sensor at N2

                connections
                N1 - TV1 - N2

                control TV1 with TE1 by PID1
                """));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointMayItselfBeACurve()
    {
        // The feature this was all asked for: a compensated setpoint. The curve yields a bare 45,
        // which `D-14` reinterprets in the measured property's dimension.
        var model = Model("""
            fluidscript 1
            design tout=-26

            curve supplyTemp tout
            -26  45
             20  20

            circuit ahu 300
            fluid static water
            TV1  valve
            PID1 pid kp=3
            TE1  t_sensor at N2

            connections
            N1 - TV1 - N2

            control TV1 with TE1 by PID1 setpoint=supplyTemp
            """);

        Assert.Equal(
            Quantity.FromBareNumber(45, Dimension.Temperature),
            Assert.Single(model.ControlBindings).Setpoint);
    }
}
