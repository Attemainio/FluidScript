using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>The codes the lexer emits.</summary>
/// <remarks>
/// <para>
/// The <c>FS10xx</c> range is allocated to lexical rules
/// (<c>plan/10-language/16-diagnostics.md</c>), and two of its four codes are emitted here. The other
/// two are not: <c>FS1003</c> fires on an identifier that reads as a quantity and <c>FS1004</c> on a
/// reserved word used as a name, and neither condition is visible to the lexer — <c>3K</c> is a
/// quantity everywhere, including in <c>let x = 3K</c> where it is correct, and only the parser knows
/// that a token stands where a name belongs. They arrive with the parser, which is the rule the
/// registry states: a descriptor lands with its emitter.
/// </para>
/// <para>
/// That leaves the range naming a stage that does not emit two of its codes, which is worth knowing
/// before <c>16</c>'s "the range says which stage produced it" is relied on as an invariant.
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

    /// <summary>Gets every code this stage owns, for the registry to collect.</summary>
    /// <value>Two descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        UnterminatedString,
        UnrecognisedCharacter,
    ];
}
