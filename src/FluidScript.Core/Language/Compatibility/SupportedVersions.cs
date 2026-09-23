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
    /// <value>Major 1 only. A second entry appears the day a major 2 exists, with its migration.</value>
    public static SupportedVersions Default { get; } = new(new LanguageMajor(1), [new LanguageMajor(1)]);
}
