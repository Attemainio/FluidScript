namespace FluidScript.Core.Model.Contract;

/// <summary>The <c>project</c> block.</summary>
/// <param name="Name">The project's title.</param>
/// <remarks>Contract 3.0 dropped <c>defaultMode</c>: language 2 states a mode per run (<c>D-169</c>, <c>C-139</c>).</remarks>
public sealed record ProjectWire(string? Name);
