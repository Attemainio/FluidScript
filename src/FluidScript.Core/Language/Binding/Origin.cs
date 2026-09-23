namespace FluidScript.Core.Language.Binding;

/// <summary>How a component came to exist.</summary>
public abstract record Origin
{
    private Origin()
    {
    }

    /// <summary>The user wrote a declaration for it.</summary>
    public sealed record Declared : Origin;

    /// <summary>An inference rule created it.</summary>
    /// <param name="Rule">The rule's id — <c>I1</c>, <c>I2</c> or <c>I3</c>.</param>
    /// <param name="StableKey">
    /// What the name is derived from, so the same script always produces the same name.
    /// </param>
    public sealed record Inferred(string Rule, string StableKey) : Origin;
}
