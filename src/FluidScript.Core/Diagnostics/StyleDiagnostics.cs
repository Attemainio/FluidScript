using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>What reading a <c>style</c> or a <c>show</c> directive has to say: <c>FS1201</c>, <c>FS1202</c>, <c>FS1204</c>, <c>FS1205</c> (<c>12</c>, <c>D-104</c>) and <c>FS1210</c>, <c>FS1213</c>, <c>FS1214</c> (<c>57</c>).</summary>
/// <remarks>
/// None is an error: a style is presentation, and a diagram in the default colour is still the
/// diagram; a bad <c>show</c> must never stop a circuit rendering. <c>FS1203</c>, the bare hex colour,
/// is the parser's, because it is about a comment. <c>FS1211</c> and <c>FS1212</c> of <c>57</c> wait for
/// humid air on the wire.
/// </remarks>
public static class StyleDiagnostics
{
    /// <summary>A style token that is not a colour, a width, a corner treatment or a line pattern.</summary>
    /// <value><c>FS1201</c>, a warning.</value>
    public static DiagnosticDescriptor UnclassifiableToken { get; } = new(
        "FS1201",
        DiagnosticSeverity.Warning,
        "Ignoring style '{token}'. Expected a colour, a width, a corner style, or a line pattern.");

    /// <summary>Two tokens of the same category in one directive; the later wins.</summary>
    /// <value><c>FS1202</c>, a warning.</value>
    public static DiagnosticDescriptor OverriddenToken { get; } = new(
        "FS1202",
        DiagnosticSeverity.Warning,
        "'{a}' overrides the earlier '{b}'.");

    /// <summary>A style applied by a name no <c>style name = ...</c> defined.</summary>
    /// <value><c>FS1204</c>, a warning: the directive is ignored.</value>
    public static DiagnosticDescriptor UndefinedStyle { get; } = new(
        "FS1204",
        DiagnosticSeverity.Warning,
        "No style called '{name}' is defined; the components keep their previous style.");

    /// <summary>A style name defined twice; the later definition wins.</summary>
    /// <value><c>FS1205</c>, a warning.</value>
    public static DiagnosticDescriptor RedefinedStyle { get; } = new(
        "FS1205",
        DiagnosticSeverity.Warning,
        "Style '{name}' is defined again; the later definition is used.");

    /// <summary><c>show</c> names a property the scale does not know; it is skipped.</summary>
    /// <value><c>FS1210</c>, a warning.</value>
    public static DiagnosticDescriptor UnknownShowProperty { get; } = new(
        "FS1210",
        DiagnosticSeverity.Warning,
        "Nothing to show called '{name}'. Available: {list}.");

    /// <summary>The same property twice in one <c>show</c>.</summary>
    /// <value><c>FS1213</c>, informational.</value>
    public static DiagnosticDescriptor DuplicateShowProperty { get; } = new(
        "FS1213",
        DiagnosticSeverity.Info,
        "'{name}' listed twice.");

    /// <summary>A second <c>show</c> directive; only the first is read.</summary>
    /// <value><c>FS1214</c>, a warning.</value>
    public static DiagnosticDescriptor SecondShowDirective { get; } = new(
        "FS1214",
        DiagnosticSeverity.Warning,
        "Only the first 'show' is used.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
        [UnclassifiableToken, OverriddenToken, UndefinedStyle, RedefinedStyle, UnknownShowProperty, DuplicateShowProperty, SecondShowDirective];
}
