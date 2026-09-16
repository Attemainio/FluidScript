using System.Text;

using FluidScript.Core.Layout;
using FluidScript.Core.Tests.Model;

namespace FluidScript.Core.Tests.Layout;

/// <summary>Prints the hints of every sample, for a session designing against real data.</summary>
public sealed class HintDumpTests
{
    [Theory]
    [InlineData("m2-cooling-loop")]
    [InlineData("m2-simple-loop")]
    [InlineData("m2-substation")]
    [InlineData("m4-storage-header")]
    [InlineData("m2-distribution-header")]
    public void Dump(string sample)
    {
        var input = ContractFixture.Compile(ContractFixture.Sample(sample + ".fluid"));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var text = new StringBuilder();
        text.AppendLine($"== {sample}");
        text.AppendLine("Order: " + string.Join(" ", hints.Order));

        foreach (var stage in hints.ThermalStages) text.AppendLine($"Stage {stage.Rank} {stage.Role}: " + string.Join(" ", stage.Components));
        foreach (var circuit in hints.Circuits) text.AppendLine($"Circuit {circuit.Name} parent={circuit.ParentCircuit} supply={circuit.SupplyAnchorId} return={circuit.ReturnAnchorId}");
        foreach (var group in hints.DistributionGroups) text.AppendLine($"Group {group.ParentCircuit}: " + string.Join(" ", group.Members));

        foreach (var e in hints.NonFlowElements) text.AppendLine($"NonFlow {e.ComponentId} at {e.PlacementAnchorId} measures {e.MeasurementTargetId}");
        text.AppendLine("Connections: " + string.Join(" ", input.Model.Connections.Select(static c => $"{c.From.Component}.{c.From.Port}-{c.To.Component}.{c.To.Port}")));
        text.AppendLine("Kinds: " + string.Join(" ", input.Graph.Components.Select(static c => $"{c.Name}:{c.Kind}")));
        TestContext.Current.TestOutputHelper?.WriteLine(text.ToString());
    }
}
