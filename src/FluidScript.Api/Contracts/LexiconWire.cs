using System.Collections.Immutable;

using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Api.Contracts;

/// <summary>What the editor's tokenizer needs to classify a word without a round trip (<c>52</c> invariant 2).</summary>
/// <remarks>
/// The lexical grammar is table-driven on a table Core owns: the unit symbols that turn <c>20C</c> into a quantity
/// and leave <c>3WV</c> a name. Language 2's lexer reserves no word; its statement words are known by where they
/// stand (<c>19</c>), and the highlighter needs the list to colour them there. Both must agree with Core and must
/// not wait for the network, so they are committed beside the schemas as <c>language.json</c>, generated into the
/// frontend and gated like the schemas. The resolution thresholds ride along for completion's ranking.
/// </remarks>
/// <param name="StatementWords">The words that open a statement at a line's start, in <c>19</c>'s order.</param>
/// <param name="EventWords">The words that open an event inside a run: <c>at</c>, <c>over</c>.</param>
/// <param name="UnitSymbols">Every unit spelling a script may write after a number, longest first, as the lexer probes them; <c>in</c> and <c>t</c> are not among them (<c>A-9</c>).</param>
/// <param name="ResolveThreshold">The similarity at which a misspelled kind or parameter is offered as the fix (<c>D-15</c>, <c>D-170</c>: it suggests and never binds).</param>
/// <param name="AmbiguityMargin">How far clear of the runner-up a match must be, or both are reported.</param>
/// <param name="SuggestionFloor">The score below which a failed match carries no suggestion.</param>
public sealed record LexiconWire(
    ImmutableArray<string> StatementWords,
    ImmutableArray<string> EventWords,
    ImmutableArray<string> UnitSymbols,
    double ResolveThreshold,
    double AmbiguityMargin,
    double SuggestionFloor)
{
    /// <summary>The lexicon of the deployed build.</summary>
    public static LexiconWire Current { get; } = new(
        SettingRegistry.StatementWords,
        SettingRegistry.EventWords,
        [.. UnitTable.All.Select(static u => u.Text)
            .Where(static text => !Lexer.ExcludedUnitSymbols.Contains(text, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static t => t.Length)
            .ThenBy(static t => t, StringComparer.Ordinal)],
        NameResolution.ResolveThreshold,
        NameResolution.AmbiguityMargin,
        NameResolution.SuggestionFloor);
}
