using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A catalogue's exact version.</summary>
/// <param name="At">The <c>@</c>.</param>
/// <param name="Number">The version, which reaches the parser as one number token.</param>
/// <remarks>
/// Split out of a single number token's source text rather than parsed from three tokens: the lexer's
/// number rule consumes a <c>.</c> followed by a digit, and recognising a version only after an
/// <c>@</c> would make the lexer position-sensitive. Reading the text rather than the value is also
/// the only way <c>@2026.10</c> stays distinguishable from <c>@2026.1</c>.
/// </remarks>
public sealed record CatalogVersionSyntax(Token At, Token Number) : SyntaxNode
{
    /// <summary>Gets the version exactly as written, without the <c>@</c>.</summary>
    public string Text => Number.Text;

    /// <summary>Gets the major part.</summary>
    public int Major => Part(0);

    /// <summary>Gets the minor part.</summary>
    public int Minor => Part(1);

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [At, Number];

    private int Part(int index)
    {
        var parts = Number.Text.Split('.');
        return index < parts.Length
            && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
    }
}
