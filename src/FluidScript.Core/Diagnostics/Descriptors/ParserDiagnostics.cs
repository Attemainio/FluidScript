using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>The codes the parser emits.</summary>
/// <remarks>
/// <para>
/// Two areas, because an area names a subject rather than an emitter (<c>D-53</c>). <c>FS1003</c> and
/// <c>FS1004</c> are lexical rules — a name that reads as a quantity, a reserved word used as a name —
/// and only the parser can notice either, because the lexer has no notion of a position where a name
/// belongs. The rest are <c>FS11xx</c>, which is the parser's own area.
/// </para>
/// <para>
/// <c>FS1202</c>, classifying a style's settings, is the binder's (<see cref="StyleDiagnostics"/>).
/// <c>FS1203</c> is here because detecting it needs only the comment the lexer already attached as trivia.
/// </para>
/// </remarks>
public static class ParserDiagnostics
{
    /// <summary>An identifier that reads as a quantity.</summary>
    /// <value><c>FS1003</c>, an error.</value>
    public static DiagnosticDescriptor NameReadsAsQuantity { get; } = new(
        "FS1003",
        DiagnosticSeverity.Error,
        "'{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'.");

    /// <summary>A reserved word used where a name belongs.</summary>
    /// <value><c>FS1004</c>, an error.</value>
    public static DiagnosticDescriptor ReservedWordAsName { get; } = new(
        "FS1004",
        DiagnosticSeverity.Error,
        "'{word}' is reserved. Choose another name.");

    /// <summary>A line that cannot be classified.</summary>
    /// <value><c>FS1104</c>, an error.</value>
    public static DiagnosticDescriptor UnclassifiableStatement { get; } = new(
        "FS1104",
        DiagnosticSeverity.Error,
        "Cannot read this line. Expected a declaration such as 'PU1 pump', a connection such as 'A - B', or a setting such as 'name = value'.");

    /// <summary>A parameter name with no value.</summary>
    /// <value><c>FS1105</c>, an error.</value>
    public static DiagnosticDescriptor ParameterWithoutValue { get; } = new(
        "FS1105",
        DiagnosticSeverity.Error,
        "'{token}' looks like a parameter but has no value. Write '{token} = …'.");

    /// <summary>A hyphen inside a name or a kind name.</summary>
    /// <value><c>FS1108</c>, an error.</value>
    /// <remarks>
    /// A hyphenated kind name is what a user coming from HTML, CSS or a <c>/docs</c> filename will
    /// naturally type. The parser recognises the shape and says so, rather than letting it become a
    /// subtraction between two names that inference rule I1 would silently create a node for.
    /// </remarks>
    public static DiagnosticDescriptor HyphenInName { get; } = new(
        "FS1108",
        DiagnosticSeverity.Error,
        "'{text}' — a name cannot contain '-'. Write '{underscored}'.");

    /// <summary>A bare <c>#rrggbb</c> as a style setting's value.</summary>
    /// <value><c>FS1203</c>, a warning.</value>
    /// <remarks>
    /// A warning about a comment, which sounds odd until you see the failure: <c>colour = #2f6f9f</c>
    /// comments out everything from the <c>#</c>, leaving a setting with no value at all. The lexer cannot
    /// know a colour was meant; the parser can, because it sees a line ending in <c>=</c> whose value was
    /// consumed by a comment beginning with a hex-shaped run.
    /// </remarks>
    public static DiagnosticDescriptor BareHexColour { get; } = new(
        "FS1203",
        DiagnosticSeverity.Warning,
        "'#' starts a comment; the rest of this line was ignored. Write the colour as \"{hex}\".");

    /// <summary>Text after a statement that is already complete.</summary>
    /// <value><c>FS1114</c>, an error.</value>
    /// <remarks>
    /// The general case of "this line holds more than it can": a second catalogue id, a second circuit
    /// number, a stray word after a project name. Each of those was previously either uncoded or
    /// pointed at a code whose message was about something else.
    /// </remarks>
    public static DiagnosticDescriptor ExtraTextOnLine { get; } = new(
        "FS1114",
        DiagnosticSeverity.Error,
        "'{extra}' is more than this line can hold.");

    /// <summary>A curve row written outside a curve's block.</summary>
    /// <value><c>FS1115</c>, an error.</value>
    /// <remarks>
    /// Two bare values are a statement nowhere else in the language, so the message can say what the line
    /// is rather than that it did not parse.
    /// </remarks>
    public static DiagnosticDescriptor CurveRowOutsideSection { get; } = new(
        "FS1115",
        DiagnosticSeverity.Error,
        "Put this pair under a 'curve' line.");

    /// <summary>A <c>curve</c> header naming no driver.</summary>
    /// <value><c>FS1116</c>, an error.</value>
    /// <remarks>
    /// A curve with no <c>x</c> axis is a table nothing can look a value up in. The driver is required
    /// rather than defaulted because there is no candidate a guess could be right about.
    /// </remarks>
    public static DiagnosticDescriptor CurveWithoutDriver { get; } = new(
        "FS1116",
        DiagnosticSeverity.Error,
        "'curve {name}' needs what it depends on after a colon, such as 'curve {name}: outdoor'.");

    /// <summary>A curve row that is not one x and one y.</summary>
    /// <value><c>FS1117</c>, an error.</value>
    public static DiagnosticDescriptor MalformedCurveRow { get; } = new(
        "FS1117",
        DiagnosticSeverity.Error,
        "A curve row is one x and one y, such as '-26 50'.");

    /// <summary>A bracket after a name that does not enclose one whole number touching the name (<c>D-120</c>).</summary>
    /// <value><c>FS1119</c>, an error.</value>
    /// <remarks>
    /// <c>in [2]</c>, <c>in[2.5]</c>, <c>in[]</c> and <c>in[2</c> all land here. The index is part of
    /// the name to the eye, so the grammar admits no whitespace inside it; a space would otherwise be a
    /// choice the printer had to make on every write-back.
    /// </remarks>
    public static DiagnosticDescriptor MalformedIndex { get; } = new(
        "FS1119",
        DiagnosticSeverity.Error,
        "An index is a whole number in brackets right after the name, such as 'in[2]'.");

    /// <summary>A bracketed value list that is not closed, or that has an empty slot (<c>D-143</c>).</summary>
    /// <value><c>FS1121</c>, an error.</value>
    /// <remarks>
    /// Shape only. <strong>Whether the list has the right number of values is not a parser question</strong>
    /// -- the count comes from the project's <c>cases</c>, which the parser has no view of, so a wrong
    /// length is the binder's <c>FS1540</c>. This code is for <c>[30,</c>, <c>[30 10]</c> and <c>[]</c>.
    /// </remarks>
    public static DiagnosticDescriptor MalformedScenarioList { get; } = new(
        "FS1121",
        DiagnosticSeverity.Error,
        "A list is one value per case, separated by commas, such as '[30, 10]'.");

    /// <summary>Gets every code the parser emits, for the registry to collect.</summary>
    /// <value>Twenty-three descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        MalformedScenarioList,
        MalformedIndex,
        NameReadsAsQuantity,
        ReservedWordAsName,
        UnclassifiableStatement,
        ParameterWithoutValue,
        HyphenInName,
        ExtraTextOnLine,
        CurveRowOutsideSection,
        CurveWithoutDriver,
        MalformedCurveRow,
        BareHexColour,
    ];
}
