namespace FluidScript.Core.Fluids;

/// <summary>The one value of a type that carries no information.</summary>
/// <remarks>
/// <see cref="Result{T}"/> answers "did this work, and why not" and needs something to carry when the
/// answer is only "yes". <c>Result&lt;bool&gt;</c> would work and reads as though the <see langword="true"/>
/// meant something; a non-generic <c>Result</c> would be a second type with the same members to keep in
/// step. This is the standard third option and it costs nothing at runtime.
/// </remarks>
public readonly record struct Unit
{
    /// <summary>Gets the value.</summary>
    public static Unit Value => default;
}
