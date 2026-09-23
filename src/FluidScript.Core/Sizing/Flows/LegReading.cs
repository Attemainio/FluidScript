namespace FluidScript.Core.Sizing.Flows;

/// <summary>A three-way valve's two switched legs as an iterate has them.</summary>
/// <param name="Mixing"><see langword="true"/> when both legs enter the valve, <see langword="false"/> when both leave it.</param>
/// <param name="DropA">Pa across the <c>a</c> leg in the flow's direction: its far port less the common port when mixing, the reverse when diverting. Positive for a leg the valve dissipates on.</param>
/// <param name="DropB">Pa across the <c>b</c> leg, the same convention.</param>
/// <param name="FlowA">kg/s through the <c>a</c> leg, magnitude.</param>
/// <param name="FlowB">kg/s through the <c>b</c> leg, magnitude.</param>
public readonly record struct LegReading(bool Mixing, double DropA, double DropB, double FlowA, double FlowB)
{
    /// <summary>Gets how much easier the <c>b</c> path is than the <c>a</c> path, Pa: positive when the valve throttles <c>b</c> to make up the difference, negative when it throttles <c>a</c>.</summary>
    public double Imbalance => DropB - DropA;
}
