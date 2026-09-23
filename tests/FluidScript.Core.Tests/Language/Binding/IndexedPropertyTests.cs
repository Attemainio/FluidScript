using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// A tank's per-layer and per-port temperatures are readable, which <c>22</c> §6 lists among its
/// properties and nothing resolved until now (<c>C-8</c>).
/// </summary>
/// <remarks>
/// The gap was structural rather than an omission of three rows: the registry had
/// <see cref="ComponentKindInfo.IndexedParameterFamilies"/> and no property equivalent, so
/// <c>t1</c>…<c>tN</c> could be <em>stated</em> and never read, and <c>in2_t</c> — which has no
/// parameter behind it at all — could not be named in any way.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class IndexedPropertyTests
{
    private static BindResult Bind(string body) =>
        new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + body + "\n")), "script");

    private static void NoErrors(string body)
    {
        var result = Bind(body);

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));
    }

    private static ComponentKindInfo Tank => ComponentRegistry.Default.ByKeyword("tank")!;

    [Theory]
    [InlineData("layer[1].t", "layer[1].t", "t1")]
    [InlineData("layer[3].t", "layer[3].t", "t3")]
    [InlineData("in.t", "in.t", "in1_t")]
    [InlineData("in[1].t", "in.t", "in1_t")]
    [InlineData("in[16].t", "in[16].t", "in16_t")]
    [InlineData("out[2].t", "out[2].t", "out2_t")]
    public void AFamilyMemberResolvesToItsOwnNameAndKey(string property, string name, string key)
    {
        // The name is the script's spelling with a port's `[1]` folded away -- a layer keeps its index,
        // since `layer[1]` is the bottom layer and not a first port -- and the key is the one the
        // solved value is published under (D-120).
        var resolved = Tank.ResolveProperty(property, out var suggestion);

        Assert.NotNull(resolved);
        Assert.Null(suggestion);
        Assert.Equal(name, resolved.Name);
        Assert.Equal(key, resolved.Key);
        Assert.Equal(Dimension.Temperature, resolved.Dimension);
        Assert.Equal(PropertyAvailability.Solved, resolved.Availability);
    }

    [Theory]
    [InlineData("t1", "layer[1].t")]
    [InlineData("t3", "layer[3].t")]
    [InlineData("in1_t", "in.t")]
    [InlineData("in16_t", "in[16].t")]
    [InlineData("out2_t", "out[2].t")]
    public void TheOldSpellingResolvesAndSuggestsTheNewOne(string legacy, string current)
    {
        var resolved = Tank.ResolveProperty(legacy, out var suggestion);

        Assert.NotNull(resolved);
        Assert.Equal(current, suggestion);
        Assert.Equal(current, resolved.Name);
        Assert.Equal(legacy, resolved.Key);
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("layers")]
    [InlineData("stored_energy")]
    public void AFixedPropertyStillResolves(string property) =>
        Assert.NotNull(Tank.ResolveProperty(property));

    [Theory]
    [InlineData("t")]           // The bulk parameter, not a property.
    [InlineData("layer[0].t")]  // Below the family's first index.
    [InlineData("in[17].t")]    // Above the sixteen ports a tank materializes.
    [InlineData("in17_t")]      // The same, in the old spelling.
    [InlineData("layer.t")]     // No index at all.
    [InlineData("layer[3]")]    // No quantity.
    [InlineData("t3x")]         // The digits must be the whole of the placeholder.
    public void AnythingElseResolvesToNothing(string property) =>
        Assert.Null(Tank.ResolveProperty(property));

    [Fact]
    public void AFamilyBoundedByAParameterHasNoFixedCeilingHere()
    {
        // `t{index}` is bounded by `layers`, which is per component, so the registry cannot check it.
        // T1.layer[9].t on a five-layer tank resolves and is a graph-time question -- stated deliberately,
        // because the alternative reading is that the ceiling was forgotten.
        Assert.NotNull(Tank.ResolveProperty("layer[9].t"));
        Assert.Null(Tank.IndexedPropertyFamilies.Single(
            static family => family.Pattern == "layer[{index}].t").MaxIndex);
    }

    [Fact]
    public void AReferenceToALayerTemperatureBindsWithoutError() =>
        NoErrors("T1 tank layers=3\nlet warm = T1.layer[3].t");

    [Fact]
    public void AReferenceToAPortTemperatureBindsWithoutError() =>
        NoErrors("T1 tank\nlet supply = T1.out.t");

    [Fact]
    public void FS1406_StillFiresForANameNoFamilyMatches()
    {
        var result = Bind("T1 tank\nlet x = T1.nonsense");
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Code == "FS1406");

        // The family patterns are in the list, not just the fixed names: a message that offered only
        // `volume, layers, stored_energy` would say a layer temperature is unreadable, which is the
        // opposite of what this package changed.
        Assert.Contains("layer[{index}].t", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("stored_energy", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFamilyPatternCarriesItsPlaceholderAndABound()
    {
        // ComponentRegistry.Verify throws on either failure as it builds, so this states out loud what
        // reaching Default already proves.
        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            foreach (var family in kind.IndexedPropertyFamilies)
            {
                Assert.Contains("{index}", family.Pattern, StringComparison.Ordinal);
                Assert.True(
                    family.MaxIndex is not null || family.MaxIndexParameter is not null,
                    $"'{kind.Keyword}.{family.Pattern}' has no upper bound of either kind.");

                if (family.MaxIndexParameter is { } bound)
                {
                    Assert.Contains(bound, kind.Parameters.Keys);
                }
            }
        }
    }

    [Fact]
    public void NoFixedPropertyIsShadowedByAFamilyPattern()
    {
        // ResolveProperty tries the fixed names first, so a family that also matched one would make
        // the fixed entry unreachable -- silently, since both resolve to something.
        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            foreach (var name in kind.Properties.Keys)
            {
                Assert.DoesNotContain(
                    kind.IndexedPropertyFamilies,
                    family => IndexedName.Matches(family.Pattern, name, out _));
            }
        }
    }

    [Theory]
    [InlineData("t{index}", "t3", true, 3)]
    [InlineData("t{index}", "t03", true, 3)]
    [InlineData("in{index}_t", "in12_t", true, 12)]
    [InlineData("in{index}_t", "in12_x", false, 0)]
    [InlineData("t{index}", "t", false, 0)]
    [InlineData("t{index}", "t+3", false, 0)]
    [InlineData("t{index}", "t 3", false, 0)]
    [InlineData("t{index}", "t-1", false, 0)]
    [InlineData("no placeholder", "anything", false, 0)]
    public void TheIndexMustBeDigitsAndTheWholeOfThePlaceholder(
        string pattern, string written, bool expected, int index)
    {
        // `NumberStyles.None` is what rejects the sign and the space; int.TryParse's default overload
        // accepts both, which would make `t-1` a layer.
        Assert.Equal(expected, IndexedName.Matches(pattern, written, out var actual));
        Assert.Equal(index, actual);
    }
}
