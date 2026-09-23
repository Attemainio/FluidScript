namespace FluidScript.Api.Contracts;

/// <summary>An indexed port family.</summary>
/// <param name="Prefix">The name before the index, <c>in</c> for the port id <c>in2</c>: a component's port ids and the model's keys are <c>{Prefix}{index}</c>.</param>
/// <param name="Pattern">How a script writes a member, with one <c>{index}</c> placeholder: <c>in[{index}]</c> (<c>D-120</c>). The first member is the fixed port <c>in</c>, listed under <c>ports</c>.</param>
/// <param name="MinIndex">The lowest index the family itself covers; the fixed first port sits below it.</param>
/// <param name="MaxIndex">The highest index.</param>
/// <param name="Role"><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</param>
/// <param name="LevelParameterSuffix">The suffix of the parameter key that places the port (<c>in2_level</c>), or <see langword="null"/>; the script spelling of that parameter is under <c>indexedParameters</c>.</param>
public sealed record PortFamilyWire(string Prefix, string Pattern, int MinIndex, int MaxIndex, string Role, string? LevelParameterSuffix);
