using System.Collections.Immutable;

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

        var dn = contract.Components.Single(static c => c.Id == "PA1").Parameters["dn"];
        Assert.Equal("stated", dn.Source);
    }

    [Fact]
    public void TheWireUnitsArePinnedToTheContractVersion()
    {
        // Changing a dimension's canonical unit changes the wire, which is a major bump (26): this
        // table is the version's, and it fails until both move together.
        Assert.Equal("2.0", ModelContractBuilder.ContractVersion);

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
            ContractFixture.Sample("m2-cooling-loop.fluid").Replace("HE1 heat_exchanger power=30 in=20 out=50", "HE1 heat_exchanger in=20 out=50 in2=80 out2=60 ua=2")));

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
        var source = ContractFixture.Sample("m2-cooling-loop.fluid").Replace("P1  pipe length=25 dn=25", "P1  pipe length=25 dn=25 nodes=4");
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(source));

        var group = Assert.Single(contract.Layout.Groups);
        Assert.Equal("P1", group.ParentComponentId);
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
        var input = ContractFixture.Compile(ContractFixture.Sample("m2-cooling-loop.fluid").Replace("P1  pipe length=25 dn=25", "P1  pipe length=25 dn=25 nodes=100"));
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
