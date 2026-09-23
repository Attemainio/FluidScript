using System.Collections.Immutable;
using System.Text;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// The parser's structural invariants: it keeps every token, it never throws, and one token of
/// lookahead classifies every line.
/// </summary>
/// <remarks>
/// The losslessness assertion is the lexer's, re-run over the parsed tree (<c>D-55</c>). It is what
/// proves the tree can round-trip before a printer exists to try: a node that dropped a keyword, an
/// <c>=</c> or a run of interior whitespace fails here rather than at P2.5, when the binder and the
/// registry would already be built on it.
/// </remarks>
public sealed class ParserPropertyTests
{
    public static TheoryData<string, string> Corpus()
    {
        var data = new TheoryData<string, string>();
        foreach (var source in ScriptCorpus.All())
        {
            data.Add(source.Name, source.Text);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    [Trait("Category", "Unit")]
    public void TheTreeKeepsEveryToken(string name, string text) => AssertLossless(name, text);

    [Fact]
    [Trait("Category", "Property")]
    public void DeletingAnyOneCharacterOfASampleStillYieldsATree()
    {
        // R-05 and P4 together: every intermediate state of a script being edited is reachable this
        // way, and each one has to produce a tree rather than an exception.
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
        var random = new Random(20260901);
        var seeds = ScriptCorpus.Samples();
        Assert.NotEmpty(seeds);

        for (var iteration = 0; iteration < 5_000; iteration++)
        {
            var seed = seeds[random.Next(seeds.Length)].Text;
            AssertLossless($"mutation {iteration}", Mutate(seed, random));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecoveryIsLineGranular()
    {
        // The line in the middle cannot be read; the two around it are unaffected. That is what makes
        // R-05 real -- a script under editing is broken somewhere almost always, and one broken line
        // must not cost the reader the other forty.
        const string Text = """
            fluidscript 1
            circuit demo
            ! ? !
            HE1 heat_exchanger power=30
            """;

        var result = FluidScriptParser.Parse(new SourceText(Text));

        Assert.Collection(
            result.Root.Statements,
            statement => Assert.IsType<VersionDirectiveSyntax>(statement),
            statement => Assert.IsType<CircuitHeaderSyntax>(statement),
            statement => Assert.IsType<MalformedStatementSyntax>(statement),
            statement => Assert.IsType<ComponentDeclarationSyntax>(statement));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AParentSpanContainsEveryChildSpan()
    {
        // Invariant 3, which holds by construction now that a span is derived from a node's tokens
        // rather than computed at each of forty sites (D-55). The test is what keeps that true.
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = FluidScriptParser.Parse(new SourceText(sample.Text));
            foreach (var statement in result.Root.Statements)
            {
                var span = statement.Span;
                foreach (var token in statement.Tokens)
                {
                    Assert.True(
                        span.Contains(token.Span),
                        $"{sample.Name}: {token} at {token.Span} escapes its statement's {span}.");
                }
            }
        }
    }

    private static void AssertLossless(string name, string text)
    {
        var source = new SourceText(text);
        var result = FluidScriptParser.Parse(source);

        var rebuilt = new StringBuilder(text.Length);
        var position = 0;

        foreach (var token in result.Root.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                Assert.True(trivia.Span.Start == position, $"{name}: a gap at {position}.");
                rebuilt.Append(source.Slice(trivia.Span));
                position = trivia.Span.End;
            }

            Assert.True(token.Span.Start == position, $"{name}: a gap at {position} before {token}.");
            rebuilt.Append(token.Text);
            position = token.Span.End;

            foreach (var trivia in token.TrailingTrivia)
            {
                Assert.True(trivia.Span.Start == position, $"{name}: a gap at {position}.");
                rebuilt.Append(source.Slice(trivia.Span));
                position = trivia.Span.End;
            }
        }

        Assert.Equal(text, rebuilt.ToString());

        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.NotNull(diagnostic.Span);
            Assert.True(
                diagnostic.Span!.Value.End <= text.Length,
                $"{name}: {diagnostic.Code} points past the end of the text.");
        }
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

    private static readonly ImmutableArray<char> Interesting =
        [.. "\"#=.-+*/%@,()\n\r\t 0123456789eE_kWmsK"];
}
