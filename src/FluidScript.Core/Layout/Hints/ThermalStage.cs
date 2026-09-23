using System.Collections.Immutable;

using FluidScript.Core.Language.Binding;

namespace FluidScript.Core.Layout.Hints;

/// <summary>One thermal stage: a rank on the heat-progression axis, its role, and its members.</summary>
public sealed record ThermalStage
{
    /// <summary>The stage's rank, left to right from 0. Several stages may share one.</summary>
    public required int Rank { get; init; }

    /// <summary>What the stage does with heat.</summary>
    public required ThermalStageRole Role { get; init; }

    /// <summary>The components in the stage, in source order.</summary>
    public required ImmutableArray<string> Components { get; init; }
}
