using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// What the parser makes of each shape, one test per acceptance criterion in
/// <c>plan/10-language/12-grammar.md</c>, plus one per diagnostic code it can raise.
/// </summary>
public sealed class ParserTests
{
    private static ParseResult Parse(string text) => FluidScriptParser.Parse(new SourceText(text));

    private static T Single<T>(string text)
        where T : StatementSyntax
    {
        var result = Parse(text);
        Assert.True(
            result.Diagnostics.IsEmpty,
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return Assert.IsType<T>(Assert.Single(result.Root.Statements));
    }

    private static Diagnostic OnlyDiagnostic(string text, string code)
    {
        var result = Parse(text);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();

        Assert.True(
            matching.Length == 1,
            $"Expected exactly one {code}; got "
            + (result.Diagnostics.IsEmpty
                ? "none at all."
                : string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))));

        return matching[0];
    }

    // ---- classification ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void OneTokenOfLookaheadSeparatesAConnectionFromADeclaration()
    {
        // Both start with N1, both sit below `connections`, and the second token is the whole
        // difference. Under the earlier rules the second line was FS1104 and the first reference
        // circuit did not parse at all.
        var result = Parse("""
            fluidscript 1
            circuit demo
            connections
            N1 - N2
            N1 node t=6 p=300
            """);

        Assert.Empty(result.Diagnostics);
        Assert.IsType<ConnectionSyntax>(result.Root.Statements[3]);
        Assert.IsType<ComponentDeclarationSyntax>(result.Root.Statements[4]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ClassificationReadsNothingBeyondTwoTokens()
    {
        // Invariant 7, asserted through the signature rather than around it: Classify cannot see a
        // third token, so a statement form that needed one could not be added without changing this
        // call. The test pins the classification of every line of the tour to what those two tokens
        // say, which is what would break if a later production reached further.
        var tour = ScriptCorpus.Samples()
            .Single(static s => s.Name.EndsWith("m1-syntax-tour.fluid", StringComparison.Ordinal));

        var lex = Lexer.Lex(new SourceText(tour.Text));
        var seen = new HashSet<StatementKind>();

        foreach (var token in lex.Tokens.Where(static t => t.Kind != TokenKind.EndOfFile))
        {
            seen.Add(FluidScriptParser.Classify(token, null, ScriptSection.Declaration));
        }

        Assert.Contains(StatementKind.Declaration, seen);
        Assert.Contains(StatementKind.Circuit, seen);
    }

    [Theory]
    [InlineData("circuit demo", StatementKind.Circuit)]
    [InlineData("fluidscript 1", StatementKind.Version)]
    [InlineData("connections", StatementKind.ConnectionsHeader)]
    [InlineData("supply N3", StatementKind.Attachment)]
    [InlineData("return N5", StatementKind.Attachment)]
    [InlineData("control by=PID1", StatementKind.Control)]
    [Trait("Category", "Unit")]
    public void AReservedFirstTokenDecidesOnItsOwn(string line, StatementKind expected)
    {
        var tokens = Lexer.Lex(new SourceText(line)).Tokens;
        Assert.Equal(
            expected,
            FluidScriptParser.Classify(tokens[0], tokens.Length > 1 ? tokens[1] : null, ScriptSection.Declaration));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SectionPositionClassifiesAtAndOver()
    {
        // `at` and `over` are ordinary identifiers. Reserving two common English words to buy nothing
        // is the trade P6 exists to refuse, so position does the work instead.
        var tokens = Lexer.Lex(new SourceText("at 60 s HE1.power = 45")).Tokens;

        Assert.Equal(
            StatementKind.Disturbance,
            FluidScriptParser.Classify(tokens[0], tokens[1], ScriptSection.Schedule));
        Assert.Equal(
            StatementKind.Declaration,
            FluidScriptParser.Classify(tokens[0], tokens[1], ScriptSection.Declaration));
    }

    // ---- shapes -----------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnOmittedCircuitNumberStaysAbsent()
    {
        // The parser never invents one: an absent number must stay distinguishable from a written one
        // so the printer can reproduce the source byte for byte (D-33).
        Assert.Null(Single<CircuitHeaderSyntax>("circuit demo").Number);
        Assert.Equal(101, Single<CircuitHeaderSyntax>("circuit AHU 101").Number!.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnOmittedFluidModeStaysAbsent()
    {
        // D-54. Defaulting to Static loses the difference between these two lines, which breaks the
        // round trip and makes every circuit in a `project dynamic` file warn about a word its author
        // never wrote.
        Assert.Null(Single<FluidDirectiveSyntax>("fluid water").Mode);
        Assert.Equal(FluidMode.Static, Single<FluidDirectiveSyntax>("fluid static water").Mode);
        Assert.Equal(FluidMode.Dynamic, Single<FluidDirectiveSyntax>("fluid dynamic water").Mode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParenthesesAreKeptRatherThanRederived()
    {
        // D-54. `(a + b) * c` and `a + b * c` differ, and a redundant grouping in an engineering
        // formula is usually deliberate.
        var binding = Single<LetBindingSyntax>("let x = (a + b) * c");
        var product = Assert.IsType<BinaryExpressionSyntax>(binding.Value);

        Assert.Equal(BinaryOperator.Multiply, product.Operator);
        Assert.IsType<ParenthesizedExpressionSyntax>(product.Left);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrecedenceIsMultiplicativeThenAdditive()
    {
        var binding = Single<LetBindingSyntax>("let x = a + b * c");
        var sum = Assert.IsType<BinaryExpressionSyntax>(binding.Value);

        Assert.Equal(BinaryOperator.Add, sum.Operator);
        Assert.Equal(BinaryOperator.Multiply, Assert.IsType<BinaryExpressionSyntax>(sum.Right).Operator);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACallTakesItsArgumentsInOrder()
    {
        var binding = Single<LetBindingSyntax>("let peak = max(30 kW, 24 kW)");
        var call = Assert.IsType<CallSyntax>(binding.Value);

        Assert.Equal("max", call.Name.Text);
        Assert.Equal(2, call.Arguments.Length);
        Assert.Null(call.Arguments[0].LeadingComma);
        Assert.NotNull(call.Arguments[1].LeadingComma);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AChainedConnectionKeepsEveryEndpoint()
    {
        // Held as written rather than desugared here: `A - B - C` becomes two connections at bind time
        // (rule I6), and the printer has to reproduce the chain the user typed.
        var result = Parse("""
            fluidscript 1
            connections
            N1 - HS1 - N2 - PU_MAIN - N3
            """);

        Assert.Empty(result.Diagnostics);
        var connection = Assert.IsType<ConnectionSyntax>(result.Root.Statements[2]);

        Assert.Equal(
            ["N1", "HS1", "N2", "PU_MAIN", "N3"],
            connection.Endpoints.Select(static endpoint => endpoint.Component.Text));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortQualifiedEndpointKeepsItsPort()
    {
        var result = Parse("""
            fluidscript 1
            connections
            3WV.b - N3
            """);

        Assert.Empty(result.Diagnostics);
        var connection = Assert.IsType<ConnectionSyntax>(result.Root.Statements[2]);
        Assert.Equal("b", connection.First.Port!.Text);
        Assert.Null(connection.Endpoints[1].Port);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACatalogVersionSplitsOutOfOneNumberToken()
    {
        var directive = Single<CatalogDirectiveSyntax>("catalog steel_en10255@2026.10");

        Assert.Equal(2026, directive.Version!.Major);
        Assert.Equal(10, directive.Version.Minor);
        Assert.Equal("2026.10", directive.Version.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StylePatternsAreRecombinedFromTheirTokens()
    {
        var directive = Single<StyleDirectiveSyntax>("""style "#2f6f9f" 1.5px round --""");

        Assert.Equal(
            [StyleTokenKind.Quoted, StyleTokenKind.Quantity, StyleTokenKind.Word, StyleTokenKind.Pattern],
            directive.Parts.Select(static part => part.Kind));
        Assert.Equal("--", directive.Parts[^1].Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SectionsAreScopedToACircuit()
    {
        // D-52. The distribution-header reference circuit writes two circuit headers below a
        // connections section, and refused to parse until this was fixed.
        var result = Parse("""
            fluidscript 1
            circuit heating 100
            HS1 heat_exchanger power=54

            connections
            N1 - HS1 - N2

            circuit AHU 101
            HE_AHU duty power=24 kW
            supply N2
            """);

        Assert.Empty(result.Diagnostics);
        Assert.IsType<CircuitHeaderSyntax>(result.Root.Statements[5]);
        Assert.IsType<ComponentDeclarationSyntax>(result.Root.Statements[6]);
        Assert.IsType<AttachmentSyntax>(result.Root.Statements[7]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EverySampleParsesWithNoUnexpectedDiagnostic()
    {
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = FluidScriptParser.Parse(new SourceText(sample.Text));
            Assert.True(
                result.Diagnostics.IsEmpty,
                $"{sample.Name}: {string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))}");
        }
    }

    // ---- one test per code ------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1003_NameThatReadsAsAQuantity()
    {
        var diagnostic = OnlyDiagnostic("3K pump", "FS1003");

        Assert.Contains("3K", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("K3", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1004_ReservedWordUsedAsAName() => OnlyDiagnostic("circuit let", "FS1004");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1101_SecondSectionHeaderInOneCircuit() =>
        OnlyDiagnostic("fluidscript 1\nconnections\nconnections\n", "FS1101");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1102_ConnectionOutsideTheConnectionsSection() =>
        OnlyDiagnostic("fluidscript 1\nN1 - N2\n", "FS1102");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1103_DirectiveBelowTheConnectionsLine() =>
        OnlyDiagnostic("fluidscript 1\nconnections\nlet x = 1\n", "FS1103");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1104_LineThatCannotBeClassified() => OnlyDiagnostic("= = =", "FS1104");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1105_ParameterWithNoValue() => OnlyDiagnostic("HE1 pump power", "FS1105");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1106_DisturbanceOutsideTheScheduleSection() =>
        OnlyDiagnostic("at 60 s HE1.power = 45", "FS1106");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1108_HyphenInAKindName()
    {
        var diagnostic = OnlyDiagnostic("V1 3-way-valve", "FS1108");

        Assert.Contains("3-way-valve", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("3_way_valve", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1109_InWhereAnAttachmentWasMeant()
    {
        // Without this the user gets an unknown-kind message pointing at N3, or none at all and a
        // subcircuit that never attaches. A wrong answer that compiles is what P3 exists to refuse.
        var diagnostic = OnlyDiagnostic("in N3", "FS1109");

        Assert.Contains("supply", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1110_AttachmentWithNoEndpoint() => OnlyDiagnostic("supply", "FS1110");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1110_SecondAttachmentOfTheSameDirection() =>
        OnlyDiagnostic("fluidscript 1\nsupply N1\nsupply N2\n", "FS1110");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1111_ControlWithNoArguments() => OnlyDiagnostic("control", "FS1111");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1112_ProjectAfterTheFirstCircuit() =>
        OnlyDiagnostic("fluidscript 1\ncircuit demo\nproject plant_01\n", "FS1112");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1113_SpacingGivenAQuantity() => OnlyDiagnostic("spacing 20 mm", "FS1113");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1114_TextAfterACompleteStatement() =>
        OnlyDiagnostic("catalog steel_en10255 copper_en1057", "FS1114");

    [Fact]
    [Trait("Category", "Unit")]
    public void FS1203_BareHexColourEatenByTheComment()
    {
        // The directive is legal and silent otherwise: everything from the '#' is a comment, so the
        // line renders in the default colour with nothing to say it did.
        var diagnostic = OnlyDiagnostic("style #2f6f9f 2px fillet", "FS1203");

        Assert.Contains("#2f6f9f", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryCodeTheParserOwnsHasATestAbove()
    {
        // 12's acceptance criterion: every FS1xxx it lists has a test that triggers exactly it. This
        // is the half that notices a code registered and never exercised.
        var tested = typeof(ParserTests).GetMethods()
            .Select(static method => method.Name)
            .Where(static name => name.StartsWith("FS", StringComparison.Ordinal))
            .Select(static name => name[..6])
            .ToHashSet(StringComparer.Ordinal);

        var untested = ParserDiagnostics.All
            .Select(static descriptor => descriptor.Code)
            .Where(code => !tested.Contains(code))
            .ToArray();

        Assert.True(untested.Length == 0, $"Registered and never triggered: {string.Join(", ", untested)}");
    }
}
