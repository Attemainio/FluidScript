using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Language.Translation;

namespace FluidScript.Core.Language.Compatibility;

/// <summary>Parses a file under the language major its version line selected, into the statements the binder reads.</summary>
/// <remarks>
/// <c>18</c>'s invariant 2 puts version selection before parsing; this is the step after it. Language 1 is parsed
/// by <see cref="FluidScriptParser"/>. Language 2 is parsed by <see cref="FluidScript2Parser"/> and translated
/// (<see cref="Language2Translator"/>), so the one binder reads both (<c>D-164</c>). A file with no version line is
/// the current major's, which is language 1 until <c>P6.11</c>'s switch-over.
/// </remarks>
public static class MajorParser
{
    /// <summary>Parses source text for binding.</summary>
    /// <param name="source">The script.</param>
    /// <param name="major">The major <see cref="ScriptCompatibility.Inspect"/> detected; <see langword="null"/> for an unversioned draft.</param>
    /// <param name="registry">The component kinds, which language 2's translation reads.</param>
    /// <returns>
    /// The tree the binder reads, with every parser and translation diagnostic — in language 2's words for a language 2
    /// file (<see cref="Language2Wording"/>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ParseResult Parse(SourceText source, LanguageMajor? major, IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);

        if (major is not { Value: 2 })
        {
            return FluidScriptParser.Parse(source);
        }

        var translated = Language2Translator.Translate(FluidScript2Parser.Parse(source), registry);
        return translated with { Diagnostics = Language2Wording.Apply(translated.Diagnostics) };
    }
}
