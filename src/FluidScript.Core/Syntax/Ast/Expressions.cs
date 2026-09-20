using System.Collections.Immutable;

namespace FluidScript.Core.Syntax.Ast;

/// <summary>A bare identifier: a component name, a kind name, a parameter name, a circuit name.</summary>
/// <param name="Token">The word as written.</param>
public sealed record IdentifierSyntax(Token Token) : SyntaxNode
{
    /// <summary>Gets the spelling exactly as written.</summary>
    /// <value>
    /// Normalisation for kind and parameter resolution happens at bind time (<c>D-15</c>) and never
    /// rewrites this.
    /// </value>
    public string Text => Token.Text;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}

/// <summary>A number with no unit symbol.</summary>
/// <param name="Token">The literal as written.</param>
public sealed record NumberLiteralSyntax(Token Token) : ExpressionSyntax
{
    /// <summary>Gets the value.</summary>
    public double Value => Token.Value ?? double.NaN;

    /// <summary>Gets the source spelling.</summary>
    /// <value>
    /// <c>1.50</c>, <c>1.5</c> and <c>15e-1</c> are one value and three different strings, and the
    /// printer reproduces the one that was written (<c>R-25</c>).
    /// </value>
    public string Text => Token.Text;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}

/// <summary>A number immediately or whitespace-separated from a unit symbol.</summary>
/// <param name="Token">The literal as written, including any space before the unit.</param>
public sealed record QuantityLiteralSyntax(Token Token) : ExpressionSyntax
{
    /// <summary>Gets the number as written, before any conversion.</summary>
    /// <value>
    /// <strong>Not SI.</strong> A unit may denote either of two dimensions — <c>kPa</c> is both a
    /// pressure and a pressure difference — and which one depends on the parameter it is read into, so
    /// the conversion happens at bind time.
    /// </value>
    public double Value => Token.Value ?? double.NaN;

    /// <summary>Gets the whole literal as written.</summary>
    public string Text => Token.Text;

    /// <summary>Gets the unit symbol as written, not its canonical spelling.</summary>
    public string Unit => Token.Unit ?? string.Empty;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}

/// <summary>A double-quoted string. Cannot span a newline (invariant 6).</summary>
/// <param name="Token">The literal as written, quotes included.</param>
public sealed record StringLiteralSyntax(Token Token) : ExpressionSyntax
{
    /// <summary>Gets the content, without the quotes.</summary>
    /// <value>There are no escape sequences in v1, so this is the source slice verbatim.</value>
    public string Value => Token.StringValue ?? string.Empty;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}

/// <summary>A bracketed whole number after a name: the <c>[2]</c> of <c>in[2]</c> (<c>D-120</c>).</summary>
/// <param name="OpenBracket">The <c>[</c>.</param>
/// <param name="Number">The index, a whole number with no unit.</param>
/// <param name="CloseBracket">The <c>]</c>.</param>
/// <remarks>
/// The parser admits it only when the three tokens and the name before them touch: <c>in [2]</c>
/// and <c>in[ 2 ]</c> are <c>FS1119</c>, because a port name is one word to the eye and the printer
/// would otherwise have to decide whether the space was meant.
/// </remarks>
public sealed record IndexSyntax(Token OpenBracket, Token Number, Token CloseBracket) : SyntaxNode
{
    /// <summary>Gets the index.</summary>
    /// <value>A positive whole number; the parser refused anything else.</value>
    public int Value => (int)(Number.Value ?? 0);

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OpenBracket, Number, CloseBracket];
}

/// <summary>A name with an optional bracketed index: <c>in</c>, <c>in[2]</c>, <c>layer[3]</c>.</summary>
/// <param name="Name">The word.</param>
/// <param name="Index">The index after it, or <see langword="null"/> when there is none.</param>
/// <remarks>
/// A port family and its member are the same shape to the parser; whether <c>in[2]</c> names a port
/// the kind has is the binder's question. <see cref="Text"/> is the registry's spelling of what was
/// written, which is what binding matches on, and it never carries whitespace because the grammar
/// admits none inside an indexed name.
/// </remarks>
public sealed record IndexedNameSyntax(IdentifierSyntax Name, IndexSyntax? Index) : SyntaxNode
{
    /// <summary>Gets the name as written, index included: <c>in[2]</c> for <c>in[2]</c>, <c>in</c> for <c>in</c>.</summary>
    public string Text =>
        Index is null
            ? Name.Text
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name.Text}[{Index.Value}]");

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Index is null ? Name.Tokens : [.. Name.Tokens, .. Index.Tokens];
}

/// <summary>One <c>.name</c> step of a qualified reference.</summary>
/// <param name="Dot">The separator.</param>
/// <param name="Name">The name after it, with its index when it has one (<c>HX1.in[2].t</c>).</param>
public sealed record QualifiedNamePart(Token Dot, IndexedNameSyntax Name) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Dot, .. Name.Tokens];
}

/// <summary>A parameter's name: a word, an indexed port, or a port's quantity — <c>power</c>, <c>in[2]</c>, <c>in[2].t</c> (<c>D-120</c>).</summary>
/// <param name="Head">The first name, with its index when it has one.</param>
/// <param name="Parts">The <c>.name</c> steps after it. Empty for a plain parameter.</param>
/// <remarks>
/// The parser accepts any depth; the binder decides what a kind admits. One step is the whole of
/// <c>D-120</c> (<c>in[2].t</c>), and a deeper name reaches the registry as an unknown parameter,
/// which is <c>FS1503</c> with the accepted list — the parser has no better message to give.
/// </remarks>
public sealed record QualifiedNameSyntax(
    IndexedNameSyntax Head,
    ImmutableArray<QualifiedNamePart> Parts) : SyntaxNode
{
    /// <summary>Gets the whole name in the registry's spelling: <c>in[2].t</c>.</summary>
    /// <value>
    /// The written text with any whitespace removed, which the grammar already forbids inside a name;
    /// <c>in[1]</c> is kept as written here and folded to <c>in</c> by the binder, since the two are
    /// one port and the diagnostic that says so belongs there.
    /// </value>
    public string Text => Parts.IsDefaultOrEmpty
        ? Head.Text
        : string.Concat(Head.Text, string.Concat(Parts.Select(static part => "." + part.Name.Text)));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Parts.IsDefaultOrEmpty ? Head.Tokens : [.. Head.Tokens, .. Parts.SelectMany(static part => part.Tokens)];
}

/// <summary>A reference to a value: a name, or a component's property.</summary>
/// <param name="Head">The first name.</param>
/// <param name="Parts">The <c>.name</c> steps after it. Empty for a plain name.</param>
/// <remarks>
/// This is also what a <c>symbol</c> parameter value parses as —
/// <c>characteristic=equal_percentage</c> is a head with no parts, bound rather than evaluated. The
/// parser cannot tell the two apart and does not try: nothing distinguishes them until a parameter's
/// declared kind says which it wanted.
/// </remarks>
public sealed record ReferenceSyntax(
    IdentifierSyntax Head,
    ImmutableArray<QualifiedNamePart> Parts) : ExpressionSyntax
{
    /// <summary>Gets everything after the head as one property name: <c>dp</c>, <c>in[2].t</c>, <c>layer[3].t</c>.</summary>
    /// <returns>The parts joined with <c>.</c>; empty for a plain name.</returns>
    /// <remarks>
    /// A property is one name to the registry however many dots it carries (<c>D-120</c>), so the
    /// binder resolves the whole path and never the last step alone — <c>HX1.in[2].t</c> is the
    /// property <c>in[2].t</c> of <c>HX1</c>, not the <c>t</c> of something called <c>in[2]</c>.
    /// </remarks>
    public string PropertyPath() =>
        Parts.IsDefaultOrEmpty ? string.Empty : string.Join('.', Parts.Select(static part => part.Name.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. Head.Tokens, .. Parts.SelectMany(static part => part.Tokens)];
}

/// <summary>An expression the user wrapped in parentheses.</summary>
/// <param name="OpenParen">The opening parenthesis.</param>
/// <param name="Inner">The expression inside.</param>
/// <param name="CloseParen">The closing parenthesis.</param>
/// <remarks>
/// Kept rather than re-derived from precedence when printing (<c>D-54</c>). <c>(a + b) * c</c> and
/// <c>a + b * c</c> differ, and a redundant grouping in an engineering formula is usually deliberate.
/// </remarks>
public sealed record ParenthesizedExpressionSyntax(
    Token OpenParen,
    ExpressionSyntax Inner,
    Token CloseParen) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OpenParen, .. Inner.Tokens, CloseParen];
}

/// <summary>A negation.</summary>
/// <param name="OperatorToken">The minus.</param>
/// <param name="Operand">What is negated.</param>
/// <remarks>
/// Unary minus is the only prefix operator. Negating an absolute temperature is an error, but that is
/// the evaluator's to say (<c>FS1302</c>) — the parser does not know what the operand denotes.
/// </remarks>
public sealed record UnaryExpressionSyntax(
    Token OperatorToken,
    ExpressionSyntax Operand) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OperatorToken, .. Operand.Tokens];
}

/// <summary>An arithmetic operator applied to two operands.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="OperatorToken">The operator as written.</param>
/// <param name="Right">The right operand.</param>
public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    Token OperatorToken,
    ExpressionSyntax Right) : ExpressionSyntax
{
    /// <summary>Gets which operator this is.</summary>
    public BinaryOperator Operator => OperatorToken.Kind switch
    {
        TokenKind.Star => BinaryOperator.Multiply,
        TokenKind.Slash => BinaryOperator.Divide,
        TokenKind.Plus => BinaryOperator.Add,
        _ => BinaryOperator.Subtract,
    };

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Left.Tokens, OperatorToken, .. Right.Tokens];
}

/// <summary>One argument of a call, with the comma that precedes it.</summary>
/// <param name="LeadingComma">The comma before this argument; <see langword="null"/> for the first.</param>
/// <param name="Value">The argument.</param>
public sealed record ArgumentSyntax(Token? LeadingComma, ExpressionSyntax Value) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        LeadingComma is null ? Value.Tokens : [LeadingComma, .. Value.Tokens];
}

/// <summary>A call to one of the language's fixed set of functions.</summary>
/// <param name="Name">The function name. The set is closed and the binder owns it.</param>
/// <param name="OpenParen">The opening parenthesis.</param>
/// <param name="Arguments">The arguments, in order. May be empty.</param>
/// <param name="CloseParen">The closing parenthesis.</param>
public sealed record CallSyntax(
    IdentifierSyntax Name,
    Token OpenParen,
    ImmutableArray<ArgumentSyntax> Arguments,
    Token CloseParen) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        .. Name.Tokens,
        OpenParen,
        .. Arguments.SelectMany(static argument => argument.Tokens),
        CloseParen,
    ];
}

/// <summary>The binary operators, tightest binding first.</summary>
/// <remarks>
/// There is no remainder operator: <c>%</c> is a unit symbol (<c>D-51</c>). There is no exponentiation
/// operator either — <c>pow(x, 2)</c> is a call, because <c>^</c> and <c>**</c> both have a
/// constituency and picking one violates P6 for no gain.
/// </remarks>
public enum BinaryOperator
{
    /// <summary>Multiplication.</summary>
    Multiply = 1,

    /// <summary>Division.</summary>
    Divide,

    /// <summary>Addition.</summary>
    Add,

    /// <summary>Subtraction.</summary>
    Subtract,
}
