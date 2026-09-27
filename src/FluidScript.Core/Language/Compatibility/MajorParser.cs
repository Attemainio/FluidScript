using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Compatibility;

/// <summary>Parses a file under the language major its version line selected.</summary>
/// <remarks>
/// <c>18</c>'s invariant 2 puts version selection before parsing; this is the step after it. Major 2 is the only
/// one supported (<c>D-174</c>), parsed by <see cref="FluidScriptParser"/>, whose tree the binder reads directly (<c>D-177</c>).
/// A file declaring any other major is not compiled (<see cref="ScriptCompatibility"/>), so the major
/// selects nothing today; it stays so the next major has a place to branch.
/// </remarks>
public static class MajorParser
{
    /// <summary>Parses source text for binding.</summary>
    /// <param name="source">The script.</param>
    /// <param name="major">The major <see cref="ScriptCompatibility.Inspect"/> detected; <see langword="null"/> for an unversioned draft.</param>
    /// <param name="registry">The component kinds; kept so a caller need not know which major reads them.</param>
    /// <returns>The tree the binder reads, with every parser diagnostic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source, LanguageMajor? major, IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);

        return FluidScriptParser.Parse(source);
    }
}
