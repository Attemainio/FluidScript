using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// The lexer's two structural invariants: it keeps every character, and it terminates on anything.
/// </summary>
/// <remarks>
/// <para>
/// <c>plan/10-language/12-grammar.md</c>'s invariant 1 is asserted here rather than through the
/// printer, because here it is one line -- concatenate and compare. Once the parser and printer exist,
/// a failure of the same invariant surfaces as a round-trip difference several layers up, and finding
/// which stage dropped the character is the expensive part.
/// </para>
/// <para>
/// The corpus is deliberately wider than <c>samples/</c>: every fenced <c>fluidscript</c> block in
/// <c>plan/</c> and <c>docs/</c> is included, fragments and deliberately-wrong examples alike. These
/// properties hold for arbitrary text, so there is no reason to test them only on text we expect to be
/// valid -- and a specification's own examples are exactly where a contradiction hides.
/// </para>
/// </remarks>
public sealed class LosslessnessTests
{
    // Weighted towards the characters that carry lexical decisions, because a uniform draw over the
    // whole code-point space spends nearly every mutation on a character the lexer treats identically.
    private static readonly ImmutableArray<char> Interesting =
        [.. "\"#=.-+*/%@,()\n\r\t 0123456789eE_kWmsK° é"];

    /// <summary>Text that is not a script, chosen for the ways a scanner fails to terminate.</summary>
    private static readonly string[] Adversarial =
    [
        "",
        " ",
        "\n",
        "\r",
        "\r\n",
        "\n\r",
        "#",
        "#no newline at end",
        "\"",
        "\"unterminated",
        "\"unterminated\nnext line",
        ".",
        "..",
        "...",
        "30.",
        "30..60",
        "30...60",
        "1e",
        "1e+",
        "1e-",
        "1exchanger",
        "0x1F",
        "%",
        "10 % 3",
        "é",
        "😀",
        "﻿fluidscript 1",
        "let x = ",
        "= = =",
        "((((((((((",
        "3WV",
        "30 ",
        "30 kW",
        "30kW",
        "\t\t\t",
        "a b",
    ];

    public static TheoryData<string, string> Corpus()
    {
        var data = new TheoryData<string, string>();
        foreach (var source in ScriptCorpus.All())
        {
            data.Add(source.Name, source.Text);
        }

        return data;
    }

    public static TheoryData<string> AdversarialText()
    {
        var data = new TheoryData<string>();
        foreach (var text in Adversarial)
        {
            data.Add(text);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    [Trait("Category", "Unit")]
    public void EveryCharacterSurvives(string name, string text) => AssertLossless(name, text);

    [Theory]
    [MemberData(nameof(AdversarialText))]
    [Trait("Category", "Unit")]
    public void AdversarialTextIsLosslessToo(string text) => AssertLossless("adversarial", text);

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCorpusIsNotEmpty()
    {
        // A losslessness suite that silently walks zero files passes forever. This is the assertion
        // that fails the day samples/ is emptied or the fence marker changes.
        Assert.NotEmpty(ScriptCorpus.Samples());
        Assert.True(
            ScriptCorpus.MarkdownBlocks().Length >= 20,
            $"Expected the plan's own examples to be found; got {ScriptCorpus.MarkdownBlocks().Length}.");
    }

    [Fact]
    [Trait("Category", "Property")]
    public void DeletingAnyOneCharacterOfASampleStillLexes()
    {
        // 12's acceptance criterion, at the lexer: deleting each character of each sample in turn
        // never throws and always yields tokens. Every intermediate state of a script being edited is
        // reachable this way, and each one is a state the editor will actually lex.
        foreach (var sample in ScriptCorpus.Samples())
        {
            for (var i = 0; i < sample.Text.Length; i++)
            {
                AssertLossless($"{sample.Name} less character {i}", sample.Text.Remove(i, 1));
            }
        }
    }

    [Fact]
    [Trait("Category", "Property")]
    public void RandomMutationsNeverThrowAndNeverEscapeTheirBounds()
    {
        // A fixed seed: a fuzz test that finds a different failure on every run cannot be bisected,
        // and one that finds none is indistinguishable from one that is not running.
        var random = new Random(20260901);
        var seeds = ScriptCorpus.Samples();
        Assert.NotEmpty(seeds);

        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            var seed = seeds[random.Next(seeds.Length)].Text;
            AssertLossless($"mutation {iteration}", Mutate(seed, random));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrailingTriviaStopsAtTheLineBreak()
    {
        var result = Lexer.Lex(new SourceText("let a = 1   # tail\nlet b = 2\n"));
        var one = result.Tokens.Single(static token => token.Text == "1");

        Assert.Equal(
            [TriviaKind.Whitespace, TriviaKind.Comment],
            one.TrailingTrivia.Select(static trivia => trivia.Kind));

        // The newline belongs to whatever comes next, which is what keeps a blank line owned by the
        // statement it precedes rather than the one it follows.
        var next = result.Tokens[result.Tokens.IndexOf(one) + 1];
        Assert.Equal(TriviaKind.EndOfLine, next.LeadingTrivia[0].Kind);
    }

    private static void AssertLossless(string name, string text)
    {
        var source = new SourceText(text);
        var result = Lexer.Lex(source);

        Assert.Equal(TokenKind.EndOfFile, result.Tokens[^1].Kind);
        Assert.Single(result.Tokens, static token => token.Kind == TokenKind.EndOfFile);

        var rebuilt = new StringBuilder(text.Length);
        var position = 0;

        foreach (var token in result.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                AssertContiguous(name, trivia.Span, ref position, source);
                rebuilt.Append(source.Slice(trivia.Span));
            }

            AssertContiguous(name, token.Span, ref position, source);
            rebuilt.Append(token.Text);

            foreach (var trivia in token.TrailingTrivia)
            {
                AssertContiguous(name, trivia.Span, ref position, source);
                rebuilt.Append(source.Slice(trivia.Span));
            }
        }

        Assert.Equal(text.Length, position);
        Assert.Equal(text, rebuilt.ToString());

        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.NotNull(diagnostic.Span);
            Assert.True(
                diagnostic.Span!.Value.End <= text.Length,
                $"{name}: {diagnostic.Code} points at {diagnostic.Span} in {text.Length} characters.");
        }
    }

    // Contiguity is the stronger half of losslessness. Comparing the concatenation alone would accept
    // a lexer that emitted the right characters in the wrong spans, which the printer would survive
    // and every squiggle in the editor would not.
    private static void AssertContiguous(string name, TextSpan span, ref int position, SourceText source)
    {
        Assert.True(
            span.Start == position,
            $"{name}: a gap or overlap at {position}; the next span begins at {span.Start}.");
        Assert.True(
            span.End <= source.Length,
            $"{name}: {span} reaches past {source.Length} characters.");
        position = span.End;
    }

    private static string Mutate(string text, Random random)
    {
        var builder = new StringBuilder(text);
        var edits = random.Next(1, 6);

        for (var i = 0; i < edits && builder.Length > 0; i++)
        {
            var at = random.Next(builder.Length);
            switch (random.Next(3))
            {
                case 0:
                    builder.Remove(at, 1);
                    break;
                case 1:
                    builder.Insert(at, Interesting[random.Next(Interesting.Length)]);
                    break;
                default:
                    builder[at] = Interesting[random.Next(Interesting.Length)];
                    break;
            }
        }

        return builder.ToString();
    }
}
