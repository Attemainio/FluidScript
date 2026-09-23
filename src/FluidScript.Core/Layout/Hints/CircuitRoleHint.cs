using FluidScript.Core.Language.Binding;

namespace FluidScript.Core.Layout.Hints;

/// <summary>A resolved circuit role and the stage it biases toward.</summary>
/// <param name="CanonicalName">The registry's canonical role name.</param>
/// <param name="Stage">The stage the role is evidence for.</param>
public sealed record CircuitRoleHint(string CanonicalName, ThermalStageRole Stage);
