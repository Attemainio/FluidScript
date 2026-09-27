using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Registry;

/// <summary>
/// The setting table is what the reader and the run binder accept, and what the component registry agrees with
/// (<c>L-86</c>): a setting the table lists and the reader refuses would be offered by the editor and then
/// underlined, and one the reader takes and the table omits would never be offered.
/// </summary>
public sealed class SettingRegistryTests
{
    private const string Plant = """
        fluidscript 2

        project "P":
        {project}

        circuit "C":
          fluid = water
        {circuit}
          V1  valve
          TE1  temperature_sensor
          TC1  controller:
            moves = V1
            reads = TE1
        {controller}
          N1 - V1 - TE1 - N2
        """;

    /// <summary>A value each setting takes, written as its own line (a block setting writes its block).</summary>
    private static readonly Dictionary<string, string> Written = new(StringComparer.Ordinal)
    {
        ["cases"] = "cases = [winter, mild]",
        ["catalog"] = "catalog = steel_en10255@2026.1",
        ["show"] = "show = temperature",
        ["scale"] = "scale = 20..90 C",
        ["spacing"] = "spacing = 1.2",
        ["style"] = "style:\n    colour = crimson",
        ["fluid"] = "fluid = water",
        ["number"] = "number = 200",
        ["role"] = "role = heating",
        ["colour"] = "colour = crimson",
        ["width"] = "width = 2",
        ["corner"] = "corner = fillet",
        ["line"] = "line = dashed",
        ["type"] = "type = PI",
        ["moves"] = "moves = V1",
        ["reads"] = "reads = TE1",
        ["setpoint"] = "setpoint = 50 C",
        ["band"] = "band = 10 K",
        ["kp"] = "kp = 2",
        ["ti"] = "ti = 120 s",
        ["td"] = "td = 10 s",
        ["output"] = "output = 10..100 %",
        ["action"] = "action = reverse",
        ["differential"] = "differential = 2 K",
        ["curve"] = "curve = heating",
    };

    public static TheoryData<string, string> Settings()
    {
        var data = new TheoryData<string, string>();
        foreach (var (block, settings) in SettingRegistry.Blocks.Where(static block => block.Block != "run"))
        {
            foreach (var setting in settings)
            {
                data.Add(block, setting.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("Category", "Unit")]
    public void TheReaderTakesEverySettingTheTableLists(string block, string name)
    {
        var line = Written[name];
        var text = Plant
            .Replace("{project}", block == "project" ? "  " + line : block == "style" ? "  style:\n    " + line : "  show = temperature", StringComparison.Ordinal)
            .Replace("{circuit}", block == "circuit" ? "  " + line : string.Empty, StringComparison.Ordinal)
            .Replace("{controller}", block == "controller" && name is not ("moves" or "reads") ? "    " + line : string.Empty, StringComparison.Ordinal);

        var source = new SourceText(text);
        var parse = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, "script");

        var refused = parse.Diagnostics.Concat(bound.Diagnostics)
            .Where(d => d.Code == "FS1503" && d.Span is { } span && source.ToString(span) == name)
            .ToArray();
        Assert.True(refused.Length == 0, $"The {block} block refuses '{name}': {string.Join("; ", refused.Select(static d => d.Message))}");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("circuit")]
    [InlineData("style")]
    [InlineData("controller")]
    [Trait("Category", "Unit")]
    public void TheCheckAboveSeesARefusal(string block)
    {
        // The theory above passes when nothing is refused; this is what says it would notice a refusal.
        var text = Plant
            .Replace("{project}", block == "project" ? "  bogus = 1" : block == "style" ? "  style:\n    bogus = 1" : "  show = temperature", StringComparison.Ordinal)
            .Replace("{circuit}", block == "circuit" ? "  bogus = 1" : string.Empty, StringComparison.Ordinal)
            .Replace("{controller}", block == "controller" ? "    bogus = 1" : string.Empty, StringComparison.Ordinal);

        var source = new SourceText(text);
        var parse = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);
        var bound = new Binder(ComponentRegistry.Default).Bind(parse, "script");

        var refused = Assert.Single(bound.Diagnostics, d => d.Code == "FS1503" && d.Span is { } span && source.ToString(span) == "bogus");
        Assert.Contains(SettingRegistry.Listed(SettingRegistry.Blocks.Single(b => b.Block == block).Settings), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheRunBinderReadsEveryRunSettingTheTableLists()
    {
        // A run takes any other `name =` as an override, so a setting it did not know would bind as an override of
        // nothing rather than be refused; each is checked by the field it sets instead.
        var text = """
            fluidscript 2

            project:
              cases = [winter, mild]

            circuit "C":
              fluid = water
              P1  pump
              N1 - P1 - N1

            run "R":
              from = mild
              start = 2026-01-15 06:00
              duration = 2 h
              frame = 10 s
              steady = ["C"]
            """;

        var source = new SourceText(text);
        var parse = MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);
        var run = Assert.Single(new Binder(ComponentRegistry.Default).Bind(parse, "script").Model.Runs);

        Assert.Equal(["from", "start", "duration", "frame", "steady"], SettingRegistry.Run.Select(static setting => setting.Name));
        Assert.Equal(1, run.From);
        Assert.NotNull(run.Start);
        Assert.Equal(7200, run.Duration, 6);
        Assert.Equal(10, run.Frame, 6);
        Assert.Equal(["C"], run.Steady);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllersWordsAreTheRegistrys()
    {
        // `type` and `action` are also the controller kind's registry parameters, whose accepted symbols the binder
        // checks; the table's words for them are those symbols, in order.
        var controller = ComponentRegistry.Default.ByKeyword("controller")!;

        foreach (var name in (string[])["type", "action"])
        {
            Assert.Equal(
                controller.Parameters[name].AcceptedSymbols,
                SettingRegistry.Find(SettingRegistry.Controller, name)!.Values);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AControllerTypeTakesTheSettingsOfItsRow()
    {
        // `19`'s table: every type takes the common settings; a curve controller takes no setpoint.
        Assert.Equal(
            ["type", "moves", "reads", "setpoint", "band", "kp", "ti", "output", "action"],
            SettingRegistry.ControllerSettingsOf("pi").Select(static setting => setting.Name));
        Assert.Equal(
            ["type", "moves", "reads", "output", "action", "curve"],
            SettingRegistry.ControllerSettingsOf("curve").Select(static setting => setting.Name));
        Assert.Empty(SettingRegistry.ControllerSettingsOf("fuzzy"));
    }
}
