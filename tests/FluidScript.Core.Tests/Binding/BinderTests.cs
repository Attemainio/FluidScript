using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// Binding steps 0 through 5 from <c>plan/10-language/15-semantic-model.md</c>, and the expression
/// rules from <c>14</c>. Several of these are M1 exit criteria from <c>05</c>, named where they are.
/// </summary>
public sealed class BinderTests
{
    private static BindResult Bind(string text, string documentName = "script") =>
        new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText(text)), documentName);

    private static SemanticModel Model(string text)
    {
        var result = Bind(text);
        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return result.Model;
    }

    private static Diagnostic OnlyDiagnostic(string text, string code)
    {
        var result = Bind(text);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();

        Assert.True(
            matching.Length == 1,
            $"Expected exactly one {code}; got "
            + (result.Diagnostics.IsEmpty
                ? "none at all"
                : string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))));

        return matching[0];
    }

    // ---- step 0: circuits ------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AScriptWithNoCircuitHeaderStillBindsOne()
    {
        // Consumers never special-case an empty collection, which is why the implicit circuit exists
        // rather than the model carrying none.
        var result = Bind("fluidscript 1\nHE1 heat_exchanger power=30\n", "cooling.fluid");

        var circuit = Assert.Single(result.Model.Circuits);
        Assert.Equal("cooling.fluid", circuit.Name);
        Assert.Equal(100, circuit.Number);
        Assert.False(circuit.NumberIsExplicit);
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1508");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ThreeHeadersBindThreeCircuitsNumberedInDeclarationOrder()
    {
        // M1's exit criterion for D-33, and the reason NumberIsExplicit exists: the printer must not
        // write these numbers back into a file that never had them.
        var model = Model("""
            fluidscript 1
            circuit primary
            circuit secondary
            circuit tertiary
            """);

        Assert.Equal([100, 200, 300], model.Circuits.Select(static circuit => circuit.Number));
        Assert.All(model.Circuits, static circuit => Assert.False(circuit.NumberIsExplicit));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatedNumberIsKeptAndNeverReused()
    {
        var model = Model("""
            fluidscript 1
            circuit ahu 200
            circuit radiators
            """);

        Assert.Equal(200, model.Circuits[0].Number);
        Assert.True(model.Circuits[0].NumberIsExplicit);

        // 100, not 300: the lowest unused multiple, so stating 200 first does not push everything up.
        Assert.Equal(100, model.Circuits[1].Number);
        Assert.False(model.Circuits[1].NumberIsExplicit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1524_TwoCircuitsClaimingOneNumber() =>
        OnlyDiagnostic("fluidscript 1\ncircuit a 100\ncircuit b 100\n", "FS1524");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1525_TwoCircuitsSharingAName() =>
        OnlyDiagnostic("fluidscript 1\ncircuit ahu 100\ncircuit ahu 200\n", "FS1525");

    [Fact]
    [Trait("Category", "Unit")]
    public void TheProjectSetsTheDefaultModeAndACircuitOverridesIt()
    {
        // D-37's precedence: the circuit's own setting wins, and the disagreement is visible rather
        // than resolved quietly.
        var result = Bind("""
            fluidscript 1
            project dynamic plant_01
            circuit storage
            fluid static water
            """);

        Assert.Equal("plant_01", result.Model.Project.Name);
        Assert.Equal(FluidMode.Dynamic, result.Model.Project.DefaultMode);
        Assert.Equal(FluidMode.Static, result.Model.Circuits[0].Mode);
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1517");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitWithNoModeAnywhereIsStatic()
    {
        var model = Model("fluidscript 1\ncircuit demo\nfluid water\n");

        Assert.Equal(FluidMode.Static, model.Circuits[0].Mode);
        Assert.Equal("water", model.Circuits[0].Substance);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SpacingBindsIntoStyleAndNotIntoProject()
    {
        // D-37: one value, one path. A second home on ProjectSettings would create the one that gets
        // serialized and the one that does not.
        var model = Model("fluidscript 1\nspacing 20\n");

        Assert.Equal(20, model.Style.Spacing);
    }

    [Theory]
    [InlineData("ahu", "ahu", ThermalStageRole.Consumer)]
    [InlineData("AirHandlingUnit", "ahu", ThermalStageRole.Consumer)]
    [InlineData("radiators", "radiator", ThermalStageRole.Consumer)]
    [InlineData("ground_loop", "ground_loop", ThermalStageRole.Source)]
    [InlineData("buffer", "storage", ThermalStageRole.Storage)]
    [Trait("Category", "Unit")]
    public void ACircuitNameResolvesToARole(string name, string canonical, ThermalStageRole stage)
    {
        var model = Model($"fluidscript 1\ncircuit {name}\n");

        Assert.Equal(canonical, model.Circuits[0].Role.CanonicalName);
        Assert.Equal(stage, model.Circuits[0].Role.Stage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1519_ACircuitNameThatIsNoRoleIsNeutralAndNotAnError()
    {
        // A plant is full of circuits whose function has no registry entry. Refusing to bind one would
        // make the language useless for the plant it describes.
        var diagnostic = OnlyDiagnostic("fluidscript 1\ncircuit loop_7b\n", "FS1519");

        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(ThermalStageRole.Neutral, Model("fluidscript 1\ncircuit loop_7b\n").Circuits[0].Role.Stage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1107_AScheduleInACircuitWithNoTimeToRunIn()
    {
        // 12's acceptance criterion, raised here because the parser cannot see a circuit's mode: it is
        // the circuit's own directive resolved against the project's.
        var diagnostic = OnlyDiagnostic(
            """
            fluidscript 1
            circuit demo
            fluid static water
            schedule
            at 60 s HE1.power = 45
            """,
            "FS1107");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AScheduleUnderADynamicFluidIsFine()
    {
        var result = Bind(
            """
            fluidscript 1
            circuit demo
            fluid dynamic water
            schedule
            at 60 s HE1.power = 45
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1107");
    }

    // ---- steps 1-3: declarations, kinds, parameters -----------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADeclarationWithNoParametersBindsWithNone()
    {
        // M1's headline criterion, and D-02's whole point: absence is representable and distinct from
        // a default. `3WV` also has to survive as an identifier despite the leading digit.
        var model = Model("fluidscript 1\n3WV three_way_valve\n");

        // One declaration, and the two I3 boundary nodes its non-optional ports now terminate: `b` is
        // optional, so a three-way valve alone in a file produces exactly two.
        var component = Assert.Single(model.Components, symbol => symbol.Origin is Origin.Declared);
        Assert.Equal("3WV", component.Name);
        Assert.Equal("three_way_valve", component.Kind!.Keyword);
        Assert.Empty(component.Parameters);
        Assert.Equal(["3WV__ab", "3WV__a"], model.Components
            .Where(symbol => symbol.Origin is Origin.Inferred)
            .Select(symbol => symbol.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnresolvedKindStillProducesAComponent()
    {
        // P4: the script continues. A stage that dropped the component would take every connection
        // naming it down as well, and a user mid-word would watch their circuit disappear.
        var result = Bind("fluidscript 1\nX1 wombat\n");

        var component = Assert.Single(result.Model.Components);
        Assert.Null(component.Kind);
        Assert.Equal("wombat", component.WrittenKind);
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1502");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1512_AKindOneKeystrokeFromExactlyOneOther()
    {
        // `pmp` scores 0.75 against `pump` and nothing else comes near it, so the kind resolves. The
        // info is the whole of what the user gets told, which is why it names both spellings.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nPU1 pmp\n", "FS1512");

        Assert.Contains("'pmp'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'pump'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1513_AKindTheSameDistanceFromTwo()
    {
        // `pide` is one edit from `pipe` and one from `pid`, so both score 0.75 and neither clears the
        // other by `AmbiguityMargin`. Taking the higher would be a coin flip, and worse, the winner
        // would depend on the alias list of a kind the script never mentioned.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nX1 pide\n", "FS1513");

        Assert.Contains("'pide'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'pipe'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'controller'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1501_ANameDeclaredTwice()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nPU1 pump\nPU1 valve\n", "FS1501");

        Assert.Contains("line 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1503_AParameterTheKindDoesNotHave()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nPU1 pump colour=3\n", "FS1503");

        Assert.Contains("head", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AParameterAliasBindsToItsCanonicalNameAndKeepsItsSpelling()
    {
        // D-32: the model keys on `volume`; the source keeps `v`, because write-back must not rewrite
        // a spelling the user chose.
        var model = Model("fluidscript 1\nT1 tank v=300\n");

        var parameter = Assert.Single(model.Components[0].Parameters);
        Assert.Equal("volume", parameter.Key);
        Assert.Equal("v", parameter.Value.WrittenName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnIndexedParameterBindsAgainstItsFamily()
    {
        // Every layer, because a profile that names only some of them is FS2113 and Model asserts a
        // clean bind. What is under test is that `t2` resolves against the family at all.
        var model = Model("fluidscript 1\nT1 tank layers=3 layer[1].t=50 layer[2].t=60 layer[3].t=70\n");

        Assert.True(model.Components[0].Parameters.ContainsKey("t2"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1516_AnIndexOutsideItsFamily() =>
        OnlyDiagnostic("fluidscript 1\nT1 tank in[40].level=0.5\n", "FS1516");

    [Fact]
    [Trait("Category", "Unit")]
    public void ASymbolParameterBindsItsName()
    {
        var model = Model("fluidscript 1\nV1 valve characteristic=equal_percentage\n");

        Assert.Equal("equal_percentage", model.Components[0].Parameters["characteristic"].Symbol);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1514_ASymbolParameterGivenSomethingElse() =>
        OnlyDiagnostic("fluidscript 1\nV1 valve characteristic=banana\n", "FS1514");

    // ---- steps 4-5: evaluation --------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareNumberTakesTheParametersCanonicalUnit()
    {
        // M1's criterion: power=30, power=30 kW and power=30000 W are one quantity. This is D-14, and
        // it is why the evaluator reports whether a unit took part rather than guessing later.
        var bare = Model("fluidscript 1\nHE1 heat_exchanger power=30\n");
        var kilowatts = Model("fluidscript 1\nHE1 heat_exchanger power=30 kW\n");
        var watts = Model("fluidscript 1\nHE1 heat_exchanger power=30000 W\n");

        Assert.Equal(30000, bare.Components[0].Parameters["power"].Value!.Value.SiValue, 6);
        Assert.Equal(30000, kilowatts.Components[0].Parameters["power"].Value!.Value.SiValue, 6);
        Assert.Equal(30000, watts.Components[0].Parameters["power"].Value!.Value.SiValue, 6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("p=2 bar", 200_000)]
    [InlineData("p=200 kPa", 200_000)]
    [InlineData("p=200000 Pa", 200_000)]
    [InlineData("p=200", 200_000)]
    public void ASharedPressureSpellingIsReadAgainstTheParameterItIsAssignedTo(
        string parameter, double expected)
    {
        // `L-37`. A spelling that denotes two dimensions used to fall through to "bare", so `p=2 bar`
        // bound as two kilopascals — silently, and wrong by a factor of a hundred. `kPa` survived by
        // accident, being the canonical spelling of both pressure dimensions.
        var model = Model($"fluidscript 1\nNB1 node {parameter}\n");

        Assert.Equal(expected, model.Components[0].Parameters["p"].Value!.Value.SiValue, 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OneSpellingReadsAsAReadingOrADifferenceByWhereItIsWritten()
    {
        // The reason the ambiguity exists at all: `bar` is a pressure and a pressure difference, and
        // which one is meant belongs to the destination.
        var model = Model("fluidscript 1\nNB1 node p=2 bar\nPU1 pump dp=0.005 bar\n");

        var reading = Assert.Single(model.Components, c => c.Name == "NB1").Parameters["p"].Value!.Value;
        var difference = Assert.Single(model.Components, c => c.Name == "PU1").Parameters["dp"].Value!.Value;

        Assert.Equal(Dimension.Pressure, reading.Dimension);
        Assert.Equal(200_000, reading.SiValue, 3);

        Assert.Equal(Dimension.PressureDelta, difference.Dimension);
        Assert.Equal(500, difference.SiValue, 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASignWrittenOnALiteralIsPartOfIt()
    {
        // `D-62`. `13`'s rule that an absolute temperature is never negated is about negating a
        // temperature-valued *expression*; applying it to the literal left a negative Celsius value
        // unwritable anywhere, including `design tout=-26 C`.
        var bare = Model("fluidscript 1\nNB1 node t=-5\n");
        var stated = Model("fluidscript 1\nNB1 node t=-5 C\n");

        Assert.Equal(268.15, bare.Components[0].Parameters["t"].Value!.Value.SiValue, 6);
        Assert.Equal(268.15, stated.Components[0].Parameters["t"].Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NegatingATemperatureValuedExpressionIsStillRefused()
    {
        // The narrowness of `D-62` is the point: only a sign directly on a literal is part of it.
        Assert.Contains(
            "FS1305",
            Bind("fluidscript 1\nlet t0 = 20 C\nNB1 node t=-t0\n")
                .Diagnostics.Select(static d => d.Code));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATemperatureDifferenceAddsToATemperature()
    {
        // M1's criterion: `let dT = 30 dK` then `out.t=20C+dT` is 50 °C, stored as 323.15 K.
        var model = Model("""
            fluidscript 1
            let dT = 30 dK
            HE1 heat_exchanger out.t=20 C + dT
            """);

        Assert.Equal(323.15, model.Components[0].Parameters["out"].Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1302_TwoAbsoluteTemperaturesDoNotAdd()
    {
        // The invariant the whole type system exists for. 20 °C + 30 °C is an error, not 596 K.
        //
        // The message is asserted whole rather than for the letters `dK`, because the point of it is the
        // correction it offers and a correction with the wrong number in it is worse than none: a user
        // who pastes it gets a second wrong answer and no error the second time.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger out.t=20 C + 30 C\n", "FS1302");

        Assert.Equal(
            "Cannot add two temperatures. To offset by a difference, write '20 °C + 30 dK'.",
            diagnostic.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1302_KelvinIsAnAbsoluteTemperatureAndSoDoesNotAddEither()
    {
        // `D-26`: `K` is absolute temperature and a difference is written `dK` or `dC`. So `40 C + 30 K`
        // is the same error as adding two Celsius readings, which is not obvious to anyone who has ever
        // written a temperature rise in kelvin -- and it is the reason the message has to name the
        // spelling that works.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger out.t=40 C + 30 K\n", "FS1302");

        Assert.Equal(
            "Cannot add two temperatures. To offset by a difference, write '40 °C + 30 dK'.",
            diagnostic.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ATemperatureRiseInKelvinIsWrittenWithTheDeltaSpelling()
    {
        // The other half of the pair above: what the corrected line does. 40 °C + 30 dK is 70 °C, stored
        // as 343.15 K.
        var model = Model("fluidscript 1\nHE1 heat_exchanger out.t=40 C + 30 dK\n");

        Assert.Equal(343.15, model.Components[0].Parameters["out"].Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ALetIsOrderIndependent()
    {
        // The dependency graph decides evaluation order, not source position — because write-back
        // inserts lines and must not have to reason about where.
        var model = Model("""
            fluidscript 1
            let mdot = Q / (4.18 kJ/(kg*K) * dT)
            let Q    = 30 kW
            let dT   = 20 dK
            """);

        var mdot = model.Bindings.Single(static binding => binding.Name == "mdot");
        Assert.Equal(30000d / (4180 * 20), mdot.Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1401_ASecondLetOfOneName() =>
        OnlyDiagnostic("fluidscript 1\nlet x = 1\nlet x = 2\n", "FS1401");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1402_AStaticCycleNamesEveryLink()
    {
        // M1's criterion: one diagnostic naming both, not a stack overflow. Reporting one participant
        // is the standard failure of cycle diagnostics and is useless when the cycle is four links.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nlet a = b + 1\nlet b = a + 1\n", "FS1402");

        Assert.Contains("a", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("b", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("→", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1403_DividingByZero() =>
        OnlyDiagnostic("fluidscript 1\nlet x = 1 / 0\n", "FS1403");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1404_ANameThatIsNothing()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nlet x = nosuchthing + 1\n", "FS1404");

        // Nothing is close enough to suggest, so nothing is offered. A suggestion is a structured fix
        // the editor can apply, not a clause in the sentence.
        Assert.Null(diagnostic.Suggestion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1404_ANearMissSuggestsTheName()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nlet total = 1\nlet x = totl + 1\n", "FS1404");

        Assert.Equal("total", diagnostic.Suggestion!.Replacement);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1404_TwoNamesThatSpellAlikeOnceNormalisedStillYieldASuggestion()
    {
        // Names are ordinal -- `Total` and `total` are two bindings -- but the suggestion index is
        // keyed by the normalised spelling, under which they collide. Building it with a throwing
        // dictionary took the binder down on this script; the nearest of the two is offered instead.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nlet total = 1\nlet Total = 2\nlet x = totl + 1\n", "FS1404");

        Assert.Equal("total", diagnostic.Suggestion!.Replacement);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1406_APropertyTheKindDoesNotHave()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nPU1 pump\nlet x = PU1.colour\n", "FS1406");

        Assert.Contains("head", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1408_AFunctionThatDoesNotExist()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nlet x = wibble(1, 2)\n", "FS1408");

        Assert.Contains("min", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1409_AFunctionCalledWithTheWrongCount() =>
        OnlyDiagnostic("fluidscript 1\nlet x = abs(1, 2)\n", "FS1409");

    [Fact]
    [Trait("Category", "Unit")]
    public void TheFunctionSetEvaluates()
    {
        var model = Model("""
            fluidscript 1
            let a = min(3 kW, 5 kW)
            let b = max(3 kW, 5 kW)
            let c = abs(0 kW - 4 kW)
            let d = round(1.267, 2)
            let e = pow(2, 10)
            let f = sqrt(16)
            """);

        double Value(string name) => model.Bindings.Single(binding => binding.Name == name).Value!.Value.SiValue;

        Assert.Equal(3000, Value("a"), 6);
        Assert.Equal(5000, Value("b"), 6);
        Assert.Equal(4000, Value("c"), 6);
        Assert.Equal(1.27, Value("d"), 6);
        Assert.Equal(1024, Value("e"), 6);
        Assert.Equal(4, Value("f"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrecedenceAndParenthesesSurviveEvaluation()
    {
        var model = Model("fluidscript 1\nlet a = 2 + 3 * 4\nlet b = (2 + 3) * 4\n");

        Assert.Equal(14, model.Bindings.Single(static x => x.Name == "a").Value!.Value.SiValue, 6);
        Assert.Equal(20, model.Bindings.Single(static x => x.Name == "b").Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AReferenceToADeclaredParameterEvaluatesAtOnce()
    {
        var model = Model("""
            fluidscript 1
            HE1 heat_exchanger power=30 kW
            let doubled = HE1.power * 2
            """);

        Assert.Equal(60000, model.Bindings.Single(static x => x.Name == "doubled").Value!.Value.SiValue, 6);
        Assert.Empty(model.Deferred);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AReferenceToASolvedValueDefersRatherThanFailing()
    {
        // 14's central case: `head=1.2*HE1.dp` is the expression a designer wants to write. It is not
        // a cycle and not an error — it is deferred to the outer sizing loop.
        var model = Model("""
            fluidscript 1
            HE1 heat_exchanger power=30 kW
            PU1 pump head=1.2 * HE1.dp
            """);

        var deferred = Assert.Single(model.Deferred);
        Assert.Equal("PU1.head", deferred.Target.ToString());
        Assert.Contains(deferred.Dependencies, static id => id.ToString() == "HE1.dp");
        Assert.Null(model.Components.Single(static c => c.Name == "PU1").Parameters["head"].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADeferredLetIsTypedWithoutBeingEvaluated()
    {
        // U-5: `let x = 1.2*HE1.dp` has no value until the solve but is a pressure difference the
        // moment HE1's kind is known, so completion after `dp=` may offer it and after `power=` must
        // not. A dimension-only pass: units and properties type, a bare number beside them is a number,
        // a product combines vectors, and a let reading a let follows the chain.
        var model = Model("""
            fluidscript 1
            design tout=-26
            curve heating tout
            -26 50
            20 0
            HE1 heat_exchanger power=30 kW
            let x = 1.2 * HE1.dp
            let y = x / 2 + 5 kPa
            let z = heating * 2
            let w = HE1.power / (4180 J/(kg*K) * 20 dK)
            let v = HE1.flow * 3
            let u = HE1.dp kPa
            """);

        Dimension? Of(string name) => model.Bindings.Single(b => b.Name == name).Dimension;

        Assert.Null(model.Bindings.Single(static b => b.Name == "x").Value);
        Assert.Equal(Dimension.PressureDelta, Of("x"));
        Assert.Equal(Dimension.PressureDelta, Of("y"));
        // A curve in a static circuit is read at the design point, so `z` is not deferred at all: 100, bare.
        Assert.Equal(100, model.Bindings.Single(static b => b.Name == "z").Value!.Value.SiValue, 9);
        Assert.Equal(Dimension.Dimensionless, Of("z"));
        Assert.Equal(Dimension.MassFlow, Of("w"));
        Assert.Equal(Dimension.MassFlow, Of("v"));
        Assert.Equal(Dimension.PressureDelta, Of("u"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1306_AValueFarOutsideItsUsualRange()
    {
        // The real-world failure: `power=30000` meaning watts draws a plausible diagram of a 30 MW
        // plant, and nothing else in the pipeline objects.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger power=300000\n", "FS1306");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain("FS1306", OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger power=30\n", "FS1519").Code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1304_AValueOfTheWrongDimension()
    {
        var diagnostic = OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger power=30 kg/s\n", "FS1304");

        Assert.Contains("power", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1305_TwoDimensionsThatDoNotCombine() =>
        OnlyDiagnostic("fluidscript 1\nlet x = 30 kW + 2 kg/s\n", "FS1305");

    [Fact]
    [Trait("Category", "Unit")]
    public void BindingNeverThrowsOnAnythingTheParserProduces()
    {
        // The same corpus the parser fuzz uses, bound rather than parsed. A script under editing is
        // malformed most of the time, and the binder is the stage after the one that knows that.
        foreach (var text in ScriptCorpus.Adversarial)
        {
            Assert.NotNull(Bind(text).Model);
        }

        foreach (var text in ScriptCorpus.Mutations(2_000, seed: 20260902))
        {
            Assert.NotNull(Bind(text).Model);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EverySampleBindsWithNoError()
    {
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = Bind(sample.Text, sample.Name);
            var errors = result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

            Assert.True(
                errors.Length == 0,
                $"{sample.Name}: {string.Join("; ", errors.Select(static d => $"{d.Code} {d.Message}"))}");
        }
    }

    // ---- what must not take the process down ------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ADependencyChainTwentyThousandDeepEvaluatesRatherThanOverflowingTheStack()
    {
        // Every `let` reads the one declared *after* it, so the walk from the first has to descend the
        // whole chain before anything finishes -- declared the other way round, each value is finished
        // before the next starts and the walk never goes deep. A recursive walk overflowed on this; a
        // stack overflow cannot be caught.
        const int Length = 20_000;
        var text = new System.Text.StringBuilder("fluidscript 1\n");

        for (var i = 0; i < Length - 1; i++)
        {
            text.Append("let v").Append(i).Append(" = v").Append(i + 1).Append(" + 1\n");
        }

        text.Append("let v").Append(Length - 1).Append(" = 1\n");

        var model = Model(text.ToString());

        Assert.Equal(Length, model.Bindings.Single(static b => b.Name == "v0").Value!.Value.SiValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AMinusOverAnUnknownNameReportsItOnce()
    {
        // The operand used to be visited twice -- once to classify, once to return -- and every visit
        // reports; three minus signs over a bad operand reported it eight times.
        var result = Bind("fluidscript 1\nlet x = ---nosuchthing\n");

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1404");
    }
}
