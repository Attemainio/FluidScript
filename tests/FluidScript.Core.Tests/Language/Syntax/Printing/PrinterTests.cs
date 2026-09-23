using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// <c>plan/10-language/17-formatting-and-round-trip.md</c>'s invariant 1, and the standing fuzz behind
/// it: <c>Print(Parse(x)) == x</c> byte for byte, for every input.
/// </summary>
/// <remarks>
/// The printer is a few lines, and that is the result rather than the shortcut — losslessness is a
/// property of the lexer and the AST, and the printer only reveals whether they have it. These tests
/// are the reason <c>08</c> schedules it fifth: a trivia model that loses information is a cheap AST
/// change here and a change underneath the binder, the registry and every golden file later.
/// </remarks>
public sealed class PrinterTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void TheCorpusRoundTrips()
    {
        // One case rather than one per script, deliberately: LosslessnessTests already runs the corpus
        // file by file at the lexer, and the failure message here names the script, so a second
        // per-file theory buys isolation the assertion already gives and costs the unit tier's budget.
        foreach (var script in ScriptCorpus.All())
        {
            AssertRoundTrips(script.Name, script.Text);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TextThatIsNotAScriptRoundTripsToo()
    {
        foreach (var text in ScriptCorpus.Adversarial)
        {
            AssertRoundTrips($"adversarial {Escape(text)}", text);
        }
    }

    [Fact]
    [Trait("Category", "Property")]
    public void EveryMutationRoundTrips()
    {
        // The standing round-trip fuzz (`08`, P2.5). A malformed script is the normal state of one
        // being edited, and invariant 1 covers malformed input explicitly: a tree that dropped the
        // half-typed token would print a file the user did not write.
        var seen = 0;

        foreach (var text in ScriptCorpus.Mutations(10_000, seed: 20260901))
        {
            AssertRoundTrips($"mutation {seen++}", text);
        }

        Assert.Equal(10_000, seen);
    }

    [Fact]
    [Trait("Category", "Property")]
    public void DeletingAnyOneCharacterOfASampleStillRoundTrips()
    {
        foreach (var sample in ScriptCorpus.Samples())
        {
            for (var i = 0; i < sample.Text.Length; i++)
            {
                AssertRoundTrips($"{sample.Name} less character {i}", sample.Text.Remove(i, 1));
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrintingIsIdempotent()
    {
        // 17's second criterion. It follows from invariant 1, and it is worth its own test because it
        // is the one that fails loudly if the printer ever normalises something: a printer that tidies
        // is stable on its second pass and wrong on its first.
        foreach (var sample in ScriptCorpus.All())
        {
            var once = SyntaxPrinter.Print(FluidScriptParser.Parse(new SourceText(sample.Text)));
            var twice = SyntaxPrinter.Print(FluidScriptParser.Parse(new SourceText(once)));

            Assert.Equal(once, twice);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NothingIsTidiedOnTheWayOut()
    {
        // Every shape a formatter would be tempted to fix, in one script: run-on spacing, a comment
        // column, a blank line, a unit written both ways, trailing whitespace, and no final newline.
        const string Ugly = "fluidscript 1\n"
            + "let   x   =   1     # three spaces, on purpose\n"
            + "\n"
            + "PU1 pump power=30kW    # against the number\n"
            + "PU2 pump power=30 kW   # and spaced from it   \n"
            + "circuit demo";

        var result = FluidScriptParser.Parse(new SourceText(Ugly));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(Ugly, SyntaxPrinter.Print(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AResolvedCircuitNumberIsNotPrintedBack()
    {
        // 17's invariant 9, asserted now so it is a standing test rather than a discovery in P2.7.
        // The binder gives every circuit a number; a printer that read the bound model would rewrite
        // `circuit coolingLoop` as `circuit coolingLoop 100` the first time anything touched the file.
        // Printing from the syntax tree is what makes that impossible, and this is that test with the
        // tree in the state the binder will find it.
        const string Text = "fluidscript 1\ncircuit coolingLoop\n";

        var result = FluidScriptParser.Parse(new SourceText(Text));
        var header = Assert.IsType<CircuitHeaderSyntax>(result.Root.Statements[1]);

        Assert.Null(header.Number);
        Assert.Equal(Text, SyntaxPrinter.Print(result));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANodePrintsItsOwnFullSpanAndNothingElse()
    {
        const string Text = "fluidscript 1\n"
            + "    HE1 heat_exchanger power=30   # the exchanger\n"
            + "PU1 pump\n";

        var result = FluidScriptParser.Parse(new SourceText(Text));
        var declaration = result.Root.Statements[1];

        // Leading indentation and the trailing comment belong to the statement; the line break that
        // ends the line does not, because it opens the next statement's leading trivia.
        Assert.Equal(
            "\n    HE1 heat_exchanger power=30   # the exchanger",
            SyntaxPrinter.Print(result.Source, declaration));

        Assert.Equal(
            result.Source.ToString(declaration.FullSpan),
            SyntaxPrinter.Print(result.Source, declaration));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StatementsTileTheFileWithNoGapAndNoOverlap()
    {
        // What write-back depends on and the round trip alone does not prove: statements' full spans
        // abut, so an edit computed against one statement cannot disturb its neighbours. A tree could
        // print correctly with two statements claiming the same characters.
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = FluidScriptParser.Parse(new SourceText(sample.Text));
            var position = 0;

            foreach (var statement in result.Root.Statements)
            {
                var span = statement.FullSpan;

                Assert.Equal(position, span.Start);
                Assert.Equal(
                    result.Source.ToString(span),
                    SyntaxPrinter.Print(result.Source, statement));

                position = span.End;
            }

            // Whatever is left over is the end-of-file token's leading trivia -- the blank lines and
            // comments a file ends in, owned by a token with no text of its own (trivia rule 4).
            Assert.Equal(string.Empty, result.Root.EndOfFile.Text);
            Assert.Equal(sample.Text.Length, result.Root.FullSpan.End);
            Assert.True(
                position <= sample.Text.Length,
                $"{sample.Name}: statements claim {position} of {sample.Text.Length} characters.");
        }
    }

    private static void AssertRoundTrips(string name, string text)
    {
        var result = FluidScriptParser.Parse(new SourceText(text));
        var printed = SyntaxPrinter.Print(result);

        Assert.True(
            text == printed,
            $"{name}: round trip differs.\n  in:  {Escape(text)}\n  out: {Escape(printed)}");

        Assert.Equal(new TextSpan(0, text.Length), result.Root.FullSpan);
    }

    private static string Escape(string text) =>
        text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
