namespace FluidScript.Core.Language.Binding;

/// <summary>A circuit's thermal classification, feeding <c>D-31</c>'s staging.</summary>
/// <param name="CanonicalName">The registered role name, or <c>neutral</c>.</param>
/// <param name="Stage">Where a circuit of this role sits in the thermal chain.</param>
/// <remarks>
/// Registry data, not a closed set in the language: adding a role is a registry change, never a
/// grammar change (<c>D-35</c>).
/// </remarks>
public sealed record CircuitRole(string CanonicalName, ThermalStageRole Stage);
