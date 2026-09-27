using System.Collections.Immutable;

namespace FluidScript.Core.Language.Compatibility;

/// <summary>The language majors an application build understands.</summary>
/// <param name="Current">The major a new or saved file is written in.</param>
/// <param name="Supported">
/// Every major that can still be compiled and solved, including <paramref name="Current"/>.
/// </param>
public sealed record SupportedVersions(LanguageMajor Current, ImmutableArray<LanguageMajor> Supported)
{
    /// <summary>Gets what this build of FluidScript supports.</summary>
    /// <value>
    /// Current 2, supported 2 alone (<c>D-174</c>): a file that states another major is unsupported, readable as
    /// text and nothing else (<c>18</c>).
    /// </value>
    public static SupportedVersions Default { get; } = new(new LanguageMajor(2), [new LanguageMajor(2)]);
}
