using FluidScript.Api.Contracts;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// A script's diagnostics as a client reads them, every stage included (<c>plan/10-language/19-fluidscript-2.md</c>
/// §Diagnostics): the wording from the parse, the binding and the contract, and the retired codes never reached.
/// </summary>
[Trait("Category", "Api")]
public sealed class ScriptDiagnosticsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Compile = "/api/v1/compile";

    private const string Loop = """
        circuit "loop":
          fluid = water
          PU1 pump
          N1 - PU1 - N1
        """;

    private async Task<IReadOnlyList<DiagnosticWire>> CompiledAsync(string script)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "diagnostics", script });
        var body = await response.ReadAsync<CompileResponse>();

        return (IReadOnlyList<DiagnosticWire>?)body.Model?.Diagnostics ?? body.Diagnostics ?? [];
    }

    private async Task<DiagnosticWire> OnlyAsync(string script, string code)
    {
        var diagnostics = await CompiledAsync(script);
        var matching = diagnostics.Where(d => d.Code == code).ToArray();
        Assert.True(
            matching.Length == 1,
            $"Expected exactly one {code}; got: {string.Join("; ", diagnostics.Select(static d => $"{d.Code} {d.Message}"))}");
        return matching[0];
    }

    // ---- the contract stage ------------------------------------------------------------------

    [Fact]
    public async Task TheCatalogueNoteNamesTheCatalogSetting()
    {
        var diagnostic = await OnlyAsync("fluidscript 2\n" + Loop, "FS2606");

        Assert.Equal(
            "Using catalogue 'steel_en10255'. Write 'catalog = steel_en10255' in the project block to pin it.",
            diagnostic.Message);
    }

    [Fact]
    public async Task ASecondShowIsReportedAndTheFirstKept()
    {
        var diagnostic = await OnlyAsync(
            "fluidscript 2\nproject \"p\":\n  show = temperature\n  show = pressure\n" + Loop, "FS1214");

        Assert.Equal("Only the first 'show' is used.", diagnostic.Message);
    }

    [Fact]
    public async Task ASettingRepeatedAcrossStyleBlocksIsReported()
    {
        var diagnostic = await OnlyAsync(
            "fluidscript 2\nproject \"p\":\n  style:\n    width = 2\n  style:\n    width = 3\n" + Loop, "FS1202");

        Assert.Equal("'3' overrides the earlier '2'.", diagnostic.Message);
    }

    [Fact]
    public async Task AnUnquotedColourIsReportedOnce()
    {
        var diagnostics = await CompiledAsync(
            "fluidscript 2\nproject \"p\":\n  style:\n    colour = #ff0000\n" + Loop);

        Assert.Single(diagnostics, static d => d.Code == "FS1203");
        Assert.DoesNotContain(diagnostics, static d => d.Code == "FS1104");
    }

    // ---- the fluid (`D-181`) -------------------------------------------------------------------

    [Fact]
    public async Task ACircuitThatStatesNoFluidIsToldItCarriesWater()
    {
        var diagnostic = await OnlyAsync("fluidscript 2\n" + Loop.Replace("  fluid = water\n", "", StringComparison.Ordinal), "FS2008");

        Assert.Equal("info", diagnostic.Severity);
        Assert.Equal("'loop' states no fluid, so it carries water. Write 'fluid = water' to say so.", diagnostic.Message);
    }

    [Fact]
    public async Task ACircuitThatStatesItsFluidIsNotTold() =>
        Assert.DoesNotContain(await CompiledAsync("fluidscript 2\n" + Loop), static d => d.Code == "FS2008");

    // ---- a reader of a curve -----------------------------------------------------------------

    /// <summary>
    /// A load that follows a curve, in a file with cases and in a file with a run. Package 3e held every reader of a
    /// curve for the run's clock, and the steady solve then evaluated it again in a scope with no curves: <c>FS1404 Nothing named 'demand'</c>, and the design solve without
    /// its load.
    /// </summary>
    public static TheoryData<string> CurveReaders =>
    [
        """
        fluidscript 2
        project "p":
          cases = [winter, mild]
        let outdoor = [-26, 5] C
        curve demand: outdoor
          -26   30
           18    0
        circuit "loop":
          fluid = water
          HE1  heat_exchanger  power = demand  in.t = 20 C  out.t = 50 C
          LOAD heat_exchanger  power = -30 kW  dp = 0
          CV1  valve
          PU1  pump
          N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5
          N5 - N1  25 m
        """,
        """
        fluidscript 2

        let tout = -26 °C

        curve demand: tout
          -26 30
          18 0

        circuit "demandStep":
          fluid = water

          HE1  heat_exchanger  power = demand  out.t = 50  volume = 0.5
          3WV  three_way_valve
          PU1  pump
          P1  pipe  length = 25
          PB  pipe  length = 8  dn = 20  nodes = 4
          TC1 controller:
            moves = 3WV.position
            reads = NS.t
            setpoint = 20
          N1 - N2
          N2 - NS
          NS - PU1
          PU1 - HE1
          HE1 - 3WV
          3WV - PB - N2
          3WV - P1
          P1 - N3
          N1  inlet  t = 6  p = 300
          N3  outlet  p = 280

        run "Transient":
        """,
    ];

    [Theory]
    [MemberData(nameof(CurveReaders))]
    public async Task ALoadThatFollowsACurveIsSolvedAtItsDesignValue(string script)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Compile, new { sessionId = "diagnostics", script });
        var body = await response.ReadAsync<CompileResponse>();

        var model = Assert.IsType<ModelContract>(body.Model);
        var said = string.Join("; ", model.Diagnostics.Select(static d => $"{d.Code} {d.Message}"));
        Assert.True(model.Solve?.Converged == true, said);
        Assert.DoesNotContain(model.Diagnostics, static d => d.Code is "FS1404" or "FS1412" or "FS1413");

        // The design value: the curve at -26 °C, which is the design case in both files.
        var exchanger = model.Components.Single(static c => c.Id == "HE1");
        Assert.Equal(30, exchanger.Parameters["power"].Value!.Value, 6);
    }

    // ---- the corpus ------------------------------------------------------------------------

    /// <summary>
    /// The codes no mistake may raise: the retired ones (<c>DiagnosticRegistry.RetiredCodes</c>), and those whose
    /// statement or spelling the language does not have.
    /// </summary>
    private static readonly HashSet<string> Unreachable =
    [
        "FS1101", "FS1102", "FS1103", "FS1106", "FS1107", "FS1109", "FS1110", "FS1111", "FS1112", "FS1113",
        "FS1118", "FS1120", "FS1204", "FS1205", "FS1508", "FS1517", "FS1518", "FS1520", "FS1523", "FS1526",
        "FS1527", "FS1543", "FS1547", "FS2217",
    ];

    /// <summary>Retired spellings, which a message never quotes (FS1806 names a retired statement's word by design).</summary>
    private static readonly string[] RetiredSpellings =
        ["scenarios", "'design ", "in[2]", "out[2]", " dK", "t=", "p=", "'control ", "connections"];

    /// <summary>Each script is a file written wrong in one of the ways the audit's battery found.</summary>
    public static TheoryData<string> Mistakes =>
    [
        "event at top level\nat 10 min PU1.head = 5 m\n" + Loop,
        "curve without driver\ncurve c\n  -26 85\n  18 65\n" + Loop,
        "list with no cases\n" + """
            circuit "loop":
              fluid = water
              S1 inlet   t = [85, 70] C   p = 300 kPa
              S2 outlet  p = 100 kPa
              PU1 pump
              S1 - PU1 - S2
            """,
        "list, cases and a duplicate case\n" + """
            project "p":
              cases = [a, a]
            circuit "loop":
              fluid = water
              S1 inlet   t = [85, 70, 60] C   p = 300 kPa
              S2 outlet  p = 100 kPa
              PU1 pump
              S1 - PU1 - S2
            run "r":
              from = summer
              start = tomorrow
            """,
        "two-sided exchanger\n" + """
            circuit "loop":
              fluid = water
              S1 inlet   t = 85 C   p = 300 kPa
              S2 outlet  p = 100 kPa
              HX1  heat_exchanger  power = 150 kW  u = 3300:
                primary.in.t    = 40 C
                primary.out.t   = 60 C
                secondary.in.t  = 85 C
                secondary.out.t = 45 C
              HX2  heat_exchanger  power = 50 kW  ua = 5000
              PU1 pump
              S1 - PU1 - HX1 - HX2 - S2
              N9 - HX1.secondary.in
            """,
        "power sign on both sides\n" + """
            circuit "loop":
              fluid = water
              HX1  heat_exchanger  power = 150 kW:
                primary.in.t    = 60 C
                primary.out.t   = 40 C
                secondary.in.t  = 30 C
                secondary.out.t = 50 C
              PU1  pump
              PU2  pump
              N1 - PU1 - HX1 - N1
              M1 - PU2 - HX1.secondary.in
              HX1.secondary.out - M1
            """,
        "names and ports\n" + """
            circuit "loop":
              fluid = water
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = 50 C  haed = 5 m
              PU1  pump
              PU1  pump
              X1   pumpp
              N1 - PU1 - N2 - HE1 - N1
              N5 - HE1.in[3]
              N6 - PU1.in
            """,
        "controllers\n" + """
            circuit "loop":
              fluid = water
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = 50 C
              LOAD heat_exchanger  power = -30 kW  dp = 0
              PU1  pump
              TE1  t_sensor
              N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - N1
              TC1 controller:
                type = PI
                setpoint = 50 C
              TC2 controller:
                type = PI
                moves = HE1
                reads = N3
                setpoint = 50 C
            """,
        "clocked curve without a start\n" + """
            curve w: time
              0 h   -5
              24 h  -10
            circuit "loop":
              fluid = water
              HE1  heat_exchanger  power = 30 kW  in.t = 20 C  out.t = w
              PU1  pump
              N1 - PU1 - HE1 - N1
            run "r":
              duration = 1 h
            """,
        "units and lets\n" + """
            let a = 1
            let a = 2
            let c = b
            let d = 20 C + 30 C
            circuit "loop":
              fluid = water
              B1 boiler  power = 30 C
              PU1 pump
              N1 - PU1 - B1 - N1
            """,
        "presentation\n" + """
            project "p":
              spacing = 2 m
              show = colourfulness
              style:
                corner = round
                thickness = 3
            """ + "\n" + Loop,
        "retired statement words\n" + """
            circuit "loop":
              fluid = water
              PU1 pump
            connections
              N1 - PU1 - N1
            schedule
            """,
    ];

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task AMistakeIsToldInTheLanguagesOwnWords(string mistake)
    {
        var script = "fluidscript 2\n" + mistake[(mistake.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        var diagnostics = await CompiledAsync(script);

        Assert.Contains(diagnostics, static d => d.Severity is "error" or "warning");
        Assert.All(diagnostics, static d =>
        {
            Assert.DoesNotContain(d.Code, Unreachable);
            if (d.Code != "FS1806")
            {
                Assert.All(RetiredSpellings, spelling => Assert.DoesNotContain(spelling, d.Message, StringComparison.Ordinal));
            }
        });
    }
}
