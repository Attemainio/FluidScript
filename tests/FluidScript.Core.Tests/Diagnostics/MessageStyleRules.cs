using System.Collections.Immutable;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>
/// The mechanically checkable half of the message style rules in
/// <c>plan/10-language/16-diagnostics.md</c>, applied to a descriptor's rendered message.
/// </summary>
/// <remarks>
/// <para>
/// Rules 2 through 5 -- say what is wrong then what to do, name the thing, list the alternatives,
/// suggest by edit distance -- are judgement, and a test that pretended to check them would only
/// check a proxy. What is left is still worth automating: it is the set that decays silently as a
/// hundred messages are written by different hands on different days.
/// </para>
/// <para>
/// Every check runs against the <em>rendered</em> message rather than the template, because a
/// placeholder name is not user-visible text. <c>FS1105</c>'s template legitimately contains
/// <c>{token}</c>; the message a user reads does not contain the word.
/// </para>
/// </remarks>
public static class MessageStyleRules
{
    /// <summary>Terms rule 6 keeps out of a message about a user's script.</summary>
    /// <remarks>
    /// The user wrote a line of text; the message is about that line. Internal vocabulary belongs to
    /// the <see cref="DiagnosticStage.Internal"/> range, whose messages are bug reports and are
    /// therefore exempt.
    /// </remarks>
    public static readonly ImmutableArray<string> BannedJargon =
    [
        "token", "tokens", "AST", "abstract syntax tree", "syntax tree", "syntax node",
        "lexer", "parser", "binder", "trivia", "residual", "Jacobian", "null", "nullable",
        "exception", "stack trace", "enum", "boolean",
    ];

    private const string PlaceholderValue = "X";

    private static readonly Regex DoubleSpace = new(@"\S  +\S", RegexOptions.None, TimeSpan.FromSeconds(1));

    // Rule 7 bans blame, not the second person: "Did you mean 'power'?" is the wording rule 5
    // mandates, so a check on the bare word would ban the message the plan asks for.
    private static readonly Regex Blame = new(
        @"\byou\s+(forgot|failed|must|should|need|neglected|cannot|can't|didn't|did not|have to)\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>Lists every style rule the descriptor's message breaks.</summary>
    /// <param name="descriptor">The code to check.</param>
    /// <returns>
    /// One sentence per violation, naming the rule and quoting what triggered it. Empty when the
    /// message is clean.
    /// </returns>
    public static ImmutableArray<string> Violations(DiagnosticDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var arguments = descriptor.ArgumentNames
            .Select(static name => new DiagnosticArgument(name, PlaceholderValue))
            .ToArray();
        var message = descriptor.Render(arguments);

        var violations = ImmutableArray.CreateBuilder<string>();

        if (!message.EndsWith('.') && !message.EndsWith('?'))
        {
            violations.Add("rule 1: a message is a sentence and ends in a period.");
        }

        if (message.Contains('!', StringComparison.Ordinal))
        {
            violations.Add("rule 7: no exclamation.");
        }

        var blame = Blame.Match(message);
        if (blame.Success)
        {
            violations.Add($"rule 7: no blame -- '{blame.Value}' faults the user rather than describing the script.");
        }

        if (!message.Any(char.IsLower))
        {
            violations.Add("rule 1: sentence case, not capitals.");
        }

        if (DoubleSpace.IsMatch(message))
        {
            violations.Add("rule 1: a message is ordinary prose, with single spaces between words.");
        }

        if (descriptor.Stage != DiagnosticStage.Internal)
        {
            violations.AddRange(
                BannedJargon
                    .Where(term => ContainsWord(message, term))
                    .Select(static term => $"rule 6: '{term}' is internal vocabulary the script never uses."));
        }

        return violations.ToImmutable();
    }

    private static bool ContainsWord(string message, string term)
    {
        var index = message.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var openingIsBoundary = index == 0 || !char.IsLetter(message[index - 1]);
            var after = index + term.Length;
            var closingIsBoundary = after == message.Length || !char.IsLetter(message[after]);

            if (openingIsBoundary && closingIsBoundary)
            {
                return true;
            }

            index = message.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
