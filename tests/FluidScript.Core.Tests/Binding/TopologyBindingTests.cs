using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// Binding steps 6 through 11 from <c>plan/10-language/15-semantic-model.md</c>, plus the schedule
/// step its order never had. The last four M1 exit criteria from <c>05</c> are here, named where they
/// are.
/// </summary>
public sealed class TopologyBindingTests
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

    private static ImmutableArray<string> Codes(BindResult result) =>
        [.. result.Diagnostics.Select(static d => d.Code)];

    private static string Link(ConnectionSymbol connection) =>
        $"{Endpoint(connection.From)}-{Endpoint(connection.To)}";

    private static string Endpoint(EndpointSymbol endpoint) =>
        endpoint.Port.Length == 0 ? endpoint.Component : $"{endpoint.Component}.{endpoint.Port}";

    // ---- the fixed nine, which is what P2 closes on ----------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSyntaxReferenceProducesExactlyItsNineDiagnostics()
    {
        // M1, and `01`'s table: one FS1507, two FS2107, six FS1510. Three documents disagreed about
        // this count before `01` fixed it, so it is asserted from the sample file itself rather than
        // from a copy that could drift away from the one the documentation shows.
        var path = Path.Combine(RepositoryLayout.Samples, "m1-syntax-reference.fluid");
        var result = Bind(File.ReadAllText(path), "m1-syntax-reference.fluid");

        var counts = result.Diagnostics
            .GroupBy(static d => d.Code, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["FS1507"] = 1,
                ["FS2107"] = 2,
                ["FS1510"] = 6,
            },
            counts);

        // The identities matter as much as the counts: PU1 is the unconnected one, and N1 and N3 are
        // the dead ends. A different six inferred components would give the same total.
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1507" && d.Message.Contains("PU1"));
        Assert.Equal(
            ["N1", "N3"],
            result.Diagnostics
                .Where(static d => d.Code == "FS2107")
                .Select(static d => d.Message.Split('\'')[1])
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["HE1__3WV", "N1", "N2", "N3", "PU1__in", "PU1__out"],
            result.Model.Components
                .Where(static component => component.Origin is Origin.Inferred)
                .Select(static component => component.Name)
                .Order(StringComparer.Ordinal));
    }

    // ---- step 6: ports exist only where the source evidenced them --------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnIndexedPortExistsOnlyWhereSomethingNamedIt()
    {
        // A tank has sixteen possible inlets. It gets the ones the script wrote and no others, which
        // is what keeps the model contract's port list a description of this script.
        var model = Model(
            "fluidscript 1\nT1 tank v=300 in3_elevation=0.8\nconnections\nT1.in3 - N1\nT1.out1 - N2\n");

        var tank = model.Components.Single(static component => component.Name == "T1");

        Assert.Equal(["in1", "out1", "in3"], tank.Ports);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortOutsideItsFamilysRangeIsReported()
    {
        var result = Bind("fluidscript 1\nT1 tank v=300\nconnections\nT1.in17 - N1\n");

        Assert.Contains("FS1516", Codes(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortTheKindDoesNotHaveIsReported()
    {
        var result = Bind("fluidscript 1\nPU1 pump\nconnections\nPU1.middle - N1\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1505");
        Assert.Contains("in, out", diagnostic.Message, StringComparison.Ordinal);
    }

    // ---- step 7: connections ----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnqualifiedEndpointTakesAnOutletOnTheLeftAndAnInletOnTheRight()
    {
        // What makes the reference circuits work with no port names at all. Reversing it would make
        // `N1 - PU1 - N2` push flow backwards through the pump on a script that reads correctly.
        var model = Model("fluidscript 1\nPU1 pump\nconnections\nN1 - PU1 - N2\n");

        Assert.Equal(["N1-PU1.in", "PU1.out-N2"], model.Connections.Select(Link));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AChainBecomesOneConnectionPerDash()
    {
        // Rule I6: one line, three endpoints, two connections — and both carry the line's span, so a
        // diagnostic about either points at something the user can see.
        var model = Model("fluidscript 1\nconnections\nN1 - N2 - N3\n");

        Assert.Equal(["N1-N2", "N2-N3"], model.Connections.Select(Link));
        Assert.Single(model.Connections.Select(static connection => connection.SourceSpan).Distinct());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortConnectedTwiceIsReportedOnceWithTheEarlierLine()
    {
        var result = Bind("fluidscript 1\nPU1 pump\nconnections\nPU1.out - N1\nPU1.out - N2\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1506");
        Assert.Contains("line 4", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnEndpointNamingALetBindingIsNotInferredIntoANode()
    {
        // The one case I1 cannot absorb. Inferring here would put a value and a component under one
        // identifier, and nothing could then say what `x.t` meant.
        var result = Bind("fluidscript 1\nlet x = 30 kW\nconnections\nx - N1\n");

        Assert.Contains("FS1504", Codes(result));
        Assert.DoesNotContain(result.Model.Components, static component => component.Name == "x");
    }

    // ---- step 8: the three inference rules --------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUndeclaredEndpointBecomesANodeKeepingItsName()
    {
        var model = Model("fluidscript 1\nconnections\nN1 - N2\n");

        Assert.Equal(["N1", "N2"], model.Components.Select(static component => component.Name));
        Assert.All(model.Components, static component =>
        {
            Assert.Equal("I1", Assert.IsType<Origin.Inferred>(component.Origin).Rule);
            Assert.Null(component.DeclarationSpan);
            Assert.Null(component.Tag);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoComponentsJoinedDirectlyGetANodeBetweenThem()
    {
        // I2. Without the node there is no state between them to write an equation about.
        var model = Model("fluidscript 1\nHE1 heat_exchanger power=30\nPU1 pump\nconnections\nHE1 - PU1\n");

        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1");
        Assert.Contains(model.Connections, static connection => Link(connection) == "HE1.out-HE1__PU1");
        Assert.Contains(model.Connections, static connection => Link(connection) == "HE1__PU1-PU1.in");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ThatSamePairConnectedTwiceGetsAnOrdinal()
    {
        var model = Model(
            "fluidscript 1\nHE1 heat_exchanger power=30\nPU1 pump\n"
            + "connections\nHE1.out - PU1.in\nHE1.out2 - PU1.out\n");

        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1");
        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1_2");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnOptionalPortIsNotTerminated()
    {
        // I3 fires on the ports a component must have, not on the ones it may have. A heat exchanger
        // with no secondary side is the common case, and terminating `in2` would invent a second
        // circuit nobody wrote.
        var model = Model("fluidscript 1\nHE1 heat_exchanger power=30\nconnections\nN1 - HE1 - N2\n");

        Assert.DoesNotContain(model.Components, static component => component.Name == "HE1__in2");
        Assert.DoesNotContain(model.Components, static component => component.Name == "HE1__out2");
    }

    // ---- step 9: attachments, control bindings, the schedule --------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ASubcircuitsAttachmentsResolveIntoItsParent()
    {
        // M1: `supply` and `return` bind. The lookup is unqualified because identifiers are unique
        // across the model (`D-41`) — which is the whole reason an attachment can be written this way.
        var model = Model(
            "fluidscript 1\ncircuit primary 100\nNB1 node t=6 p=300\nNB2 node p=280\n"
            + "connections\nNB1 - NB2\n\ncircuit ahu 300\nsupply NB1\nreturn NB2\n");

        var subcircuit = model.Circuits.Single(static circuit => circuit.Name == "ahu");

        Assert.Equal("NB1", subcircuit.Supply!.ParentComponentName);
        Assert.Equal("NB2", subcircuit.Return!.ParentComponentName);
        Assert.Equal("primary", subcircuit.ParentCircuit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OneAttachmentWithoutTheOtherIsReported()
    {
        var result = Bind(
            "fluidscript 1\ncircuit primary 100\nNB1 node t=6 p=300\n\ncircuit ahu 300\nsupply NB1\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1520");
        Assert.Contains("'supply NB1'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("no 'return'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnAttachmentNamingNothingIsReported()
    {
        var result = Bind("fluidscript 1\ncircuit ahu 300\nsupply NB9\nreturn NB8\n");

        Assert.Equal(2, Codes(result).Count(static code => code == "FS1518"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControlLineBindsItsFourNamedArguments()
    {
        // M1, and `D-40`: every field comes from a named argument, so transposing two is an error
        // rather than a silent reversal that drives the valve the wrong way.
        var model = Model(
            "fluidscript 1\nTV1 three_way_valve\nPID1 pid kp=3\nN2 node t=6 p=300\n"
            + "connections\nN2 - TV1\n"
            + "control actuate=TV1.position measure=N2.t by=PID1 setpoint=20\n");

        var binding = Assert.Single(model.ControlBindings);

        Assert.Equal("PID1", binding.Controller.Name);
        Assert.Equal(new PropertyReference("TV1", "position"), binding.Actuator);
        Assert.Equal(new PropertyReference("N2", "t"), binding.Measurement);
        Assert.Equal(293.15, binding.Setpoint!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareComponentNameIsNotAnActuator()
    {
        // `D-43`. There is deliberately no per-kind default: a valve has more than one thing that
        // could move, so guessing one would drive the wrong thing on a script that looks right.
        var result = Bind(
            "fluidscript 1\nTV1 three_way_valve\nPID1 pid kp=3\nN2 node t=6 p=300\n"
            + "control actuate=TV1 measure=N2.t by=PID1 setpoint=20\n");

        Assert.Contains("FS1515", Codes(result));
        Assert.Empty(result.Model.ControlBindings);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControlLineMissingAnArgumentNamesTheOneItLacks()
    {
        var result = Bind(
            "fluidscript 1\nTV1 three_way_valve\nPID1 pid kp=3\n"
            + "control actuate=TV1.position by=PID1 setpoint=20\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1521");
        Assert.Contains("Missing: measure.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerThatIsNotOneIsReported()
    {
        var result = Bind(
            "fluidscript 1\nTV1 three_way_valve\nPU1 pump\nN2 node t=6 p=300\n"
            + "control actuate=TV1.position measure=N2.t by=PU1 setpoint=20\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1523");
        Assert.Contains("is a pump", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheScheduleBindsBothItsForms()
    {
        // The step `15`'s binding order never had: the parser produced a disturbance and nothing
        // consumed it. A step and a ramp, with the bare values reinterpreted in the parameter's
        // canonical unit — `HE4.power = 45` is 45 kW, exactly as `power=45` would be (`D-14`).
        var model = Model(
            "fluidscript 1\ncircuit demandStep 400\nfluid dynamic water\n"
            + "HE4 heat_exchanger in=50 out=30 power=30 kW\n"
            + "schedule\nat 60 s HE4.power = 45\nover 60 s .. 120 s HE4.power = 30 .. 45\n");

        Assert.Equal(2, model.Disturbances.Length);

        var step = model.Disturbances[0];
        Assert.Equal(new PropertyReference("HE4", "power"), step.Target);
        Assert.Equal(60, step.From!.Value.SiValue, 6);
        Assert.Equal(60, step.To!.Value.SiValue, 6);
        Assert.Equal(45000, step.ToValue!.Value.SiValue, 6);

        var ramp = model.Disturbances[1];
        Assert.Equal(120, ramp.To!.Value.SiValue, 6);
        Assert.Equal(30000, ramp.FromValue!.Value.SiValue, 6);
        Assert.Equal(45000, ramp.ToValue!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AScheduledParameterTheKindDoesNotHaveIsReported()
    {
        var result = Bind(
            "fluidscript 1\ncircuit demandStep 400\nfluid dynamic water\nPU1 pump\n"
            + "schedule\nat 60 s PU1.colour = 3\n");

        Assert.Contains("FS1503", Codes(result));
        Assert.Empty(result.Model.Disturbances);
    }

    // ---- step 10: validation ----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AClusterAdriftFromTheRestIsReportedOnceAndNotAsUnconnected()
    {
        // FS1511 is about a cluster and FS1507 about a component on its own; the two partition the
        // same mistake and never both fire for one component.
        var result = Bind(
            "fluidscript 1\nconnections\nN1 - N2 - N3\nN8 - N9\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1511");
        Assert.Contains("'N8' and 1 others", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FS1507", Codes(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANodeWithABoundaryIsNotADeadEnd()
    {
        // A degree-1 node that states t, p or flow is a boundary condition, which is exactly the shape
        // FS2107 exists to ask for.
        var result = Bind("fluidscript 1\nN1 node t=6 p=300\nconnections\nN1 - N2\n");

        Assert.Equal(["N2"], result.Diagnostics
            .Where(static d => d.Code == "FS2107")
            .Select(static d => d.Message.Split('\'')[1]));
    }

    // ---- step 11: tags, last -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TagsNumberFromOnePerCircuitAndCodeInDeclarationOrder()
    {
        var model = Model(
            "fluidscript 1\ncircuit primary 100\nPU1 pump\nPU2 pump\nHE1 heat_exchanger power=30\n"
            + "\ncircuit secondary 200\nPU3 pump\n");

        Assert.Equal(
            ["100PU01", "100PU02", "100HE01", "200PU01"],
            model.Components
                .Where(static component => component.Tag is not null)
                .Select(static component => component.Tag));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnInferredComponentIsNeverTagged()
    {
        // `D-34`: a tag goes on an equipment schedule, and scaffolding the user did not write has no
        // business on one.
        var model = Model("fluidscript 1\nconnections\nN1 - N2\n");

        Assert.All(model.Components, static component => Assert.Null(component.Tag));
    }

    // ---- recovery, and the map ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void OneBadLineLeavesEveryOtherStatementBound()
    {
        // M1: recovery leaves a bound model. The malformed line contributes its own diagnostic and
        // nothing else — every statement around it binds exactly as it would alone.
        var result = Bind(
            "fluidscript 1\nHE1 heat_exchanger power=30\n?????\nPU1 pump\nconnections\nN1 - HE1 - PU1 - N1\n");

        Assert.Contains(result.Model.Components, static component => component.Name == "HE1");
        Assert.Contains(result.Model.Components, static component => component.Name == "PU1");
        Assert.Equal(4, result.Model.Connections.Length);
        Assert.DoesNotContain(result.Diagnostics, static d =>
            d.Severity == DiagnosticSeverity.Error && d.Code != "FS1104");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APositionInsideADeclarationResolvesToItsSymbol()
    {
        // Invariant 6, and what hover and canvas write-back rest on.
        const string Text = "fluidscript 1\nHE1 heat_exchanger power=30\nconnections\nN1 - HE1\n";
        var model = Model(Text);

        var reference = model.SymbolMap.AtOffset(Text.IndexOf("heat_exchanger", StringComparison.Ordinal));

        Assert.Equal("HE1", Assert.IsType<SymbolReference.Component>(reference).Value.Name);

        // And the declaration plus the endpoint that names it, so go-to-definition works from a use.
        Assert.Equal(2, model.SymbolMap.References(reference).Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EverySampleAndFencedBlockBindsWithoutThrowing()
    {
        // The same corpus the parser is held to. Binding a malformed script is a return value, never
        // an exception — invariant 1, and the reason P4 is credible.
        foreach (var script in ScriptCorpus.All())
        {
            var result = Bind(script.Text, script.Name);

            Assert.NotNull(result.Model.SymbolMap);
            Assert.All(result.Model.Connections, connection =>
            {
                Assert.Contains(
                    result.Model.Components,
                    component => component.Name == connection.From.Component);
                Assert.Contains(
                    result.Model.Components,
                    component => component.Name == connection.To.Component);
            });
        }
    }
}
