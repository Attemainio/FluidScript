using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;

namespace FluidScript.Core.Language.Translation;

/// <summary>Turns a language 2 syntax tree into the statements the binder reads (<c>plan/10-language/19-fluidscript-2.md</c> §Translation to the binder).</summary>
/// <remarks>
/// <para>
/// <strong>The binder is shared; the syntax is not.</strong> Language 2 has its own tree, which the printer
/// prints and the editor reads (<see cref="FluidScript2Parser"/>). This produces a second tree, of language 1's
/// statement nodes, for the binder alone: every token in it is either one of the language 2 tree's or one made
/// here whose span lies inside the language 2 text it stands for, so every diagnostic the binder raises lands on
/// what the user wrote. The translated tree is never printed.
/// </para>
/// <para>
/// <strong>Never throws on user input</strong> (principle P4). A statement the parser could not read is already
/// reported and is left out; one the translation cannot place is reported here and left out, and the rest of
/// the file still binds.
/// </para>
/// </remarks>
public static class Language2Translator
{
    /// <summary>Translates a parsed language 2 file.</summary>
    /// <param name="language2">What <see cref="FluidScript2Parser.Parse"/> returned.</param>
    /// <param name="registry">The component kinds, for what a declaration's kind decides: whether it is a sensor.</param>
    /// <returns>
    /// The statements the binder reads, over the same source, with <see cref="ParseResult.Language"/> 2 and the
    /// parser's diagnostics followed by the translation's.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static ParseResult Translate(ParseResult language2, IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(language2);
        ArgumentNullException.ThrowIfNull(registry);

        return new TranslationRun(language2, registry).Execute();
    }
}
