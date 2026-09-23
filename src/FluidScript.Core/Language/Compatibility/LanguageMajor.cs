namespace FluidScript.Core.Language.Compatibility;

/// <summary>One major version of the FluidScript language.</summary>
/// <param name="Value">The unsigned decimal the <c>fluidscript</c> directive states.</param>
/// <remarks>There is no minor. A change that needs one is a change that needs a major.</remarks>
public readonly record struct LanguageMajor(int Value);
