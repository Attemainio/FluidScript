using System.Collections.Immutable;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Registry;

public sealed partial class ComponentRegistry
{
    // Everything asserted here is a rule the data can break silently. A duplicated normalised spelling
    // would make one kind unreachable depending on registration order; an alias equal to a reserved
    // word would be unwriteable, because a reserved word never reaches kind position (`D-40` did
    // exactly this to `control`); a tag code that lexes as a unit would produce equipment tags the
    // language reads as numbers.
    private static void Verify(
        ImmutableArray<ComponentKindInfo> kinds,
        ImmutableDictionary<string, ComponentKindInfo> index)
    {
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kind in kinds)
            foreach (var spelling in new[] { kind.Keyword }.Concat(kind.Aliases))
            {
                var normalized = NameResolution.Normalize(spelling);

                if (claimed.TryGetValue(normalized, out var owner) && owner != kind.Keyword)
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' resolves to both '{owner}' and '{kind.Keyword}'.");
                }

                claimed[normalized] = kind.Keyword;

                // A kind's own keyword MAY be a reserved word. It could not be until `D-64` made
                // `S1 supply t=5` a declaration: statement classification reads the *first* token, so a
                // reserved word in kind position is unambiguous, and `supply N3` still attaches a
                // subcircuit because that line starts with the keyword.
                //
                // An alias may not, and the difference is worth keeping. An alias is a convenience
                // spelling, so one that collides with a reserved word buys a second way to write
                // something already writable and costs a reader the question of which they are looking
                // at. Only a kind the decision log sanctions should be reachable by a reserved word.
                if (!string.Equals(spelling, kind.Keyword, StringComparison.Ordinal)
                    && ReservedWords.TryMatch(spelling, out _))
                {
                    throw new InvalidOperationException(
                        $"'{spelling}' is a reserved word, so it may not be an alias for '{kind.Keyword}'.");
                }
            }

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kind in kinds.Where(static kind => kind.TagCode is not null))
        {
            var code = kind.TagCode!;

            if (codes.TryGetValue(code, out var owner))
            {
                throw new InvalidOperationException($"'{owner}' and '{kind.Keyword}' share tag code '{code}'.");
            }

            codes[code] = kind.Keyword;

            // The tag itself, lexed. Checking against the unit table would miss the real failure: it
            // is the whole tag that must not read as a number and a unit, not the code alone.
            var tag = $"100{code}01";
            var tokens = Lexer.Lex(new SourceText(tag)).Tokens;

            if (tokens[0].Kind != TokenKind.Identifier || tokens[0].Text != tag)
            {
                throw new InvalidOperationException(
                    $"Tag code '{code}' makes '{tag}' lex as {tokens[0].Kind}, not one identifier.");
            }
        }

        // A marker naming a parameter or property the kind does not have would make the short control
        // form resolve to nothing at bind time, with a message about a name the registry itself
        // invented. Asserted here, where the fix is one row away.
        foreach (var kind in kinds)
        {
            if (kind.ActuatedParameter is { } actuated && !kind.Parameters.ContainsKey(actuated))
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' actuates '{actuated}', which is not one of its parameters.");
            }

            if (kind.MeasuredProperty is { } measured && !kind.Properties.ContainsKey(measured))
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' measures '{measured}', which is not one of its properties.");
            }

            if (kind.IsObserver && !kind.Ports.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"'{kind.Keyword}' observes a node, so it may carry no ports.");
            }

            // A group naming a parameter the kind does not have would never fill up, so the code it
            // carries could not fire and nothing would say why. A group with as many freedoms as
            // members is the same failure spelled differently.
            foreach (var group in kind.ParameterGroups)
            {
                foreach (var parameter in group.Parameters)
                {
                    // Groups name keys, since they are read against what a component stated.
                    if (!kind.Parameters.Values.Any(row => string.Equals(row.Key, parameter, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException(
                            $"'{kind.Keyword}' groups '{parameter}', which is not one of its parameters.");
                    }
                }

                if (group.Freedoms < 1 || group.Freedoms >= group.Parameters.Length)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group of {group.Parameters.Length} with "
                        + $"{group.Freedoms} freedoms, which can never be over-determined.");
                }

                // A lower bound with no code to raise would reject a script and say nothing, and one
                // above the freedoms would reject every script including the ones it is there to allow.
                if (group.Minimum > 0 && group.MinimumDescriptor is null)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group with a minimum of {group.Minimum} and no code "
                        + "to raise when it is not met.");
                }

                if (group.Minimum < 0 || group.Minimum > group.Freedoms)
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has a group needing at least {group.Minimum} of its members "
                        + $"stated and at most {group.Freedoms}, which nothing can satisfy.");
                }
            }

            // A family whose pattern carries no placeholder matches nothing, and one whose bound names
            // a parameter the kind lacks has no maximum at all. Both leave a name the registry
            // advertises and no reference can reach.
            foreach (var (pattern, bound) in Families(kind))
            {
                if (!pattern.Contains("{index}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' has an indexed family '{pattern}' with no '{{index}}' in it.");
                }

                if (bound is { } parameter && !kind.Parameters.ContainsKey(parameter))
                {
                    throw new InvalidOperationException(
                        $"'{kind.Keyword}' bounds '{pattern}' by '{parameter}', which is not one of its "
                        + "parameters.");
                }
            }
        }

        if (index.Count < kinds.Length)
        {
            throw new InvalidOperationException("Every kind must be reachable by at least its keyword.");
        }
    }
}
