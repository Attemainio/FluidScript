namespace FluidScript.Core.Language.Binding;

/// <summary>A reference to a component's property, such as <c>N2.t</c>.</summary>
/// <param name="Component">The component's name, as written.</param>
/// <param name="Property">The property's canonical name.</param>
public readonly record struct PropertyReference(string Component, string Property);
