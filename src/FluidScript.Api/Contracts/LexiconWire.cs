using System.Collections.Immutable;

using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Api.Contracts;

/// <summary>What the editor's tokenizer needs to classify a word without a round trip (<c>52</c> invariant 2).</summary>
/// <remarks>
/// The lexical grammar of <c>12</c> is table-driven on two tables Core owns: the reserved words, and
/// the unit symbols that turn <c>20C</c> into a quantity and leave <c>3WV</c> a name. The editor's
/// highlighter must agree with the lexer on both and must not wait for the network, so the tables
/// are committed beside the schemas as <c>language.json</c>, generated into the frontend and gated
/// like the schemas. The resolution thresholds ride along for completion (<c>52</c> invariant 5:
/// the similarity threshold comes from the server).
/// </remarks>
/// <param name="ReservedWords">Every reserved word, in <c>12</c>'s order.</param>
/// <param name="UnitSymbols">Every accepted unit spelling, longest first, as the lexer probes them.</param>
/// <param name="ResolveThreshold">The similarity a written kind or parameter must reach to resolve (<c>D-15</c>).</param>
/// <param name="AmbiguityMargin">How far clear of the runner-up a match must be, or both are reported.</param>
/// <param name="SuggestionFloor">The score below which a failed match carries no suggestion.</param>
public sealed record LexiconWire(
    ImmutableArray<string> ReservedWords,
    ImmutableArray<string> UnitSymbols,
    double ResolveThreshold,
    double AmbiguityMargin,
    double SuggestionFloor)
{
    /// <summary>The lexicon of the deployed build.</summary>
    public static LexiconWire Current { get; } = new(
        FluidScript.Core.Language.Syntax.Lexing.ReservedWords.All,
        [.. UnitTable.All.Select(static u => u.Text).Distinct(StringComparer.Ordinal).OrderByDescending(static t => t.Length).ThenBy(static t => t, StringComparer.Ordinal)],
        NameResolution.ResolveThreshold,
        NameResolution.AmbiguityMargin,
        NameResolution.SuggestionFloor);
}
