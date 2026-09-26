using System.Text;

using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Syntax.Printing;

/// <summary>
/// The formatter's layout (<c>17</c>): what it indents, spaces and aligns, what it never touches, and that formatting
/// twice is formatting once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormatterTests
{
    private const string V2 = "fluidscript 2\n";

    [Fact]
    public void ItIsIdempotentOverTheCorpus()
    {
        // 17's acceptance row: Format(Format(x)) == Format(x), over every sample and every block.
        foreach (var (name, text, _) in ScriptCorpus.All())
        {
            var once = Formatter.FormatText(text);
            var twice = Formatter.FormatText(once);

            Assert.True(string.Equals(once, twice, StringComparison.Ordinal), $"{name}: formatting twice differs from formatting once");
            Assert.Empty(Formatter.Format(new SourceText(once)));
        }
    }

    [Fact]
    public void ItChangesNoTokenAndNoCommentOverTheCorpus()
    {
        foreach (var (name, text, _) in ScriptCorpus.All())
        {
            var formatted = Formatter.FormatText(text);
            var before = Lexer.Lex(new SourceText(text));
            var after = Lexer.Lex(new SourceText(formatted));

            Assert.True(
                before.Tokens.Select(static t => (t.Kind, t.Text)).SequenceEqual(after.Tokens.Select(static t => (t.Kind, t.Text))),
                $"{name}: formatting changed a token");
            Assert.Equal(Comments(before, text), Comments(after, formatted));
        }

        static IEnumerable<string> Comments(LexResult lexed, string text) =>
            lexed.Tokens.SelectMany(static t => t.LeadingTrivia.Concat(t.TrailingTrivia))
                .Where(static t => t.Kind == TriviaKind.Comment)
                .Select(t => text.Substring(t.Span.Start, t.Span.Length));
    }

    [Fact]
    public void ItLeavesTheTreeAsItWasOverTheCorpus()
    {
        // Indentation is language 2's block structure: the depth written must be the depth the parse read.
        foreach (var (name, text, _) in ScriptCorpus.All())
        {
            Assert.True(
                string.Equals(Shape(text), Shape(Formatter.FormatText(text)), StringComparison.Ordinal),
                $"{name}: the formatted file parses to another tree");
        }

        static string Shape(string text)
        {
            var parse = FluidScript2Parser.Parse(new SourceText(text));
            var shape = new StringBuilder();

            void Walk(StatementSyntax statement, int depth)
            {
                shape.Append(depth).Append(' ').Append(statement.GetType().Name).Append(' ')
                    .AppendJoin("|", statement.Tokens.Select(static t => t.Text)).Append('\n');
                if (statement is BlockSyntax block)
                {
                    foreach (var child in block.Body)
                    {
                        Walk(child, depth + 1);
                    }
                }
            }

            foreach (var statement in parse.Root.Statements)
            {
                Walk(statement, 0);
            }

            return shape.AppendJoin(",", parse.Diagnostics.Select(static d => d.Code)).ToString();
        }
    }

    [Fact]
    public void ItIndentsTwoSpacesForEachBlockALineSitsIn()
    {
        const string Text = V2 + "circuit \"c\":\n     fluid = water\n     HX1  exchanger:\n         primary.in.t = 70 C\n";

        Assert.Equal(
            V2 + "circuit \"c\":\n  fluid = water\n  HX1  exchanger:\n    primary.in.t = 70 C\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItSetsADeclarationsFieldsTwoSpacesApartAndOneSpaceAroundEachEquals()
    {
        // A value runs to the next `name =`, so one space would run the pairs together to the eye.
        const string Text = V2 + "circuit \"c\":\n  HE1 load power=30 kW   in.t  =  20 C\n  PE1 pressure_sensor at   N1\n";

        Assert.Equal(
            V2 + "circuit \"c\":\n  HE1  load  power = 30 kW  in.t = 20 C\n  PE1  pressure_sensor  at N1\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItSetsAPipesFirstPropertyThreeSpacesPastItsLink()
    {
        const string Text = V2 + "circuit \"c\":\n  P1  -  HE1 12 m DN25\n  HE1 - P1\n";

        Assert.Equal(
            V2 + "circuit \"c\":\n  P1 - HE1   12 m  DN25\n  HE1 - P1\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItAlignsTrailingCommentsWithinARunAndNotAcrossABlankLine()
    {
        const string Text = V2 + "circuit \"c\":\n  HE1  load  power = 30 kW   # coil\n  TV1  valve3 # valve\n\n  PU1  pump # pump\n";

        Assert.Equal(
            V2 + "circuit \"c\":\n  HE1  load  power = 30 kW  # coil\n  TV1  valve3               # valve\n\n  PU1  pump  # pump\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItPadsConsecutiveLetsSoTheEqualsSignsLineUp()
    {
        const string Text = V2 + "let dT = 30 K\nlet margin=2 kW\nlet Q   =   30 kW\n";

        Assert.Equal(V2 + "let dT     = 30 K\nlet margin = 2 kW\nlet Q      = 30 kW\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItPadsConsecutiveSettingsOfOneBlockSoTheEqualsSignsLineUp()
    {
        // A declaration between them ends the group; a nested block's settings are a group of their own.
        const string Text = V2 + "circuit \"c\":\n  fluid = water\n  number=100\n  P1  pump\n  role = district\n  style:\n    colour = crimson\n    width=2\n";

        Assert.Equal(
            V2 + "circuit \"c\":\n  fluid  = water\n  number = 100\n  P1  pump\n  role = district\n  style:\n    colour = crimson\n    width  = 2\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItKeepsTheWritersSpacingInsideAnExpressionCollapsedToOne()
    {
        const string Text = V2 + "let a = Q/(cp*dT)\nlet b = Q  /  ( cp * dT )\nlet c = max( Q ,24 kW )\nlet d = [ 60 ,50 ] C\n";

        Assert.Equal(
            V2 + "let a = Q/(cp*dT)\nlet b = Q / (cp * dT)\nlet c = max(Q, 24 kW)\nlet d = [60, 50] C\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItLeavesBlankLinesFullLineCommentsAndCurveRowsExactlyAsWritten()
    {
        const string Text = V2 + "# a comment   with   spacing\n\n\ncurve heating:   outdoor   extrapolated\n  -26   50\n    0   30\n\nlet x=1\n";

        Assert.Equal(
            V2 + "# a comment   with   spacing\n\n\ncurve heating: outdoor extrapolated\n  -26   50\n    0   30\n\nlet x = 1\n",
            Formatter.FormatText(Text));
    }

    [Fact]
    public void ItReturnsOneEditPerChangedLineAtThatLinesSpan()
    {
        const string Text = V2 + "circuit \"c\":\n  P1  pump\n  PU2   pump\n";

        var edit = Assert.Single(Formatter.Format(new SourceText(Text)));

        Assert.Equal(Text.IndexOf("  PU2", StringComparison.Ordinal), edit.Span.Start);
        Assert.Equal("  PU2   pump".Length, edit.Span.Length);
        Assert.Equal("  PU2  pump", edit.NewText);
    }

    [Fact]
    public void ItKeepsAByteOrderMark()
    {
        Assert.Equal(
            "\uFEFF" + V2 + "circuit \"c\":\n  P1  pump\n",
            Formatter.FormatText("\uFEFF" + V2 + "circuit \"c\":\n    P1 pump\n"));
    }

    [Fact]
    public void ItFormatsADraftWithNoVersionLineAsLanguageTwo()
    {
        Assert.Equal("circuit \"c\":\n  P1  pump\n", Formatter.FormatText("circuit \"c\":\n    P1 pump\n"));
    }

    [Theory]
    [InlineData("fluidscript 1\nHE1 heat_exchanger  power=30\n")]
    [InlineData("fluidscript 1\nfluidscript 2\ncircuit \"c\":\n    P1   pump\n")]
    [InlineData("fluidscript 3\ncircuit \"c\":\n    P1   pump\n")]
    public void ItLeavesAFileOfAnotherMajorAsWritten(string text)
    {
        // Its layout would say something else in another language: in language 1 indentation meant nothing.
        Assert.Empty(Formatter.Format(new SourceText(text)));
    }
}
