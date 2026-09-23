using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>
/// Parses one line into one statement. A line that cannot be read becomes a
/// <see cref="MalformedStatementSyntax"/> holding its tokens, so the printer can reproduce it and every
/// other line is unaffected.
/// </summary>
internal sealed partial class LineParser(
    SourceText source,
    ImmutableArray<Token> tokens,
    ImmutableArray<Diagnostic>.Builder diagnostics)
{
    /// <summary>How deep an expression may nest before the line is read as malformed.</summary>
    /// <remarks>
    /// Every nesting level -- a parenthesis, a call argument, a unary minus -- is a few stack frames of
    /// recursive descent, and the parser runs on the host once per keystroke over whatever the line
    /// holds. A stack overflow cannot be caught, so without a bound one line of a few thousand `(`
    /// takes the process down instead of returning a diagnostic. Sixty-four is far beyond any
    /// expression a script states and far below the stack.
    /// </remarks>
    private const int MaxExpressionDepth = 64;

    private int _index;
    private int _depth;

    private Token? Current => _index < tokens.Length ? tokens[_index] : null;

    private bool AtEnd => _index >= tokens.Length;

    private TextSpan LineSpan => TextSpan.FromBounds(tokens[0].Span.Start, tokens[^1].Span.End);

    public StatementSyntax Parse(StatementKind kind, FluidScriptParser.ScriptState state)
    {
        // A circuit header ends whatever section the previous circuit was in and opens a new
        // declaration section (D-52), so it is checked against no section at all.
        if (kind == StatementKind.Circuit)
        {
            state.BeginCircuit();
        }
        else
        {
            CheckSection(kind, state);
        }
        var explained = diagnostics.Count;

        var statement = kind switch
        {
            StatementKind.Version => ParseVersion(),
            StatementKind.Project => ParseProject(state),
            StatementKind.Spacing => ParseSpacing(state),
            StatementKind.Circuit => ParseCircuit(),
            StatementKind.Fluid => ParseFluid(),
            StatementKind.Catalog => ParseCatalog(),
            StatementKind.Style => ParseStyle(),
            StatementKind.Show => ParseShow(),
            StatementKind.Let => ParseLet(),
            StatementKind.ConnectionsHeader => ParseSectionHeader(state, connections: true),
            StatementKind.ScheduleHeader => ParseSectionHeader(state, connections: false),
            StatementKind.Attachment => ParseAttachment(state),
            StatementKind.Control => ParseControl(),
            StatementKind.Connection => ParseConnection(),
            StatementKind.Disturbance => ParseDisturbance(),
            StatementKind.Declaration => ParseDeclaration(),
            StatementKind.CurveHeader => ParseCurveHeader(state),
            StatementKind.CurveRow => ParseCurveRow(),
            StatementKind.Design => ParseDesign(state),
            StatementKind.Scenarios => ParseScenarios(state),
            _ => Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan),
        };

        // A statement that parsed without consuming its line has left text the line has no place for.
        // Every token has to stay in the tree whatever else happens, so the whole line becomes one
        // malformed statement rather than a node that silently drops the remainder -- which is the bug
        // the losslessness assertion caught, and the reason it runs over the tree and not the tokens.
        if (!AtEnd)
        {
            return ExtraText();
        }

        // A line marked wrong with no sentence beside it is worse than no marking at all: the editor
        // shows a squiggle the user cannot act on. If nothing more specific explained this line, say
        // the general thing.
        if (statement is MalformedStatementSyntax && diagnostics.Count == explained)
        {
            Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return statement;
    }

    // ---- section legality -------------------------------------------------------------------

    private void CheckSection(StatementKind kind, FluidScriptParser.ScriptState state)
    {
        var section = state.Section;

        switch (kind)
        {
            case StatementKind.Connection when section != ScriptSection.Connections:
                Report(ParserDiagnostics.ConnectionOutsideSection, LineSpan);
                return;

            // A `schedule` header is legal in both of the sections that can precede it, and a second
            // one is FS1101 from the header parser rather than FS1103, so neither header is in the
            // list below on its own account (D-56).
            // A curve row belongs to exactly one section and is a statement nowhere else, so it gets
            // its own code rather than the generic wrong-section one: the message can say what the
            // line is, instead of only where it may not be.
            case StatementKind.CurveRow when section != ScriptSection.Curve:
                Report(ParserDiagnostics.CurveRowOutsideSection, LineSpan);
                return;

            case StatementKind.Declaration or StatementKind.Attachment or StatementKind.Control
                when section is ScriptSection.Schedule or ScriptSection.Curve:
            case StatementKind.ConnectionsHeader or StatementKind.ScheduleHeader
                when section == ScriptSection.Curve:
            case StatementKind.ConnectionsHeader when section == ScriptSection.Schedule:
            case StatementKind.Version or StatementKind.Project or StatementKind.Spacing
                or StatementKind.Fluid or StatementKind.Catalog or StatementKind.Style
                or StatementKind.Show or StatementKind.Let or StatementKind.Design
                or StatementKind.Scenarios
                when section != ScriptSection.Declaration:
                Report(
                    ParserDiagnostics.StatementInWrongSection,
                    LineSpan,
                    new DiagnosticArgument("statement", NameOf(kind)),
                    new DiagnosticArgument("section", SectionName(section)));
                return;

            default:
                return;
        }
    }

    private static string SectionName(ScriptSection section) => section switch
    {
        ScriptSection.Schedule => "schedule",
        ScriptSection.Curve => "curve",
        _ => "connections",
    };

    private static string NameOf(StatementKind kind) => kind switch
    {
        StatementKind.Version => "version line",
        StatementKind.Project => "project directive",
        StatementKind.Spacing => "spacing directive",
        StatementKind.Fluid => "fluid directive",
        StatementKind.Catalog => "catalog directive",
        StatementKind.Style => "style directive",
        StatementKind.Show => "show directive",
        StatementKind.Let => "let binding",
        StatementKind.ConnectionsHeader => "connections line",
        StatementKind.ScheduleHeader => "schedule line",
        StatementKind.Attachment => "inlet or outlet line",
        StatementKind.Control => "control line",
        StatementKind.Declaration => "component declaration",
        StatementKind.CurveHeader => "curve line",
        StatementKind.CurveRow => "curve row",
        StatementKind.Design => "design directive",
        StatementKind.Scenarios => "scenarios directive",
        _ => "statement",
    };

    // ---- primitives -------------------------------------------------------------------------

    private Token Advance() => tokens[_index++];

    private MalformedStatementSyntax ExtraText()
    {
        var extra = TextSpan.FromBounds(tokens[_index].Span.Start, tokens[^1].Span.End);
        Report(
            ParserDiagnostics.ExtraTextOnLine,
            extra,
            new DiagnosticArgument("extra", source.ToString(extra)));
        return Malformed();
    }

    private MalformedStatementSyntax Fail(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        params ReadOnlySpan<DiagnosticArgument> arguments)
    {
        Report(descriptor, span, arguments);
        return Malformed();
    }

    private MalformedStatementSyntax Malformed()
    {
        _index = tokens.Length;
        return new MalformedStatementSyntax(tokens);
    }

    private void Report(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        params ReadOnlySpan<DiagnosticArgument> arguments) =>
        diagnostics.Add(Diagnostic.Create(descriptor, span, arguments));
}
