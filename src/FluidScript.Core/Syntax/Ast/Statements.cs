using System.Collections.Immutable;
using System.Globalization;

namespace FluidScript.Core.Syntax.Ast;

/// <summary>A whole script: one ordered list of statements, and the end token that closes it.</summary>
/// <param name="Statements">Every statement in source order, malformed lines included.</param>
/// <param name="EndOfFile">
/// The zero-length token at the end of the text. Trivia after the last statement attaches to it, so a
/// file ending in comments or blank lines round-trips.
/// </param>
/// <remarks>
/// The version directive is a statement in this list rather than a field of its own (<c>D-54</c>),
/// even though the grammar writes it as <c>script = version-directive , { statement }</c>. A script
/// under editing has it missing, duplicated, or not first, and all three have to round-trip; one
/// ordered list is what makes that true and keeps the printer walking a single sequence.
/// </remarks>
public sealed record ScriptSyntax(
    ImmutableArray<StatementSyntax> Statements,
    Token EndOfFile) : SyntaxNode
{
    /// <summary>Gets the first version directive, if the script has one.</summary>
    /// <value>
    /// <see langword="null"/> when none was written. Whether that is an unsaved draft or a broken file
    /// is <c>18-script-compatibility</c>'s to decide, not the parser's.
    /// </value>
    public VersionDirectiveSyntax? Version =>
        Statements.OfType<VersionDirectiveSyntax>().FirstOrDefault();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. Statements.SelectMany(static statement => statement.Tokens), EndOfFile];
}

/// <summary>The <c>fluidscript</c> line and the language major it names.</summary>
/// <param name="Keyword">The <c>fluidscript</c> word.</param>
/// <param name="Major">The major version as written.</param>
/// <remarks>
/// The parser records it and judges nothing. Whether an absent directive is an unsaved draft
/// (<c>FS1701</c>) or a misplaced one (<c>FS1705</c>) depends on whether the text is a durable file,
/// which the parser cannot know.
/// </remarks>
public sealed record VersionDirectiveSyntax(Token Keyword, NumberLiteralSyntax Major) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Major.Tokens];
}

/// <summary>Names the project and sets the file-wide default solve mode.</summary>
/// <param name="Keyword">The <c>project</c> word.</param>
/// <param name="ModeToken">The <c>dynamic</c> or <c>static</c> word, if one was written.</param>
/// <param name="Name">The project name.</param>
public sealed record ProjectDirectiveSyntax(
    Token Keyword,
    Token? ModeToken,
    IdentifierSyntax Name) : StatementSyntax
{
    /// <summary>Gets the stated solve mode.</summary>
    /// <value>
    /// <see langword="null"/> when neither word was written, which leaves every circuit's own
    /// directive to decide (<c>D-37</c>).
    /// </value>
    public FluidMode? Mode => ModeToken.ToFluidMode();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        ModeToken is null ? [Keyword, .. Name.Tokens] : [Keyword, ModeToken, .. Name.Tokens];
}

/// <summary>Component spacing on the canvas, in world units.</summary>
/// <param name="Keyword">The <c>spacing</c> word.</param>
/// <param name="Value">A bare number.</param>
/// <remarks>
/// Never a quantity: world units have no physical dimension, and accepting <c>20 mm</c> would imply
/// the canvas has a scale it does not have. A quantity here is <c>FS1113</c>.
/// </remarks>
public sealed record SpacingDirectiveSyntax(Token Keyword, NumberLiteralSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Value.Tokens];
}

/// <summary>Begins a circuit. Every statement until the next header belongs to it.</summary>
/// <param name="Keyword">The <c>circuit</c> word.</param>
/// <param name="Name">
/// The circuit's name, which doubles as its role — resolved against the role registry at bind time
/// (<c>D-35</c>). The parser does not classify it.
/// </param>
/// <param name="Number">
/// The designation as written; <see langword="null"/> when omitted, in which case the binder resolves
/// one (<c>D-33</c>). The parser never invents a number: an absent one must stay distinguishable from
/// a written one so the printer can reproduce the source byte for byte.
/// </param>
public sealed record CircuitHeaderSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    NumberLiteralSyntax? Number) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Number is null ? [Keyword, .. Name.Tokens] : [Keyword, .. Name.Tokens, .. Number.Tokens];
}

/// <summary>What a circuit carries, and how it is solved.</summary>
/// <param name="Keyword">The <c>fluid</c> word.</param>
/// <param name="ModeToken">The <c>dynamic</c> or <c>static</c> word, if one was written.</param>
/// <param name="Substance">The substance name, resolved at bind time.</param>
/// <param name="Arguments">Empty in v1; mixtures are deferred.</param>
public sealed record FluidDirectiveSyntax(
    Token Keyword,
    Token? ModeToken,
    IdentifierSyntax Substance,
    ImmutableArray<ExpressionSyntax> Arguments) : StatementSyntax
{
    /// <summary>Gets the stated solve mode.</summary>
    /// <value>
    /// <see langword="null"/> when neither word was written, which leaves the project's default to
    /// decide (<c>D-37</c>, <c>D-54</c>). It must not default to <see cref="FluidMode.Static"/>: that
    /// loses the difference between <c>fluid water</c> and <c>fluid static water</c>, which breaks the
    /// round trip and makes every circuit in a <c>project dynamic</c> file warn about a word its
    /// author never wrote.
    /// </value>
    public FluidMode? Mode => ModeToken.ToFluidMode();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. ModeToken is null ? ImmutableArray<Token>.Empty : [ModeToken],
        .. Substance.Tokens,
        .. Arguments.SelectMany(static argument => argument.Tokens),
    ];
}

/// <summary>Selects the catalogue auto-sizing draws from.</summary>
/// <param name="Keyword">The <c>catalog</c> word.</param>
/// <param name="CatalogId">The catalogue's name.</param>
/// <param name="Version">The pinned version, or <see langword="null"/> to track the shipped one.</param>
public sealed record CatalogDirectiveSyntax(
    Token Keyword,
    IdentifierSyntax CatalogId,
    CatalogVersionSyntax? Version) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Version is null
            ? [Keyword, .. CatalogId.Tokens]
            : [Keyword, .. CatalogId.Tokens, .. Version.Tokens];
}

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

/// <summary>One positional token of a <c>style</c> directive.</summary>
/// <param name="Kind">Its lexical shape.</param>
/// <param name="Parts">
/// The tokens it is made of. One for every shape but a pattern, which the lexer produced as two —
/// <c>--</c>, <c>..</c>, <c>-.</c> — and which is recombined here.
/// </param>
public sealed record StyleTokenSyntax(StyleTokenKind Kind, ImmutableArray<Token> Parts) : SyntaxNode
{
    /// <summary>Gets the token as written.</summary>
    public string Text => string.Concat(Parts.Select(static part => part.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Parts;
}

/// <summary>Presentation for the statements that follow.</summary>
/// <param name="Keyword">The <c>style</c> word.</param>
/// <param name="Parts">The style tokens, in source order. Order carries no meaning.</param>
public sealed record StyleDirectiveSyntax(
    Token Keyword,
    ImmutableArray<StyleTokenSyntax> Parts) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. Parts.SelectMany(static part => part.Tokens)];
}

/// <summary>Which fluid properties drive the canvas colour scale.</summary>
/// <param name="Keyword">The <c>show</c> word.</param>
/// <param name="Properties">The property names, resolved against the property registry at bind time.</param>
/// <param name="Scale">An explicit range for the scale, or <see langword="null"/> to derive one.</param>
public sealed record ShowDirectiveSyntax(
    Token Keyword,
    ImmutableArray<IdentifierSyntax> Properties,
    RangeSyntax? Scale) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        Keyword,
        .. Properties.SelectMany(static property => property.Tokens),
        .. Scale?.Tokens ?? [],
    ];
}

/// <summary>Binds a name to an expression.</summary>
/// <param name="Keyword">The <c>let</c> word.</param>
/// <param name="Name">The name being bound.</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">What it is bound to.</param>
public sealed record LetBindingSyntax(
    Token Keyword,
    IdentifierSyntax Name,
    Token EqualsToken,
    ExpressionSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. Name.Tokens, EqualsToken, .. Value.Tokens];
}

/// <summary>Where a subcircuit meets its parent.</summary>
/// <param name="Keyword">The <c>supply</c> or <c>return</c> word.</param>
/// <param name="Endpoint">
/// A node of the parent circuit. Whether it exists is a binder question, not a parser one.
/// </param>
public sealed record AttachmentSyntax(Token Keyword, EndpointSyntax Endpoint) : StatementSyntax
{
    /// <summary>Gets which side this declares.</summary>
    public AttachmentDirection Direction =>
        Keyword.Keyword == ReservedWord.Supply ? AttachmentDirection.Supply : AttachmentDirection.Return;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Endpoint.Tokens];
}

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
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. Arguments.SelectMany(static argument => argument.Tokens)];
}

/// <summary>Declares a component: a name, a kind, and a bag of parameters.</summary>
/// <param name="Name">The component's name, unique across the whole model (<c>D-41</c>).</param>
/// <param name="Kind">The kind, resolved against the component registry at bind time.</param>
/// <param name="Parameters">The stated parameters. An omitted one is absence, never null.</param>
public sealed record ComponentDeclarationSyntax(
    IdentifierSyntax Name,
    IdentifierSyntax Kind,
    ImmutableArray<ParameterSyntax> Parameters) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        .. Name.Tokens,
        .. Kind.Tokens,
        .. Parameters.SelectMany(static parameter => parameter.Tokens),
    ];
}

/// <summary>One <c>name=value</c> pair.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">
/// An expression, a reference, or a symbol — which of the three depends on the parameter's declared
/// kind, so the parser records an expression and the binder decides.
/// </param>
public sealed record ParameterSyntax(
    IdentifierSyntax Name,
    Token EqualsToken,
    ExpressionSyntax Value) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Name.Tokens, EqualsToken, .. Value.Tokens];
}

/// <summary>Begins a circuit's topology.</summary>
/// <param name="Keyword">The <c>connections</c> word.</param>
public sealed record ConnectionsHeaderSyntax(Token Keyword) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword];
}

/// <summary>Begins a circuit's transient disturbances.</summary>
/// <param name="Keyword">The <c>schedule</c> word.</param>
public sealed record ScheduleHeaderSyntax(Token Keyword) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword];
}

/// <summary>One <c>- endpoint</c> step of a connection.</summary>
/// <param name="Dash">The connection operator.</param>
/// <param name="Endpoint">The endpoint after it.</param>
public sealed record ConnectionLinkSyntax(Token Dash, EndpointSyntax Endpoint) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Dash, .. Endpoint.Tokens];
}

/// <summary>One line of topology.</summary>
/// <param name="First">The first endpoint.</param>
/// <param name="Links">
/// The rest. <c>A - B - C</c> is held as a first endpoint and two links, and desugars to two
/// connections at bind time (rule I6), so the printer can reproduce the chain the user wrote.
/// </param>
public sealed record ConnectionSyntax(
    EndpointSyntax First,
    ImmutableArray<ConnectionLinkSyntax> Links) : StatementSyntax
{
    /// <summary>Gets every endpoint on the line, in order.</summary>
    public ImmutableArray<EndpointSyntax> Endpoints =>
        [First, .. Links.Select(static link => link.Endpoint)];

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. First.Tokens, .. Links.SelectMany(static link => link.Tokens)];
}

/// <summary>One end of a connection: a component, optionally a named port.</summary>
/// <param name="Component">The component's name.</param>
/// <param name="Dot">The separator before a port, if there is one.</param>
/// <param name="Port">
/// The port, or <see langword="null"/> for "the next free port, in the component's declared port
/// order" — which is what makes the reference circuits work with no port names at all.
/// </param>
public sealed record EndpointSyntax(
    IdentifierSyntax Component,
    Token? Dot,
    IdentifierSyntax? Port) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Dot is null || Port is null
            ? Component.Tokens
            : [.. Component.Tokens, Dot, .. Port.Tokens];
}

/// <summary>One entry of the schedule section: a change applied at a time or over an interval.</summary>
/// <param name="Keyword">The <c>at</c> or <c>over</c> word, which is an ordinary identifier.</param>
/// <param name="When">A point for the <c>at</c> form, a range for the <c>over</c> form.</param>
/// <param name="Target">The <c>component.parameter</c> being changed.</param>
/// <param name="EqualsToken">The <c>=</c>.</param>
/// <param name="Value">A point for a step, a range for a ramp.</param>
/// <remarks>
/// All four combinations are legal. <c>over</c> with a single value ramps nothing and steps at the
/// end, which is occasionally what a user means and is cheaper to allow than to diagnose.
/// </remarks>
public sealed record DisturbanceSyntax(
    Token Keyword,
    RangeOrPointSyntax When,
    EndpointSyntax Target,
    Token EqualsToken,
    RangeOrPointSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. When.Tokens, .. Target.Tokens, EqualsToken, .. Value.Tokens];
}

/// <summary>Either one value or a span between two.</summary>
/// <remarks>
/// The two cases are sibling records rather than nested ones, so that <see cref="RangeSyntax"/> can be
/// named on its own — <c>show</c> takes a range and never a point.
/// </remarks>
public abstract record RangeOrPointSyntax : SyntaxNode;

/// <summary>One value.</summary>
/// <param name="Value">The value.</param>
public sealed record PointSyntax(ExpressionSyntax Value) : RangeOrPointSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Value.Tokens;
}

/// <summary>A span between two values.</summary>
/// <param name="From">The lower end.</param>
/// <param name="DotDot">The <c>..</c>.</param>
/// <param name="To">The upper end.</param>
public sealed record RangeSyntax(
    ExpressionSyntax From,
    Token DotDot,
    ExpressionSyntax To) : RangeOrPointSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. From.Tokens, DotDot, .. To.Tokens];
}

/// <summary>A line the parser could not classify.</summary>
/// <param name="Parts">Every token on the line, so nothing is lost.</param>
/// <remarks>
/// This is what makes P4 and <c>R-05</c> real. Recovery is line-granular: a line that cannot be parsed
/// becomes one of these, the parser resumes at the next line, and every other line is unaffected. Line
/// granularity is chosen over token-level recovery because the language is line-oriented — there is no
/// construct spanning lines to resynchronise into.
/// </remarks>
public sealed record MalformedStatementSyntax(ImmutableArray<Token> Parts) : StatementSyntax
{
    /// <summary>Gets the line exactly as written, excluding its trivia.</summary>
    public string RawText => string.Concat(Parts.Select(static part => part.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Parts;
}

/// <summary>Reads a solve-mode keyword token.</summary>
internal static class ModeTokenExtensions
{
    /// <summary>Converts an optional <c>dynamic</c>/<c>static</c> token to a mode.</summary>
    /// <param name="token">The token, or <see langword="null"/> when neither word was written.</param>
    /// <returns>The mode, or <see langword="null"/> when nothing was written.</returns>
    public static FluidMode? ToFluidMode(this Token? token) => token?.Keyword switch
    {
        ReservedWord.Dynamic => FluidMode.Dynamic,
        ReservedWord.Static => FluidMode.Static,
        _ => null,
    };
}
