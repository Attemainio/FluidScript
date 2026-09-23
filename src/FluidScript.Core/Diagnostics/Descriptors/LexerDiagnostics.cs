using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>The codes the lexer emits.</summary>
/// <remarks>
/// <para>
/// The <c>FS10xx</c> range is about lexical rules (<c>plan/10-language/16-diagnostics.md</c>), and two
/// of its four codes are emitted here. The other two are not, and that is the range working as
/// intended rather than a mismatch: an area names a subject, never an emitter (<c>D-53</c>).
/// </para>
/// <para>
/// <c>FS1003</c> fires on an identifier that reads as a quantity and <c>FS1004</c> on a reserved word
/// used as a name, and neither condition is visible from here — <c>3K</c> is a quantity everywhere,
/// including in <c>let x = 3K</c> where it is correct, and only the parser knows that a token stands
/// where a name belongs. Both arrive with the parser, following the rule the registry states: a
/// descriptor lands with its emitter, whatever area its code belongs to.
/// </para>
/// </remarks>
public static class LexerDiagnostics
{
    /// <summary>A string literal with no closing quote before the end of its line.</summary>
    /// <value><c>FS1001</c>, an error.</value>
    public static DiagnosticDescriptor UnterminatedString { get; } = new(
        "FS1001",
        DiagnosticSeverity.Error,
        "Unterminated string; add a closing quote.");

    /// <summary>A character the language does not use.</summary>
    /// <value><c>FS1002</c>, an error.</value>
    public static DiagnosticDescriptor UnrecognisedCharacter { get; } = new(
        "FS1002",
        DiagnosticSeverity.Error,
        "'{ch}' is not valid here.");

    /// <summary>Gets every code the lexer emits, for the registry to collect.</summary>
    /// <value>Two descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        UnterminatedString,
        UnrecognisedCharacter,
    ];
}
