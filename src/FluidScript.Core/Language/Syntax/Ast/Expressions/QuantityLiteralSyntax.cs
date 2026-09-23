using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

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
