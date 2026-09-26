using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// Binding steps 6 through 11 from <c>plan/10-language/15-semantic-model.md</c>, plus the schedule
/// step its order never had. The last four M1 exit criteria from <c>05</c> are here, named where they
/// are.
/// </summary>
public sealed class TopologyBindingTests
{
    private static BindResult Bind(string text, string documentName = "script") =>
        new Binder(ComponentRegistry.Default).Bind(
            ScriptParse.Parse(new SourceText(text)), documentName);

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
            "fluidscript 2\n\ncircuit \"script\":\n  T1  tank  v = 300  in[3].level = 0.8\n  T1.in[3] - N1\n  T1.out - N2\n");

        var tank = model.Components.Single(static component => component.Name == "T1");

        Assert.Equal(["in1", "out1", "in3"], tank.Ports);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortOutsideItsFamilysRangeIsReported()
    {
        var result = Bind("fluidscript 2\n\ncircuit \"script\":\n  T1  tank  v = 300\n  T1.in[17] - N1\n");

        Assert.Contains("FS1516", Codes(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortTheKindDoesNotHaveIsReported()
    {
        var result = Bind("fluidscript 2\n\ncircuit \"script\":\n  PU1  pump\n  PU1.middle - N1\n");

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
        var model = Model("fluidscript 2\n\ncircuit \"script\":\n  PU1  pump\n  N1 - PU1 - N2\n");

        Assert.Equal(["N1-PU1.in", "PU1.out-N2"], model.Connections.Select(Link));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AChainBecomesOneConnectionPerDash()
    {
        // Rule I6: one line, three endpoints, two connections — and each carries its own link's span, so a
        // diagnostic about either points at the part of the line it is about.
        const string source = "fluidscript 2\n\ncircuit \"script\":\n  N1 - N2 - N3\n";
        var model = Model(source);

        Assert.Equal(["N1-N2", "N2-N3"], model.Connections.Select(Link));
        Assert.Equal(
            ["N1 - N2", "N2 - N3"],
            model.Connections.Select(static connection => source.Substring(connection.SourceSpan.Start, connection.SourceSpan.Length)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortConnectedTwiceIsReportedOnceWithTheEarlierLine()
    {
        var result = Bind("fluidscript 2\n\ncircuit \"script\":\n  PU1  pump\n  PU1.out - N1\n  PU1.out - N2\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1506");
        Assert.Contains("line 5", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnEndpointNamingALetBindingIsNotInferredIntoANode()
    {
        // The one case I1 cannot absorb. Inferring here would put a value and a component under one
        // identifier, and nothing could then say what `x.t` meant.
        var result = Bind("fluidscript 2\n\nlet x = 30 kW\n\ncircuit \"script\":\n  x - N1\n");

        Assert.Contains("FS1504", Codes(result));
        Assert.DoesNotContain(result.Model.Components, static component => component.Name == "x");
    }

    // ---- step 8: the three inference rules --------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUndeclaredEndpointBecomesANodeKeepingItsName()
    {
        var model = Model("fluidscript 2\n\ncircuit \"script\":\n  N1 - N2\n");

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
        var model = Model("fluidscript 2\n\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30\n  PU1  pump\n  HE1 - PU1\n");

        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1");
        Assert.Contains(model.Connections, static connection => Link(connection) == "HE1.out-HE1__PU1");
        Assert.Contains(model.Connections, static connection => Link(connection) == "HE1__PU1-PU1.in");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ThatSamePairConnectedTwiceGetsAnOrdinal()
    {
        // A closed loop rather than an exchanger with one secondary port open: since P4.1 that is FS2112,
        // an error, and this test is about ordinals.
        var model = Model(
            "fluidscript 2\n\ncircuit \"c\":\n  HE1  heat_exchanger  power = 30\n  PU1  pump\n"
            + "  HE1.out - PU1.in\n  HE1.in - PU1.out\n");

        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1");
        Assert.Contains(model.Components, static component => component.Name == "HE1__PU1_2");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortI3TerminatedOnAWiredComponentIsReported()
    {
        // The other half of the exemption below. A pump wired on one side only is a stub the user has
        // not finished, and terminating `out` at zero flow keeps the graph solvable -- so the warning
        // is the only thing separating it from a dead end somebody meant.
        var result = Bind("fluidscript 2\n\ncircuit \"script\":\n  PU1  pump\n  N1  node\n  N1 - PU1.in\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS2202");
        Assert.Contains("'PU1'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'out'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnOptionalPortIsNotTerminated()
    {
        // I3 fires on the ports a component must have, not on the ones it may have. A heat exchanger
        // with no secondary side is the common case, and terminating `in2` would invent a second
        // circuit nobody wrote.
        var model = Model("fluidscript 2\n\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30\n  N1 - HE1 - N2\n");

        Assert.DoesNotContain(model.Components, static component => component.Name == "HE1__in2");
        Assert.DoesNotContain(model.Components, static component => component.Name == "HE1__out2");
    }

    // ---- step 9: attachments, control bindings, the schedule --------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AControlLineBindsItsFourNamedArguments()
    {
        // M1, and `D-40`: every field comes from a named setting, so transposing two is an error
        // rather than a silent reversal that drives the valve the wrong way.
        var model = Model(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N2  node  t = 6  p = 300\n  N2 - TV1.ab\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = N2.t\n    setpoint = 20\n");

        var binding = Assert.Single(model.ControlBindings);

        Assert.Equal("PID1", binding.Controller.Name);
        Assert.Equal(new PropertyReference("TV1", "position"), binding.Actuator);
        Assert.Equal(new PropertyReference("N2", "t"), binding.Measurement);
        Assert.Equal(293.15, binding.Setpoint!.Value.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnEventOnAComponentRatherThanAPropertyIsReported()
    {
        // `D-43`: a run changes a property. `PU1 = 3` does not say which of the pump's.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  fluid = water\n  PU1  pump\n"
            + "\nrun \"Transient\":\n  at 60 s  PU1 = 3\n");

        Assert.Contains("FS1515", Codes(result));
        Assert.Empty(Assert.Single(result.Model.Runs).Events);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControlLineMissingAnArgumentNamesTheOneItLacks()
    {
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    setpoint = 20\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1521");
        Assert.Contains("reads", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerMovingAComponentThatDoesNotExistIsReported()
    {
        // Bound as written until P6.11 package 7, when a short-form line was checked for nothing: `TV9.position` was a
        // loop that moved nothing and said so nowhere.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N2  node  t = 6  p = 300\n  N2 - TV1.ab\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV9.position\n    reads = N2.t\n    setpoint = 20\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1404");
        Assert.Contains("'TV9'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(result.Model.ControlBindings);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnActuatorNamingAParameterTheKindLacksIsReported()
    {
        // The companion to `ABareComponentNameIsNotAnActuator`: the reference has a property, and the
        // kind does not have it. `D-61` makes `.position` optional on the one actuated parameter a kind
        // has, which is exactly why a *wrong* property has to be rejected rather than assumed.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N2  node  t = 6  p = 300\n  N2 - TV1.ab\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.altitude\n    reads = N2.t\n    setpoint = 20\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1522");
        Assert.Contains("'altitude'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'TV1'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("  TE1  t_sensor  at N2\n", "'TE1' reads 'N2', where 3 pipes meet")]
    [InlineData("  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = N2.t\n    setpoint = 20\n", "'PID1' reads 'N2', where 3 pipes meet")]
    public void AReadingAtAJunctionIsFs1548(string reader, string expected)
    {
        // `D-150`. Three streams meet at N2 and the node's one state is their mix, which no instrument
        // on any of the three pipes reads; the script has to say which pipe it means. A sensor's `at`
        // and a controller's `measure=` read the same number, so they are refused alike.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N1  inlet  t = 6  p = 300\n  N3  outlet  p = 280\n"
            + "  N1 - N2\n  N2 - TV1.a\n  N2 - N3\n"
            + reader);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1548");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expected, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AReadingOnOnePipeOrAtAnEndIsNotAJunction()
    {
        // Two connections is a point on one pipe, one is a terminal: each has a single stream.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N1  inlet  t = 6  p = 300\n  N3  outlet  p = 280\n"
            + "  TE1  t_sensor  at N1\n"
            + "  N1 - NS\n  NS - TV1.a\n  TV1.ab - N3\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = NS.t\n    setpoint = 20\n");

        Assert.DoesNotContain("FS1548", Codes(result));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("t", "NS__TE", "t_sensor")]
    [InlineData("p", "NS__PE", "p_sensor")]
    [InlineData("flow", "NS__FE", "flow_sensor")]
    public void AControlLineReadingANodeReadsItThroughASensorTheBinderPutsThere(string property, string name, string kind)
    {
        // I8, D-151: a sensor is a physical component and is always drawn, so a controller that reads a node
        // directly reads it through one the binder places on the node, named after it, and says so.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N1  inlet  t = 6  p = 300\n  N3  outlet  p = 280\n"
            + "  N1 - NS\n  NS - TV1.a\n  TV1.ab - N3\n"
            + $"  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = NS.{property}\n    setpoint = 20\n");

        var sensor = Assert.Single(result.Model.Components, c => c.Name == name);
        Assert.Equal(kind, sensor.Kind?.Keyword);
        Assert.Equal("NS", sensor.AttachedTo);
        Assert.Equal(new Origin.Inferred("I8", name), sensor.Origin);
        Assert.Null(sensor.Tag);
        Assert.Contains(result.Diagnostics, d => d.Code == "FS1510" && d.Message.Contains($"'{name}' (I8)", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControlLineReadingANodeWithASensorOnItReadsThroughThatOne()
    {
        // The sensor the script placed is the one read; nothing is added beside it.
        var model = Model(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  TE1  t_sensor  at NS\n  N1  inlet  t = 6  p = 300\n  N3  outlet  p = 280\n"
            + "  N1 - NS\n  NS - TV1.a\n  TV1.ab - N3\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = NS.t\n    setpoint = 20\n");

        Assert.Single(model.Components, static c => c.AttachedTo == "NS");
        Assert.DoesNotContain(model.Components, static c => c.Name == "NS__TE");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AJunctionIsRefusedAndGetsNoSensor()
    {
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  TV1  three_way_valve\n  N1  inlet  t = 6  p = 300\n  N3  outlet  p = 280\n"
            + "  N1 - N2\n  N2 - TV1.a\n  N2 - N3\n"
            + "  PID1  controller:\n    kp = 3\n    moves = TV1.position\n    reads = N2.t\n    setpoint = 20\n");

        Assert.Contains("FS1548", Codes(result));
        Assert.DoesNotContain(result.Model.Components, static c => c.Name == "N2__TE");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheScheduleBindsBothItsForms()
    {
        // The step `15`'s binding order never had: the parser produced a disturbance and nothing
        // consumed it. A step and a ramp, with the bare values reinterpreted in the parameter's
        // canonical unit — `HE4.power = 45` is 45 kW, exactly as `power = 45` would be (`D-14`). The run's
        // events are the schedule of the model it projects (`D-169`).
        var model = GraphFixture.BindRun(
            "fluidscript 2\n\ncircuit \"demandStep\":\n  fluid = water\n  number = 400\n"
            + "  HE4  load  in.t = 50  out.t = 30  power = 30 kW\n"
            + "\nrun \"Transient\":\n  at 60 s  HE4.power = 45\n  over 60 s..120 s  HE4.power = 30..45\n");

        Assert.Equal(2, model.Disturbances.Length);

        var step = model.Disturbances[0];
        Assert.Equal(new PropertyReference("HE4", "power"), step.Target);
        Assert.Equal(60, step.From!.Value.SiValue, 6);
        Assert.Equal(60, step.To!.Value.SiValue, 6);
        Assert.Equal(45000, step.ToValue!.Value.SiValue, 6);
        Assert.Null(step.FromValue);

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
            "fluidscript 2\n\ncircuit \"demandStep\":\n  fluid = water\n  PU1  pump\n"
            + "\nrun \"Transient\":\n  at 60 s  PU1.colour = 3\n");

        Assert.Contains("FS1503", Codes(result));
        Assert.Empty(Assert.Single(result.Model.Runs).Events);
    }

    // ---- step 10: validation ----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AClusterAdriftFromTheRestIsReportedOnceAndNotAsUnconnected()
    {
        // FS1511 is about a cluster and FS1507 about a component on its own; the two partition the
        // same mistake and never both fire for one component.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"script\":\n  N1 - N2 - N3\n  N8 - N9\n");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1511");
        Assert.Contains("'N8' and 1 others", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FS1507", Codes(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANodeWithABoundaryIsNotADeadEnd()
    {
        // Since D-115 the kind says mass crosses: a degree-1 `inlet` is the shape FS2107 exists to ask
        // for, and a degree-1 `node` that states t and p is a datum on a stub -- still a dead end, and
        // the message says which word makes it a boundary (C-106).
        var result = Bind("fluidscript 2\n\ncircuit \"script\":\n  N1  inlet  t = 6  p = 300\n  N1 - N2\n");

        Assert.Equal(["N2"], result.Diagnostics
            .Where(static d => d.Code == "FS2107")
            .Select(static d => d.Message.Split('\'')[1]));

        var datum = Bind("fluidscript 2\n\ncircuit \"script\":\n  N1  node  t = 6  p = 300\n  N1 - N2\n");

        Assert.Equal(["N1", "N2"], datum.Diagnostics
            .Where(static d => d.Code == "FS2107")
            .Select(static d => d.Message.Split('\'')[1])
            .Order(StringComparer.Ordinal));
        Assert.Contains("'inlet' or 'outlet'", datum.Diagnostics.First(static d => d.Code == "FS2107").Message, StringComparison.Ordinal);
    }

    // ---- step 11: tags, last -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TagsNumberFromOnePerCircuitAndCodeInDeclarationOrder()
    {
        var model = Model(
            "fluidscript 2\n\ncircuit \"primary\":\n  number = 100\n  PU1  pump\n  PU2  pump\n  HE1  heat_exchanger  power = 30\n"
            + "\ncircuit \"secondary\":\n  number = 200\n  PU3  pump\n");

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
        var model = Model("fluidscript 2\n\ncircuit \"script\":\n  N1 - N2\n");

        Assert.All(model.Components, static component => Assert.Null(component.Tag));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheDistributionHeaderTagsEveryDeviceOnceAndRestartsPerCircuit()
    {
        // M2a: every device on the header carries a tag, the ordinal restarts in each circuit and the
        // circuit number the script states is the prefix. Read from the sample itself so the list cannot
        // drift away from the one the documentation shows.
        var model = Model(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid")));

        Assert.Equal(
            ["100HE01", "101HE01", "101TV01", "101PU01", "102HE01", "102TV01", "102PU01"],
            model.Components
                .Where(static component => component.Tag is not null)
                .Select(static component => component.Tag));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InsertingAPumpRenumbersTheTagsAndMovesNoIdentifier()
    {
        // `D-34`: a tag is a schedule number, and a schedule renumbers when a row is inserted above. The
        // identifier the script, the canvas and every diagnostic hold on to is the name, which is why
        // the anchor of a diagnostic on `HE1` reads `HE1` before and after -- never `100HE01`.
        const string before = "fluidscript 2\n\ncircuit \"primary\":\n  number = 100\n  role = distribution\n\n  PU1  pump\n  HE1  heat_exchanger  power = 30  flavour = 1\n";
        const string after = "fluidscript 2\n\ncircuit \"primary\":\n  number = 100\n  role = distribution\n\n  PU2  pump\n  PU1  pump\n  HE1  heat_exchanger  power = 30  flavour = 1\n";

        var first = Bind(before);
        var second = Bind(after);

        Assert.Equal(["100PU01", "100HE01"], Tags(first.Model));
        Assert.Equal(["100PU01", "100PU02", "100HE01"], Tags(second.Model));

        // The declared identifiers are the same set, and `PU1` is still `PU1` on both sides of its new tag.
        // (The unconnected ports each grow an I3 node, which is inferred and so carries no tag.)
        Assert.Equal(["PU1", "HE1"], Declared(first.Model));
        Assert.Equal(["PU2", "PU1", "HE1"], Declared(second.Model));
        Assert.Equal("100PU01", first.Model.Components.Single(static c => c.Name == "PU1").Tag);
        Assert.Equal("100PU02", second.Model.Components.Single(static c => c.Name == "PU1").Tag);

        // The diagnostic about the unknown parameter anchors on the same text in both -- the span still
        // covers the offending name on `HE1`'s line after it moved down one (L-53: the name, not
        // `name=value`) -- and no tag leaks into it.
        foreach (var (source, result) in ((string, BindResult)[])[(before, first), (after, second)])
        {
            var diagnostic = Assert.Single(result.Diagnostics, static d => d.Message.Contains("flavour", StringComparison.Ordinal));
            var span = Assert.NotNull(diagnostic.Span);

            Assert.Equal("flavour", source.Substring(span.Start, span.Length));
            Assert.Contains("HE1", source[..span.Start].Split('\n')[^1], StringComparison.Ordinal);
            Assert.DoesNotContain("100HE", diagnostic.Message, StringComparison.Ordinal);
        }

        static string[] Declared(SemanticModel model) =>
            [.. model.Components.Where(static c => c.Origin is Origin.Declared).Select(static c => c.Name)];

        static string[] Tags(SemanticModel model) =>
            [.. model.Components.Where(static c => c.Tag is not null).Select(static c => c.Tag!)];
    }

    // ---- recovery, and the map ---------------------------------------------------------------------
    // ---- recovery, and the map ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void OneBadLineLeavesEveryOtherStatementBound()
    {
        // M1: recovery leaves a bound model. The malformed line contributes its own diagnostic and
        // nothing else — every statement around it binds exactly as it would alone.
        var result = Bind(
            "fluidscript 2\n\ncircuit \"c\":\n  HE1  heat_exchanger  power = 30\n  ?????\n  PU1  pump\n  N1 - HE1 - PU1 - N1\n");

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
        const string Text = "fluidscript 2\n\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30\n  N1 - HE1\n";
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
