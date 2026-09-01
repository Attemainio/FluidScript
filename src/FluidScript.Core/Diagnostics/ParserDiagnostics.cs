using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>The codes the parser emits.</summary>
/// <remarks>
/// <para>
/// Two areas, because an area names a subject rather than an emitter (<c>D-53</c>). <c>FS1003</c> and
/// <c>FS1004</c> are lexical rules — a name that reads as a quantity, a reserved word used as a name —
/// and only the parser can notice either, because the lexer has no notion of a position where a name
/// belongs. The rest are <c>FS11xx</c>, which is the parser's own area.
/// </para>
/// <para>
/// Three of <c>12</c>'s codes are deliberately absent. <c>FS1201</c> and <c>FS1202</c> classify a
/// <c>style</c> token as a colour or a corner treatment, which needs registries that do not exist yet;
/// <c>FS1107</c> fires on a <c>schedule</c> section under a circuit solved as a steady state, and
/// which mode a circuit ends up in is <c>D-37</c>'s resolution of the circuit's directive against the
/// project's — a binder question. All three land with the binder. <c>FS1203</c> is here because
/// detecting it needs only the comment the lexer already attached as trivia.
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

    /// <summary>A second <c>connections</c> or <c>schedule</c> header in one circuit.</summary>
    /// <value><c>FS1101</c>, a warning.</value>
    public static DiagnosticDescriptor DuplicateSectionHeader { get; } = new(
        "FS1101",
        DiagnosticSeverity.Warning,
        "Only the first '{section}' section is used.");

    /// <summary>A connection outside the <c>connections</c> section.</summary>
    /// <value><c>FS1102</c>, an error.</value>
    public static DiagnosticDescriptor ConnectionOutsideSection { get; } = new(
        "FS1102",
        DiagnosticSeverity.Error,
        "Connections must come after the 'connections' line.");

    /// <summary>A statement in a section that does not accept it.</summary>
    /// <value><c>FS1103</c>, an error.</value>
    public static DiagnosticDescriptor StatementInWrongSection { get; } = new(
        "FS1103",
        DiagnosticSeverity.Error,
        "A {statement} cannot appear after the '{section}' line.");

    /// <summary>A line that cannot be classified.</summary>
    /// <value><c>FS1104</c>, an error.</value>
    public static DiagnosticDescriptor UnclassifiableStatement { get; } = new(
        "FS1104",
        DiagnosticSeverity.Error,
        "Cannot read this line. Expected a component declaration or a connection.");

    /// <summary>A parameter name with no value.</summary>
    /// <value><c>FS1105</c>, an error.</value>
    public static DiagnosticDescriptor ParameterWithoutValue { get; } = new(
        "FS1105",
        DiagnosticSeverity.Error,
        "'{token}' looks like a parameter but has no value. Write '{token}=…'.");

    /// <summary>A disturbance outside the <c>schedule</c> section.</summary>
    /// <value><c>FS1106</c>, an error.</value>
    public static DiagnosticDescriptor DisturbanceOutsideSchedule { get; } = new(
        "FS1106",
        DiagnosticSeverity.Error,
        "Put this under a 'schedule' line.");

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

    /// <summary><c>in</c> or <c>out</c> where an attachment was meant.</summary>
    /// <value><c>FS1109</c>, an error.</value>
    /// <remarks>
    /// This is the only place the parser looks at an identifier's spelling, and it is worth the
    /// exception. <c>in N3</c> is a legal component declaration — a component named <c>in</c> of kind
    /// <c>N3</c> — so without this the user gets an unknown-kind message pointing at <c>N3</c>, or no
    /// message at all and a subcircuit that never attaches.
    /// </remarks>
    public static DiagnosticDescriptor InOutIsNotAnAttachment { get; } = new(
        "FS1109",
        DiagnosticSeverity.Error,
        "'{word}' is not an attachment. Write 'supply {node}' or 'return {node}'.");

    /// <summary>An attachment with no endpoint, or a second of the same direction in one circuit.</summary>
    /// <value><c>FS1110</c>, an error.</value>
    public static DiagnosticDescriptor MalformedAttachment { get; } = new(
        "FS1110",
        DiagnosticSeverity.Error,
        "'{word}' needs one node of the parent circuit, and may appear once per circuit.");

    /// <summary>A <c>control</c> line with no arguments, or an argument with no value.</summary>
    /// <value><c>FS1111</c>, an error.</value>
    public static DiagnosticDescriptor MalformedControlBinding { get; } = new(
        "FS1111",
        DiagnosticSeverity.Error,
        "A 'control' line needs named arguments, such as "
        + "'control actuate=V1.position measure=N2.t by=PID1'.");

    /// <summary>A file-wide directive after the first <c>circuit</c>, or a second of either.</summary>
    /// <value><c>FS1112</c>, an error.</value>
    public static DiagnosticDescriptor GlobalDirectiveOutOfPlace { get; } = new(
        "FS1112",
        DiagnosticSeverity.Error,
        "'{word}' applies to the whole file and must come before the first 'circuit' line.");

    /// <summary><c>spacing</c> given a quantity rather than a bare number.</summary>
    /// <value><c>FS1113</c>, an error.</value>
    public static DiagnosticDescriptor SpacingTakesABareNumber { get; } = new(
        "FS1113",
        DiagnosticSeverity.Error,
        "Spacing is in world units, so write 'spacing {n}' with no unit.");

    /// <summary>A bare <c>#rrggbb</c> in a <c>style</c> directive.</summary>
    /// <value><c>FS1203</c>, a warning.</value>
    /// <remarks>
    /// A warning about a comment, which sounds odd until you see the failure: <c>style #2f6f9f 2px</c>
    /// comments out everything from the <c>#</c>, leaving a directive with no tokens at all — legal,
    /// silent, and rendered in the default colour. The lexer cannot know a colour was meant; the style
    /// parser can, because it sees a directive whose whole token list was consumed by a comment
    /// beginning with a hex-shaped run.
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

    /// <summary>Gets every code the parser emits, for the registry to collect.</summary>
    /// <value>Sixteen descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        NameReadsAsQuantity,
        ReservedWordAsName,
        DuplicateSectionHeader,
        ConnectionOutsideSection,
        StatementInWrongSection,
        UnclassifiableStatement,
        ParameterWithoutValue,
        DisturbanceOutsideSchedule,
        HyphenInName,
        InOutIsNotAnAttachment,
        MalformedAttachment,
        MalformedControlBinding,
        GlobalDirectiveOutOfPlace,
        SpacingTakesABareNumber,
        ExtraTextOnLine,
        BareHexColour,
    ];
}
