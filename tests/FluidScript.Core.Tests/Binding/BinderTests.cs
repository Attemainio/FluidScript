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

        // One declaration, and the two I3 boundary nodes its non-optional ports now terminate: `c` is
        // optional, so a three-way valve alone in a file produces exactly two.
        var component = Assert.Single(model.Components, symbol => symbol.Origin is Origin.Declared);
        Assert.Equal("3WV", component.Name);
        Assert.Equal("three_way_valve", component.Kind!.Keyword);
        Assert.Empty(component.Parameters);
        Assert.Equal(["3WV__a", "3WV__b"], model.Components
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
        var model = Model("fluidscript 1\nT1 tank layers=3 t2=60\n");

        Assert.True(model.Components[0].Parameters.ContainsKey("t2"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1516_AnIndexOutsideItsFamily() =>
        OnlyDiagnostic("fluidscript 1\nT1 tank in40_elevation=0.5\n", "FS1516");

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

    [Fact]
    [Trait("Category", "Unit")]
    public void ATemperatureDifferenceAddsToATemperature()
    {
        // M1's criterion: `let dT = 30 dK` then `out=20C+dT` is 50 °C, stored as 323.15 K.
        var model = Model("""
            fluidscript 1
            let dT = 30 dK
            HE1 heat_exchanger out=20 C + dT
            """);

        Assert.Equal(323.15, model.Components[0].Parameters["out"].Value!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1302_TwoAbsoluteTemperaturesDoNotAdd()
    {
        // The invariant the whole type system exists for. 20 °C + 30 °C is an error, not 596 K.
        var diagnostic = OnlyDiagnostic("fluidscript 1\nHE1 heat_exchanger out=20 C + 30 C\n", "FS1302");

        Assert.Contains("dK", diagnostic.Message, StringComparison.Ordinal);
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
}
