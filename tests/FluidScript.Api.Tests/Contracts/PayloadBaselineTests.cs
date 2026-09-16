using System.Diagnostics;

using FluidScript.Api.Contracts;
using FluidScript.Fixtures;

using Xunit;

namespace FluidScript.Api.Tests.Contracts;

/// <summary>
/// <c>07</c>'s payload budget, measured on the 200-component reference model (<c>05</c> M3, <c>F-24</c>):
/// the compile response at most 512 KiB uncompressed, and serialization inside the 50 ms it shares
/// with the client parse.
/// </summary>
public sealed class PayloadBaselineTests
{
    private const int BudgetBytes = 512 * 1024;

    [Fact]
    public void TheReferenceModelHasTwoHundredComponents()
    {
        var contract = ModelContractJson.Build(PipelineFixture.Compile(ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers)));

        Assert.Equal(200, contract.Components.Length);
        Assert.Equal(
            (ReferenceModels.TwoHundredComponentConsumers * ReferenceModels.ComponentsPerConsumer) + ReferenceModels.HeaderComponents,
            contract.Components.Length);
        Assert.Equal(ReferenceModels.TwoHundredComponentConsumers + 1, contract.Circuits.Length);
        Assert.DoesNotContain(contract.Diagnostics, static d => d.Severity == "error");
    }

    [Fact]
    public void TheCompileResponseIsUnderTheBudget()
    {
        var input = PipelineFixture.Compile(ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers));
        var contract = ModelContractJson.Build(input);

        var bytes = ModelContractJson.MeasureBytes(contract);
        var first = Time(contract);
            var warm = Enumerable.Range(0, 5).Select(_ => Time(contract)).Min();

            // 07's layout-solve line (D-103): the whole projection, layout included, warm, best of five.
            var build = Enumerable.Range(0, 5).Select(_ =>
            {
                var clock = Stopwatch.StartNew();
                ModelContractJson.Build(input);
                return clock.Elapsed.TotalMilliseconds;
            }).Min();

            TestContext.Current.TestOutputHelper?.WriteLine($"compile payload: {bytes} bytes ({bytes / 1024.0:F1} KiB); serialized in {first:F1} ms cold, {warm:F1} ms warm (best of 5); contract built with layout in {build:F1} ms warm (best of 5)");
            Assert.True(build < 500, $"building the contract took {build:F0} ms.");

        Assert.True(bytes <= BudgetBytes, $"{bytes} bytes is over 07's {BudgetBytes} byte budget.");
        Assert.All(contract.Circuits, static c => Assert.False(c.StatesOmitted));
    }

    private static double Time(FluidScript.Core.Model.ModelContract contract)
    {
        var clock = Stopwatch.StartNew();
        ModelContractJson.MeasureBytes(contract);
        return clock.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public async Task TheSolvedResponseIsMeasured()
    {
        var clock = Stopwatch.StartNew();
        var input = await PipelineFixture.SolveAsync(ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers), "header_200");
        var solve = clock.Elapsed.TotalMilliseconds;
        var contract = ModelContractJson.Build(input);
        var bytes = ModelContractJson.MeasureBytes(contract);

        TestContext.Current.TestOutputHelper?.WriteLine($"solved payload: {bytes} bytes ({bytes / 1024.0:F1} KiB); states omitted: {contract.Circuits[0].StatesOmitted}; solve {solve:F0} ms");

        Assert.True(bytes <= ModelContractJson.MaxPayloadBytes, $"{bytes} bytes is over the {ModelContractJson.MaxPayloadBytes} byte cap even without states.");
    }
}
