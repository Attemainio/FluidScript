using FluidScript.Api.Contracts;
using FluidScript.Core.Model;

namespace FluidScript.Api.Tests.Contracts;

/// <summary>The wire itself: goldens, the round trip, and the size cap (<c>26</c>).</summary>
[Trait("Category", "Unit")]
public sealed class ModelContractJsonTests
{
    [Theory]
    [InlineData("m2-cooling-loop")]
    [InlineData("m2-substation")]
    [InlineData("m4-storage-header")]
    [InlineData("m2-distribution-header")]
    public void TheCompileOnlyPayloadMatchesItsGolden(string sample) =>
        Goldens.Assert(sample + ".compile", ModelContractJson.Build(PipelineFixture.Compile(PipelineFixture.Sample(sample + ".fluid"))));

    [Theory]
    [InlineData("m2-cooling-loop")]
    [InlineData("m2-substation")]
    [InlineData("m4-storage-header")]
    [InlineData("m2-distribution-header")]
    public async Task TheSolvedPayloadMatchesItsGolden(string sample) =>
        Goldens.Assert(sample + ".solved", ModelContractJson.Build(await PipelineFixture.SolveAsync(PipelineFixture.Sample(sample + ".fluid"), sample)));

    [Fact]
    public async Task DeserializingAndReserializingIsByteIdentical()
    {
        var contract = ModelContractJson.Build(await PipelineFixture.SolveAsync(PipelineFixture.Sample("m2-substation.fluid")));

        foreach (var indented in new[] { false, true })
        {
            var json = ModelContractJson.Serialize(contract, indented);
            var back = ModelContractJson.Deserialize(json);

            Assert.NotNull(back);
            Assert.Equal(json, ModelContractJson.Serialize(back, indented));
        }
    }

    [Fact]
    public void TextThatIsNotAContractComesBackNull() => Assert.Null(ModelContractJson.Deserialize("{ \"contractVersion\": 3 "));

    [Fact]
    public async Task AnAbsentFieldIsAbsentAndANullOneIsNull()
    {
        var json = ModelContractJson.Serialize(ModelContractJson.Build(await PipelineFixture.SolveAsync(PipelineFixture.Sample("m2-cooling-loop.fluid"))));

        // A pump has no `power`: absent. An inferred node has no source: `null`.
        Assert.DoesNotContain("\"power\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceSpan\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"unit\":\"°C\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00b0", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APayloadOverTheCapLosesItsStatesAndSaysSo()
    {
        var input = await PipelineFixture.SolveAsync(PipelineFixture.Sample("m2-cooling-loop.fluid"));
        var whole = ModelContractJson.Build(input);
        var capped = ModelContractJson.Build(input, maxPayloadBytes: ModelContractJson.MeasureBytes(whole) - 1);

        Assert.All(whole.Circuits, static c => Assert.False(c.StatesOmitted));
        Assert.All(capped.Circuits, static c => Assert.True(c.StatesOmitted));
        Assert.All(capped.Circuits, static c => Assert.True(c.Solved));
        Assert.All(capped.Components, static c => Assert.Null(c.State));
        Assert.Contains(capped.Diagnostics, static d => d.Code == "FS2502");
        Assert.DoesNotContain(whole.Diagnostics, static d => d.Code == "FS2502");
        Assert.NotNull(capped.Solve);
        Assert.Equal(whole.Layout.Order, capped.Layout.Order);
    }

    [Fact]
    public void AHundredNodePipeIsWellUnderTheCap()
    {
        // 26's sizing case: one 100-node pipe is a few hundred components and must fit; the cap is
        // for the several-thousand-component scene.
        var one = ModelContractJson.Build(PipelineFixture.Compile(PipelineFixture.Sample("m2-cooling-loop.fluid").Replace("P1  pipe length=25 dn=25", "P1  pipe length=25 dn=25 nodes=100")));

        Assert.True(ModelContractJson.MeasureBytes(one) < ModelContractJson.MaxPayloadBytes / 4);
        Assert.Equal(210, one.Components.Length);
    }
}
