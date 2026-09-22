using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Model;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Model;

/// <summary><c>26</c>'s acceptance criteria that need no serializer; the goldens and the round trip are the Api's tests.</summary>
[Trait("Category", "Unit")]
public sealed class ModelContractBuilderTests
{
    
    // ---- round trip ------------------------------------------------------------------------------------

    // ---- units and values ---------------------------------------------------------------------------------

    [Fact]
    public async Task EveryValueIsInItsCanonicalUnitAndSaysSo()
    {
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(ContractFixture.Sample("m2-cooling-loop.fluid")));
        var he1 = contract.Components.Single(static c => c.Id == "HE1");
        var n1 = contract.Components.Single(static c => c.Id == "N1");

        Assert.Equal((30.0, "kW", "stated"), (he1.Parameters["power"].Value, he1.Parameters["power"].Unit, he1.Parameters["power"].Source));
        Assert.Equal((20.0, "°C"), (he1.Parameters["in"].Value, he1.Parameters["in"].Unit));
        Assert.Equal((300.0, "kPa", "stated"), (n1.Parameters["p"].Value, n1.Parameters["p"].Unit, n1.Parameters["p"].Source));
        Assert.Equal((6.0, "°C"), (n1.State!.T!.Value, n1.State.T.Unit));
        Assert.Equal("kg/s", he1.State!.Flow!.Unit);
        Assert.Equal(30.0, he1.State.Power!.Value!.Value, 2);
        Assert.Equal(50.0, he1.State.TOut!.Value!.Value, 1);
        Assert.Equal(20.0, he1.State.TIn!.Value!.Value, 1);
    }

    [Fact]
    public async Task APipeCarriesTheVelocityAndReynoldsNumberItsDropWasComputedAt()
    {
        // A-6: the connection card wants velocity and Reynolds number, and only Core has the bore. Both
        // are read at the pipe's own mean properties, so v = ṁ / (ρ A) reproduces from the wire's flow
        // and the bore, and Re at 0.24 kg/s through DN25 water is well into the turbulent regime.
        var input = await ContractFixture.SolveAsync(ContractFixture.Sample("m2-simple-loop.fluid"));
        var contract = ModelContractBuilder.Build(input);
        var wire = contract.Components.Single(static c => c.Id == "N5__N1");
        var pipe = (Pipe)input.Graph.Components.Single(static c => c.Name == "N5__N1");

        var velocity = wire.State!.Velocity!.Value!.Value;
        var flow = Math.Abs(wire.State.Flow!.Value!.Value);
        Assert.Equal("m/s", wire.State.Velocity.Unit);
        Assert.InRange(velocity, 0.2, 1.5);

        // Back out the density the report used; it must be that of water near 20 °C.
        var density = flow / (velocity * pipe.FlowArea);
        Assert.InRange(density, 990, 1000);

        // Water's viscosity at the return's 20 °C is 1.0e-3 Pa·s; the mean over the pipe is within a few percent of it.
        var re = wire.State.Re!.Value!.Value;
        Assert.InRange(re, 4000, 50_000);
        Assert.Equal(density * velocity * pipe.InsideDiameter / 1.0e-3, re, re * 0.1);

        // Nothing but a pipe reports them.
        Assert.Null(contract.Components.Single(static c => c.Id == "PU1").State!.Velocity);
    }

    [Fact]
    public void ADefaultCarriesItsBasisAndAStatedValueNone()
    {
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid")));
        var he1 = contract.Components.Single(static c => c.Id == "HE1");

        var dp = he1.Parameters["dp"];
        Assert.Equal("default", dp.Source);
        Assert.NotNull(dp.Basis);
        Assert.Equal((20.0, "kPa"), (dp.Value, dp.Unit));
        Assert.Null(he1.Parameters["power"].Basis);
    }

    [Fact]
    public async Task ASizedValueCarriesTheBasisTheOuterLoopWrote()
    {
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(ContractFixture.Sample("m2-distribution-header.fluid")));
        var pump = contract.Components.Single(static c => c.Id == "PU_AHU");

        // The head is promoted, not ruled: the solver found it, and the wire says so as `sized`.
        var head = pump.Parameters["head"];
        Assert.Equal("sized", head.Source);
        Assert.NotNull(head.Basis);
        Assert.Equal("m", head.Unit);
        Assert.Equal(head.Value, pump.State!.Solved!["head"].Value);

        var dn = contract.Components.Single(static c => c.Id == "N3__TV_AHU").Parameters["dn"];
        Assert.Equal("stated", dn.Source);
    }

    [Fact]
    public void TheWireUnitsArePinnedToTheContractVersion()
    {
        // Changing a dimension's canonical unit changes the wire, which is a major bump (26): this
        // table is the version's, and it fails until both move together.
        Assert.Equal("2.2", ModelContractBuilder.ContractVersion);

        var pinned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Temperature"] = "°C", ["TemperatureDelta"] = "dK", ["Pressure"] = "kPa", ["PressureDelta"] = "kPa",
            ["Power"] = "kW", ["MassFlow"] = "kg/s", ["Volume"] = "dm3", ["Length"] = "m",
            ["Enthalpy"] = "J/kg", ["Density"] = "kg/m3", ["Energy"] = "J",
        };

        foreach (var (dimension, unit) in pinned)
        {
            var actual = typeof(Dimension).GetProperty(dimension)!.GetValue(null) is Dimension d ? UnitTable.CanonicalUnitFor(d)?.Text : null;
            Assert.True(string.Equals(unit, actual, StringComparison.Ordinal), $"{dimension}: contract 2.0 pins '{unit}', the table says '{actual}'.");
        }

        // No canonical spelling: these go out in SI, and that too is pinned.
        Assert.Null(UnitTable.CanonicalUnitFor(Dimension.Head));
        Assert.Equal("m", Dimension.Head.SiUnit);
        Assert.Null(UnitTable.CanonicalUnitFor(Dimension.Kv));
        Assert.Equal("m3/h", Dimension.Kv.SiUnit);
    }

    // ---- exchanger modes ------------------------------------------------------------------------------------

    [Fact]
    public void ExchangerModesAreCarriedNotInferred()
    {
        var duty = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid")));
        var coupled = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m2-substation.fluid")));
        var rated = ModelContractBuilder.Build(ContractFixture.Compile(
            ContractFixture.Sample("m2-cooling-loop.fluid").Replace("HE1 heat_exchanger power=30 in.t=20 out.t=50", "HE1 heat_exchanger in.t=20 out.t=50 in[2].t=80 out[2].t=60 ua=2")));

        Assert.Equal("duty", duty.Components.Single(static c => c.Id == "HE1").Mode);
        Assert.Equal("coupled", coupled.Components.Single(static c => c.Id == "HX1").Mode);
        Assert.Equal("duty", coupled.Components.Single(static c => c.Id == "LOAD").Mode);
        Assert.Equal("rated", rated.Components.Single(static c => c.Id == "HE1").Mode);
        Assert.Null(duty.Components.Single(static c => c.Id == "PU1").Mode);
    }

    // ---- provenance ---------------------------------------------------------------------------------------------

    [Fact]
    public void ProvenanceNamesEverythingThatProducedThePayload()
    {
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid")));

        Assert.StartsWith("sha256:", contract.Provenance.SourceHash, StringComparison.Ordinal);
        Assert.Equal(64 + 7, contract.Provenance.SourceHash.Length);
        Assert.Equal(1, contract.Provenance.LanguageMajor);
        Assert.Equal("steel_en10255", contract.Provenance.Catalog.Id);
        Assert.NotEmpty(contract.Provenance.Catalog.Version);
        Assert.Equal("sharp-prop", contract.Provenance.PropertyBackend.Id);
        Assert.NotEqual("unknown", contract.Provenance.PropertyBackend.Version);
        Assert.Equal(101.325, contract.Provenance.AtmosphereKPaAbsolute);
    }

    // ---- storage header --------------------------------------------------------------------------------------

    [Fact]
    public async Task TheStorageHeaderCarriesItsStagesAndTheTanksPorts()
    {
        var compiled = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m4-storage-header.fluid")));
        var solved = ModelContractBuilder.Build(await ContractFixture.SolveAsync(ContractFixture.Sample("m4-storage-header.fluid")));

        foreach (var contract in new[] { compiled, solved })
        {
            Assert.Equal(
                [(0, "source"), (1, "storage"), (2, "consumer")],
                contract.Layout.ThermalStages.Select(static s => (s.Rank, s.Role)));

            var tank = contract.Components.Single(static c => c.Id == "T1");
            Assert.Equal("tank", tank.Kind);
            Assert.Equal("tank.stratified", tank.SymbolId);
            Assert.Equal((300.0, "dm3", "stated"), (tank.Parameters["volume"].Value, tank.Parameters["volume"].Unit, tank.Parameters["volume"].Source));
            Assert.Equal(["in1", "in2", "out1", "out2"], tank.Ports.Select(static p => p.Name));
            Assert.Equal([0.9, 0.3, 0.9, 0.3], tank.Ports.Select(static p => p.Elevation!.Value));
            Assert.Equal([5, 2, 5, 2], tank.Ports.Select(static p => p.Layer!.Value));
            Assert.All(tank.Ports, static p => Assert.Equal("bidirectional", p.Role));
        }

        Assert.NotNull(solved.Components.Single(static c => c.Id == "T1").State?.T);
    }

    [Fact]
    public void AContainerIsATankOnTheWire()
    {
        var source = ContractFixture.Sample("m4-storage-header.fluid").Replace("T1 tank volume=300", "T1 container v=300");
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));
        var tank = contract.Components.Single(static c => c.Id == "T1");

        Assert.Equal("tank", tank.Kind);
        Assert.Equal((300.0, "dm3"), (tank.Parameters["volume"].Value, tank.Parameters["volume"].Unit));
    }

    // ---- groups ----------------------------------------------------------------------------------------------------

    [Fact]
    public void AnExpandedPipeIsOneGroupAndNineExpandedComponents()
    {
        var source = ContractFixture.Sample("m2-cooling-loop.fluid").Replace("3WV - N3 length=25 dn=25", "3WV - N3 length=25 dn=25 nodes=4", StringComparison.Ordinal);
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));

        var group = Assert.Single(contract.Layout.Groups);
        Assert.Equal("3WV__N3", group.ParentComponentId);
        Assert.Equal(9, group.Children.Length);
        Assert.Equal(9, contract.Components.Count(static c => c.Origin == "expanded"));
        Assert.All(group.Children, child => Assert.Contains(contract.Components, c => c.Id == child));
    }

    // ---- size cap ------------------------------------------------------------------------------------------------

    [Fact]
    public void StatesOmittedIsTheBuildersToSetAndTheCircuitsToReport()
    {
        // The cap is measured on the serialized form, which is the Api's; Core only knows how to leave
        // the states out when asked (26).
        var input = ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid").Replace("3WV - N3 length=25 dn=25", "3WV - N3 length=25 dn=25 nodes=100", StringComparison.Ordinal));
        var whole = ModelContractBuilder.Build(input);
        var omitted = ModelContractBuilder.Build(input, statesOmitted: true);

        Assert.Equal(210, whole.Components.Length);
        Assert.All(whole.Circuits, static c => Assert.False(c.StatesOmitted));
        Assert.All(omitted.Circuits, static c => Assert.True(c.StatesOmitted));
        Assert.All(omitted.Components, static c => Assert.Null(c.State));
        Assert.Equal(whole.Layout.Order, omitted.Layout.Order);
    }

    // ---- symbols -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EverySymbolResolvesAndEveryPortHasAnAnchor()
    {
        foreach (var sample in new[] { "m2-cooling-loop", "m2-substation", "m4-storage-header", "m2-distribution-header", "m1-syntax-tour" })
        {
            // The syntax tour is not one circuit and does not solve; compile-only is what it has.
            var contract = ModelContractBuilder.Build(sample == "m1-syntax-tour"
                ? ContractFixture.Compile(ContractFixture.Sample(sample + ".fluid"))
                : await ContractFixture.SolveAsync(ContractFixture.Sample(sample + ".fluid"), sample));
            var symbols = contract.Symbols.ToDictionary(static s => s.Id, StringComparer.Ordinal);

            foreach (var component in contract.Components)
            {
                var symbol = Assert.Contains(component.SymbolId, symbols);

                foreach (var port in component.Ports)
                {
                    var anchored = symbol.PortAnchors.ContainsKey(port.Name)
                        || symbol.PortAnchors.ContainsKey("*")
                        || symbol.IndexedPortAnchors?.Any(rule => port.Name.StartsWith(rule.Prefix, StringComparison.Ordinal)) == true;

                    Assert.True(anchored, $"{sample}: {component.Id}.{port.Name} has no anchor on {symbol.Id}.");
                }
            }

            Assert.All(contract.Components.Where(static c => c.Ports.Length > 0), c => Assert.Contains(c.Id, contract.Layout.Order));
            Assert.All(contract.Components.Where(static c => c.Ports.Length == 0), c => Assert.Contains(contract.Layout.NonFlowElements, e => e.ComponentId == c.Id));
        }
    }



    // ---- diagnostics --------------------------------------------------------------------------------------------------

    [Fact]
    public void DiagnosticsCarryBothPositionFormsFromOneLineIndex()
    {
        var source = ContractFixture.Sample("m2-cooling-loop.fluid")
            .Replace("show temperature", "show temperature\nlet duty = 30 kW")
            .Replace("HE1 heat_exchanger power=30", "HE1 heat_exchanger power=dutyy zzz=1");
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));

        // `zzz` is nobody's parameter: an error, with a range. `dutyy` is one letter from a `let`,
        // and the diagnostic saying so carries the fix.
        var unknown = contract.Diagnostics.First(static d => d.Code == "FS1503");
        Assert.Equal("error", unknown.Severity);
        Assert.NotNull(unknown.Range);
        Assert.StartsWith("zzz", source.Split('\n')[unknown.Range.Start.Line][unknown.Range.Start.Character..], StringComparison.Ordinal);
        Assert.Equal(unknown.Range.Offset, source.IndexOf("zzz", StringComparison.Ordinal));
        Assert.Equal(unknown.Range.Start.Line, unknown.Range.End.Line);
        Assert.Equal(unknown.Range.Length, unknown.Range.End.Character - unknown.Range.Start.Character);

        var fix = contract.Diagnostics.First(static d => d.Suggestion is not null);
        Assert.Equal("duty", fix.Suggestion!.NewText);
        Assert.Equal("dutyy", source.Substring(fix.Suggestion.Range.Offset, fix.Suggestion.Range.Length));

        // Errors first, then warnings, then the inferences; within a severity by offset.
        var severities = contract.Diagnostics.Select(static d => d.Severity switch { "error" => 0, "warning" => 1, _ => 2 }).ToArray();
        Assert.Equal(severities.Order(), severities);
        // A diagnostic about no source text -- the layout's -- has no range; an inference points at the line that caused it.
        Assert.Contains(contract.Diagnostics, static d => d.Code == "FS2401" && d.Range is null);
        Assert.Contains(contract.Diagnostics, static d => d.Code == "FS1510" && d.Range is not null);
    }

    // ---- visualization ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheShowDirectiveResolvesToAScaleOverTheSolvedNodes()
    {
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(ContractFixture.Sample("m2-cooling-loop.fluid")));

        Assert.Equal("temperature", contract.Visualization.Active);
        Assert.Equal("°C", contract.Visualization.Scale.Unit);
        Assert.Equal("sequential", contract.Visualization.Scale.Kind);
        var domain = Assert.IsType<DomainWire>(contract.Visualization.Scale.Domain);
        Assert.True(domain.Nice);
        Assert.True(domain.Min <= 6 && domain.Max >= 50, $"{domain.Min}..{domain.Max}");
        Assert.False(contract.Visualization.Scale.Degenerate);
    }

    [Fact]
    public void AFixedRangeIsCarriedAsWritten()
    {
        var source = ContractFixture.Sample("m2-cooling-loop.fluid").Replace("show temperature", "show pressure 0..400");
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));

        Assert.Equal("pressure", contract.Visualization.Active);
        Assert.Equal(new DomainWire(0, 400, Nice: false), contract.Visualization.Scale.Domain);
        Assert.Equal(["pressure", "temperature", "flow"], contract.Visualization.Available);
    }

    // ---- style and bindings ---------------------------------------------------------------------------------------------

    [Fact]
    public void StyleTokensAndBindingsAreCarriedVerbatim()
    {
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m1-syntax-tour.fluid")));

        Assert.NotEmpty(contract.Bindings);
        Assert.All(contract.Bindings, static b => Assert.NotNull(b.Value));
        Assert.NotNull(contract.Project);
    }

    [Fact]
    public async Task AValueThatIsNotFiniteGoesOutEmptyWithFS2501()
    {
        // A solved vector with NaN in a branch flow: nothing upstream should produce one, and if it
        // does the wire says so rather than writing "NaN" into a number field.
        var input = await ContractFixture.SolveAsync(ContractFixture.Sample("m2-cooling-loop.fluid"));
        var run = input.Run!;
        var layout = Core.Solvers.SystemLayout.Build(run.Graph, Core.Topology.WellPosedness.Check(run.Graph).Counting);
        var poisoned = run.Solve.Solution.Values.SetItem(layout.BranchFlow(0), double.NaN);
        var contract = ModelContractBuilder.Build(input with { Run = run with { Solve = run.Solve with { Solution = new Core.Solvers.StateVector(poisoned) } } });

        var raised = contract.Diagnostics.Where(static d => d.Code == "FS2501").ToArray();
        Assert.NotEmpty(raised);
        Assert.All(raised, static d => Assert.Equal("error", d.Severity));
        Assert.Contains(contract.Components.Where(static c => c.State?.Flow is not null), static c => c.State!.Flow!.Value is null);
    }

    [Fact]
    public void ALayoutThatBreaksItsOwnStandardSaysSoWithFS5002()
    {
        // C-101: the audit's hard checks ran over fixtures only, so a user's script could get a picture the
        // engine would have failed on. Now every layout is audited and a hard breach is a warning on the wire.
        // Two bare nodes joined twice have no boxed member for any ring rule to seat (the two-pump ring that
        // stood here breaches no more since C-102 seated it), so every form declines, the fallback stacks
        // the nodes and the two direct links overlap; when a rule learns this shape too, this test needs a
        // new breaching script, not deleting -- the guardrail must stay provably live.
        var breached = ModelContractBuilder.Build(ContractFixture.Compile("fluidscript 1\n\ncircuit plant\n\nconnections\nN1 - N2 - N1\n"));
        var ring = ModelContractBuilder.Build(ContractFixture.Compile("fluidscript 1\n\ncircuit plant\n\nPU1 pump\n\nconnections\nPU1 - PU1\n"));

        var raised = breached.Diagnostics.Where(static d => d.Code == "FS5002").ToArray();
        Assert.NotEmpty(raised);
        Assert.All(raised, static d => Assert.Equal("warning", d.Severity));
        Assert.All(raised, static d => Assert.Null(d.Range));
        Assert.DoesNotContain(ring.Diagnostics, static d => d.Code == "FS5002");
    }

    // ---- the colour scales (57, D-117) ----------------------------------------------------------------------

    [Fact]
    public void AShowDirectiveIsReadAndItsMistakesAreSaid()
    {
        // 57's error cases were specified and never raised; `show nonsense` silently showed temperature.
        var source = ContractFixture.Sample("m2-cooling-loop.fluid")
            .Replace("show temperature", "show nonsense t temperature h\nshow p", StringComparison.Ordinal);
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));

        var unknown = Assert.Single(contract.Diagnostics, static d => d.Code == "FS1210");
        Assert.Contains("'nonsense'", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("density", unknown.Message, StringComparison.Ordinal);
        Assert.Equal("warning", unknown.Severity);
        var twice = Assert.Single(contract.Diagnostics, static d => d.Code == "FS1213");
        Assert.Equal("info", twice.Severity);
        var second = Assert.Single(contract.Diagnostics, static d => d.Code == "FS1214");
        Assert.Equal("warning", second.Severity);
        Assert.NotNull(second.Range);

        // The first directive stands: temperature first, enthalpy after it, then the three every model offers.
        Assert.Equal("temperature", contract.Visualization.Active);
        Assert.Equal(["temperature", "enthalpy", "pressure", "flow"], contract.Visualization.Available);
    }

    [Fact]
    public async Task AChangeIsShownAsItsBaseQuantityAcrossTheComponentWithItsDirectionWord()
    {
        // D-123: `d` on a state symbol is that quantity's change across the component. A pressure
        // drops (inlet less outlet: positive across the coil, negative across the pump); a temperature
        // and an enthalpy rise (outlet less inlet: negative across a cooling coil). A node has no change.
        var source = ContractFixture.Sample("m2-cooling-loop.fluid")
            .Replace("show temperature", "show dt dh dp specific_heat", StringComparison.Ordinal);
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(source));
        var visualization = contract.Visualization;

        Assert.Equal("temperature_change", visualization.Active);
        Assert.Equal("dK", visualization.Scales["temperature_change"].Unit);
        Assert.Equal("J/kg", visualization.Scales["enthalpy_change"].Unit);
        Assert.Equal("J/(kg*K)", visualization.Scales["specific_heat"].Unit);

        var heater = contract.Layout.Placements.Single(static p => p.ComponentId == "HE1");
        var pump = contract.Layout.Placements.Single(static p => p.ComponentId == "PU1");
        var node = contract.Layout.Placements.Single(static p => p.ComponentId == "N1");

        Assert.True(heater.Scales["temperature_change"].At > 0.5, "the 20 → 50 °C heater's change is a rise: above the diverging scale's middle");
        Assert.True(heater.Scales["enthalpy_change"].At > 0.5);
        Assert.True(heater.Scales["pressure_drop"].At > 0.5, "the heater drops pressure");
        Assert.True(pump.Scales["pressure_drop"].At < 0.5, "a pump's drop is negative");
        Assert.True(pump.Scales["temperature_change"].At is > 0.45 and < 0.55, "a pump changes no temperature");
        Assert.Null(node.Scales["temperature_change"].At);
        Assert.NotNull(node.Scales["specific_heat"].At);
    }

    [Fact]
    public async Task EveryAvailableScaleIsOnTheWireWithEveryElementsPlaceOnIt()
    {
        // D-117: switching the shown property is a re-preparation on the client, never a request, so
        // every available scale travels with its domain and every element's position on it. Enthalpy
        // and density were documented and drew nothing before this (the mapper had no case for them).
        var source = ContractFixture.Sample("m2-cooling-loop.fluid")
            .Replace("show temperature", "show density enthalpy", StringComparison.Ordinal);
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(source));
        var visualization = contract.Visualization;

        Assert.Equal("density", visualization.Active);
        Assert.Equal(visualization.Scale, visualization.Scales["density"]);
        Assert.Equal(["density", "enthalpy", "flow", "pressure", "temperature"], visualization.Scales.Keys.Order(StringComparer.Ordinal));
        Assert.All(visualization.Scales.Values, static scale => Assert.NotNull(scale.Domain));
        Assert.Equal("kg/m3", visualization.Scales["density"].Unit);

        var n1 = contract.Layout.Placements.Single(static p => p.ComponentId == "N1");
        Assert.Equal(n1.Scale, n1.Scales["density"].At);
        Assert.NotNull(n1.Scales["density"].At);
        Assert.NotNull(n1.Scales["enthalpy"].At);
        // The coldest node is the densest: highest on the density scale, low on temperature's (57: the domain includes N1); a niced domain reaches past it, so neither is exactly an end.
        Assert.Equal(n1.Scales["density"].At, contract.Layout.Placements.Max(static p => p.Scales["density"].At));
        Assert.True(n1.Scales["temperature"].At < 0.2);

        // An exchanger has an inlet and an outlet on every scale; a node has neither.
        var he1 = contract.Layout.Placements.Single(static p => p.ComponentId == "HE1");
        Assert.NotNull(he1.Scales["temperature"].From);
        Assert.NotNull(he1.Scales["temperature"].To);
        Assert.True(he1.Scales["temperature"].From < he1.Scales["temperature"].To);
        Assert.Null(n1.Scales["temperature"].From);

        // A route ends at the inlet value of what it enters: the pipe into the pump is at suction pressure.
        var pump = contract.Layout.Placements.Single(static p => p.ComponentId == "PU1");
        var intoPump = contract.Layout.Routes.Single(static r => r.Id == "c1");
        Assert.Equal(pump.Scales["pressure"].From, intoPump.Scales["pressure"].To);
        Assert.True(pump.Scales["pressure"].From < pump.Scales["pressure"].To);
        Assert.Equal(intoPump.Scales[visualization.Active].To, intoPump.ScaleTo);
    }

    [Fact]
    public void BeforeASolveEveryScaleIsThereWithNoDomain()
    {
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid")));
        Assert.All(contract.Visualization.Scales.Values, static scale => Assert.Null(scale.Domain));
        Assert.All(contract.Layout.Placements, static p => Assert.All(p.Scales.Values, static s => Assert.Null(s.At)));
    }

    [Fact]
    public async Task APortAComponentDischargesThroughReadsItsOwnOutletNotTheMixedNode()
    {
        // C-103: the diverting valve passes HE1's 50 °C into N2, where the 6 °C primary also arrives. Its
        // outlet is 50 °C; the node is the mix. Before the fix the valve, and the route out of it, read the mix.
        var contract = ModelContractBuilder.Build(await ContractFixture.SolveAsync(ContractFixture.Sample("m2-cooling-loop.fluid")));
        var valve = contract.Components.Single(static c => c.Id == "3WV");
        var exchanger = contract.Components.Single(static c => c.Id == "HE1");
        var mixing = contract.Components.Single(static c => c.Id == "N2");

        Assert.Equal(exchanger.State!.TOut!.Value!.Value, valve.State!.TOut!.Value!.Value, 1);
        Assert.Equal(valve.State.TIn!.Value!.Value, valve.State.TOut.Value.Value, 1);
        Assert.True(mixing.State!.T!.Value < valve.State.TOut.Value - 5);

        // The recirculation route (c6: 3WV.a - N2) leaves the valve at the valve's outlet position, not the node's.
        var placement = contract.Layout.Placements.Single(static p => p.ComponentId == "3WV");
        var recirculation = contract.Layout.Routes.Single(static r => r.Id == "c6");
        Assert.Equal(placement.Scales["temperature"].To, recirculation.Scales["temperature"].From);
        Assert.True(recirculation.Scales["temperature"].From > recirculation.Scales["temperature"].To);
    }

    [Fact]
    public void NoCoreTypeIsOnTheWire()
    {
        var wire = typeof(ModelContract).Assembly.GetTypes()
            .Where(static t => t.Namespace == "FluidScript.Core.Model" && t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Wire", StringComparison.Ordinal) || t == typeof(ModelContract) || t == typeof(Provenance));

        foreach (var type in wire)
        {
            foreach (var property in type.GetProperties())
            {
                var leaf = Leaf(property.PropertyType);
                Assert.True(
                    leaf.Namespace is null || leaf.Namespace.StartsWith("System", StringComparison.Ordinal) || leaf.Namespace == "FluidScript.Core.Model",
                    $"{type.Name}.{property.Name} exposes {leaf.FullName}.");
            }
        }

        static Type Leaf(Type type)
        {
            if (Nullable.GetUnderlyingType(type) is { } inner)
            {
                return Leaf(inner);
            }

            if (type.IsGenericType)
            {
                return Leaf(type.GetGenericArguments()[^1]);
            }

            return type;
        }
    }
}
