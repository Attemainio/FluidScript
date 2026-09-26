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
/// <remarks>
/// Language 2's statements are read by <see cref="ParseLanguage2"/> (<c>LineParser.Language2*.cs</c>); the other
/// partials read what every statement shares -- names, endpoints, expressions and values.
/// </remarks>
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
