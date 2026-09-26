using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Language.Translation;

namespace FluidScript.Core.Language.Compatibility;

/// <summary>Parses a file under the language major its version line selected.</summary>
/// <remarks>
/// <c>18</c>'s invariant 2 puts version selection before parsing; this is the step after it. Language 1 is parsed
/// by <see cref="FluidScriptParser"/> and language 2 by <see cref="FluidScript2Parser"/>, whose tree the binder reads
/// directly (<c>D-177</c>, <c>D-178</c>). A file with no version line is the current major's, language 2 since
/// <c>P6.11</c>'s switch (<c>D-174</c>); language 1 is parsed only when a caller names it, which the pipeline never does,
/// since a <c>fluidscript 1</c> file is not compiled.
/// </remarks>
public static class MajorParser
{
    /// <summary>Parses source text for binding.</summary>
    /// <param name="source">The script.</param>
    /// <param name="major">The major <see cref="ScriptCompatibility.Inspect"/> detected; <see langword="null"/> for an unversioned draft.</param>
    /// <param name="registry">The component kinds; kept so a caller need not know which major reads them.</param>
    /// <returns>
    /// The tree the binder reads, with every parser diagnostic -- in language 2's words for a language 2 file
    /// (<see cref="Language2Wording"/>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source, LanguageMajor? major, IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);

        if (major is { Value: 1 })
        {
            return FluidScriptParser.Parse(source);
        }

        var parse = FluidScript2Parser.Parse(source);
        return parse with { Diagnostics = Language2Wording.Apply(parse.Diagnostics) };
    }
}
