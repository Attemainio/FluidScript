using System.Text;

using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Syntax.Lexing;

/// <summary>
/// The lexer's classification of every sample, committed so the editor's tokenizer can be held to it
/// (<c>52</c>: the two grammars agree over the whole sample corpus, asserted).
/// </summary>
/// <remarks>
/// One line per token and per comment: the offset, the length, the kind, and for a quantity the
/// offset its unit starts at, since the editor colours the unit apart from its number. The frontend
/// test reads these files and compares its own tokens; a lexer change that reclassifies a word shows
/// up here as a reviewed diff and there as a failure until the tokenizer follows.
/// </remarks>
[Trait("Category", "Golden")]
public sealed class TokenGoldenTests
{
    private const string UpdateVariable = "FLUIDSCRIPT_UPDATE_GOLDENS";

    public static string Directory { get; } =
        Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Language", "Syntax", "Lexing", "TokenGoldens");

    public static TheoryData<string> Samples =>
        [.. ScriptCorpus.EnumerateSampleFiles().Select(static path => Path.GetFileNameWithoutExtension(path))];

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheSamplesTokensAreCommitted(string sample)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample + ".fluid"));
        var actual = Render(source);
        var path = Path.Combine(Directory, sample + ".tokens");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No token golden for '{sample}'. Run once with {UpdateVariable}=1 to create it, then review it.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"The lexer classifies '{sample}' differently from its committed tokens. If the change is intended, run once with {UpdateVariable}=1 and review the diff; the editor's tokenizer test then has to follow.");
    }

    /// <summary>Renders the lexer's tokens and comments as the golden format.</summary>
    /// <param name="source">The script.</param>
    /// <returns>One line per token or comment: <c>offset length kind [unitOffset]</c>.</returns>
    public static string Render(string source)
    {
        var text = new SourceText(source);
        var builder = new StringBuilder();

        foreach (var token in Lexer.Lex(text).Tokens)
        {
            AppendComments(builder, token.LeadingTrivia);

            if (token.Kind != TokenKind.EndOfFile)
            {
                builder.Append(token.Span.Start).Append(' ').Append(token.Span.Length).Append(' ').Append(token.Kind);

                if (token.Kind == TokenKind.QuantityLiteral && token.Unit is { } unit)
                {
                    builder.Append(' ').Append(token.Span.End - unit.Length);
                }

                builder.Append('\n');
            }

            AppendComments(builder, token.TrailingTrivia);
        }

        return builder.ToString();
    }

    private static void AppendComments(StringBuilder builder, IEnumerable<Trivia> trivia)
    {
        foreach (var piece in trivia.Where(static t => t.Kind == TriviaKind.Comment))
        {
            builder.Append(piece.Span.Start).Append(' ').Append(piece.Span.Length).Append(" Comment\n");
        }
    }
}
