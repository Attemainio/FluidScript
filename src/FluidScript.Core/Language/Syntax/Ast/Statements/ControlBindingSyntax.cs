using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Binds a declared controller to what it actuates and what it measures.</summary>
/// <param name="Keyword">The <c>control</c> word.</param>
/// <param name="Arguments">
/// Named and order-independent, reusing <see cref="ParameterSyntax"/>. The parser accepts any set of
/// names and any count; which are recognised, which are required, and what each must resolve to are
/// the binder's.
/// </param>
public sealed record ControlBindingSyntax(
    Token Keyword,
    ImmutableArray<ParameterSyntax> Arguments) : StatementSyntax
{
    /// <summary>Gets what the loop drives, in the short form (<c>D-61</c>).</summary>
    /// <value>
    /// <see langword="null"/> for the named form. The port half is optional: where a kind names
    /// exactly one actuated parameter, <c>TV1</c> is unambiguous by construction.
    /// </value>
    public EndpointSyntax? Actuator { get; init; }

    /// <summary>Gets the <c>with</c> word, retained so the line prints back byte for byte.</summary>
    public Token? WithKeyword { get; init; }

    /// <summary>Gets what the loop reads, in the short form.</summary>
    public EndpointSyntax? Sensor { get; init; }

    /// <summary>Gets the <c>by</c> word.</summary>
    public Token? ByKeyword { get; init; }

    /// <summary>Gets the controller, in the short form.</summary>
    public IdentifierSyntax? Controller { get; init; }

    /// <summary>Gets whether this line was written in the short form.</summary>
    public bool IsShortForm => Actuator is not null;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. Actuator?.Tokens ?? [],
        .. WithKeyword is { } with ? new[] { with } : [],
        .. Sensor?.Tokens ?? [],
        .. ByKeyword is { } by ? new[] { by } : [],
        .. Controller?.Tokens ?? [],
        .. Arguments.SelectMany(static argument => argument.Tokens),
    ];
}
