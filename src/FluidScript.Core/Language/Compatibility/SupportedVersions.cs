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
    /// Current 1, supported 1 and 2: language 2 is compiled beside language 1 while it is built (<c>D-164</c>,
    /// <c>SupportedNewer</c>), and becomes current at <c>P6.11</c>'s switch-over, a decision of its own.
    /// </value>
    public static SupportedVersions Default { get; } = new(new LanguageMajor(1), [new LanguageMajor(1), new LanguageMajor(2)]);
}
