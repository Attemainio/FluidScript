using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything the binder and the expression evaluator can report.</summary>
/// <remarks>
/// <para>
/// Three code ranges, because three documents own them. <c>FS13xx</c> is the type and unit system
/// (<c>13</c>), <c>FS14xx</c> is expressions and references (<c>14</c>), and <c>FS15xx</c> is binding
/// (<c>15</c>). The ranges name their subject rather than the stage that emits them (<c>D-53</c>),
/// which is why the binder raises codes from all three.
/// </para>
/// <para>
/// Codes this area owns and does not yet raise, each because it needs a stage that does not exist:
/// <c>FS1405</c> (the fixed point did not converge) needs the outer sizing loop, P3.7; <c>FS1407</c>
/// (a solved value where the consumer must be final) needs a consumer that is finalized before the
/// loop — a catalogue id, a schedule time, a fixed visualization range — and steps 0–5 bind none of
/// them;
/// <c>FS1504</c>–<c>FS1507</c>, <c>FS1510</c>, <c>FS1511</c>, <c>FS1518</c>, <c>FS1520</c>–<c>FS1523</c>
/// and <c>FS1526</c> are connections, inference, attachments and control bindings, which are binder
/// steps 6–11 and land in P2.8. Registering them before they can fire would put codes on the
/// documentation page that nothing produces.
/// </para>
/// </remarks>
public static class BinderDiagnostics
{
    /// <summary>A <c>schedule</c> section in a circuit solved as a steady state.</summary>
    /// <value><c>FS1107</c>, a warning.</value>
    /// <remarks>
    /// The code belongs to the grammar's range because that is its subject (<c>D-53</c>), but only the
    /// binder can raise it: which mode a circuit ends up in is <c>D-37</c>'s resolution of the
    /// circuit's own directive against the project's, and the parser has neither.
    /// </remarks>
    public static DiagnosticDescriptor ScheduleWithoutTime { get; } = new(
        "FS1107",
        DiagnosticSeverity.Warning,
        "'{circuit}' is solved as a steady state, so its schedule does not run. "
        + "Write 'fluid dynamic' to solve it in time.");

    /// <summary>Two absolute temperatures added together.</summary>
    /// <value><c>FS1302</c>, an error.</value>
    /// <remarks>
    /// The invariant the whole type system exists for: <c>20 °C + 30 °C</c> is not 596 K. Adding a
    /// temperature and a temperature <em>difference</em> is fine, which is what the message says.
    /// </remarks>
    public static DiagnosticDescriptor CannotAddAbsolutes { get; } = new(
        "FS1302",
        DiagnosticSeverity.Error,
        "Cannot add two {dimension}s. To offset by a difference, write '{example}'.");

    /// <summary>A value whose dimension is not the one the parameter takes.</summary>
    /// <value><c>FS1304</c>, an error.</value>
    public static DiagnosticDescriptor ParameterDimensionMismatch { get; } = new(
        "FS1304",
        DiagnosticSeverity.Error,
        "'{parameter}' is a {expected}; '{value}' is a {actual}.");

    /// <summary>Two operands whose dimensions the operator cannot combine.</summary>
    /// <value><c>FS1305</c>, an error.</value>
    public static DiagnosticDescriptor OperandDimensionMismatch { get; } = new(
        "FS1305",
        DiagnosticSeverity.Error,
        "Cannot {operation} a {left} and a {right}.");

    /// <summary>A value far outside what the parameter usually holds.</summary>
    /// <value><c>FS1306</c>, a warning.</value>
    /// <remarks>
    /// The one that catches the real failure: <c>power=30000</c> meaning watts where kW was expected
    /// draws a plausible diagram of a 30 MW plant, and nothing else in the pipeline objects.
    /// </remarks>
    public static DiagnosticDescriptor ValueOutsideUsualRange { get; } = new(
        "FS1306",
        DiagnosticSeverity.Warning,
        "{parameter} = {value} is outside the usual range ({low}–{high}). Check the unit.");

    /// <summary>A second <c>let</c> of a name already bound.</summary>
    /// <value><c>FS1401</c>, an error.</value>
    public static DiagnosticDescriptor DuplicateBinding { get; } = new(
        "FS1401",
        DiagnosticSeverity.Error,
        "'{name}' is already defined at line {line}.");

    /// <summary>A value that depends on itself without passing through a solve.</summary>
    /// <value><c>FS1402</c>, an error.</value>
    /// <remarks>
    /// Reported with the whole cycle. Naming one participant is the standard failure of cycle
    /// diagnostics and is useless when the cycle is four links long.
    /// </remarks>
    public static DiagnosticDescriptor CyclicDependency { get; } = new(
        "FS1402",
        DiagnosticSeverity.Error,
        "'{name}' depends on itself: {cycle}.");

    /// <summary>A division whose divisor evaluated to zero.</summary>
    /// <value><c>FS1403</c>, an error.</value>
    public static DiagnosticDescriptor DivisionByZero { get; } = new(
        "FS1403",
        DiagnosticSeverity.Error,
        "Dividing by zero here. '{expression}' is zero.");

    /// <summary>A name that resolves to nothing.</summary>
    /// <value><c>FS1404</c>, an error.</value>
    /// <remarks>
    /// The "did you mean" half of <c>14</c>'s message shape is carried by
    /// <see cref="Diagnostic.Suggestion"/> rather than spliced into the text. A template ending in an
    /// optional clause is not a sentence when the clause is empty, which the message style rules
    /// reject — and a structured suggestion is what an editor can offer as a fix rather than prose to
    /// parse back out.
    /// </remarks>
    public static DiagnosticDescriptor UnknownName { get; } = new(
        "FS1404",
        DiagnosticSeverity.Error,
        "Nothing named '{name}'.");

    /// <summary>A property the component's kind does not have.</summary>
    /// <value><c>FS1406</c>, an error.</value>
    /// <remarks>Listing what it does have is the difference between a diagnostic and a scavenger hunt.</remarks>
    public static DiagnosticDescriptor UnknownProperty { get; } = new(
        "FS1406",
        DiagnosticSeverity.Error,
        "A {kind} has no '{property}'. It has: {available}.");

    /// <summary>A call to a function that does not exist.</summary>
    /// <value><c>FS1408</c>, an error.</value>
    public static DiagnosticDescriptor UnknownFunction { get; } = new(
        "FS1408",
        DiagnosticSeverity.Error,
        "No function '{name}'. Available: {available}.");

    /// <summary>A call with the wrong number of arguments.</summary>
    /// <value><c>FS1409</c>, an error.</value>
    public static DiagnosticDescriptor WrongArgumentCount { get; } = new(
        "FS1409",
        DiagnosticSeverity.Error,
        "'{function}' takes {expected} arguments.");

    /// <summary>A component name declared twice.</summary>
    /// <value><c>FS1501</c>, an error.</value>
    public static DiagnosticDescriptor DuplicateComponent { get; } = new(
        "FS1501",
        DiagnosticSeverity.Error,
        "'{name}' is already declared at line {line}. Names are unique across the whole file; "
        + "tags are what distinguish circuits.");

    /// <summary>A kind name that resolves to nothing.</summary>
    /// <value><c>FS1502</c>, an error.</value>
    /// <remarks>
    /// <c>15</c> gives this two message shapes, with and without a suggestion. It is one message here,
    /// and the suggestion rides on <see cref="Diagnostic.Suggestion"/> — see
    /// <see cref="UnknownName"/> for why.
    /// </remarks>
    public static DiagnosticDescriptor UnknownKind { get; } = new(
        "FS1502",
        DiagnosticSeverity.Error,
        "There is no '{kind}'.");

    /// <summary>A parameter the kind does not accept.</summary>
    /// <value><c>FS1503</c>, an error.</value>
    public static DiagnosticDescriptor UnknownParameter { get; } = new(
        "FS1503",
        DiagnosticSeverity.Error,
        "A {kind} has no '{parameter}'. It accepts: {available}.");

    /// <summary>A script with no <c>circuit</c> header.</summary>
    /// <value><c>FS1508</c>, a warning.</value>
    public static DiagnosticDescriptor NoCircuitHeader { get; } = new(
        "FS1508",
        DiagnosticSeverity.Warning,
        "No circuit name; using '{name}'.");

    /// <summary>A name read as a near miss for a registered one.</summary>
    /// <value><c>FS1512</c>, informational.</value>
    /// <remarks>
    /// Always emitted, never suppressed. A resolution the user cannot see is magic, and escalating it
    /// to a warning would put an amber squiggle on a script doing exactly what its author meant.
    /// </remarks>
    public static DiagnosticDescriptor ResolvedBySimilarity { get; } = new(
        "FS1512",
        DiagnosticSeverity.Info,
        "Read '{written}' as '{canonical}'.");

    /// <summary>A kind name equally close to two registered kinds.</summary>
    /// <value><c>FS1513</c>, an error.</value>
    public static DiagnosticDescriptor AmbiguousKind { get; } = new(
        "FS1513",
        DiagnosticSeverity.Error,
        "'{written}' could be '{first}' or '{second}'. Write one of them.");

    /// <summary>A symbol-valued parameter given a name it does not accept.</summary>
    /// <value><c>FS1514</c>, an error.</value>
    public static DiagnosticDescriptor UnacceptedSymbol { get; } = new(
        "FS1514",
        DiagnosticSeverity.Error,
        "'{parameter}' accepts {available}; '{written}' is none of them.");

    /// <summary>A reference-valued parameter given something that is not a reference.</summary>
    /// <value><c>FS1515</c>, an error.</value>
    public static DiagnosticDescriptor ExpectedReference { get; } = new(
        "FS1515",
        DiagnosticSeverity.Error,
        "'{parameter}' names a component property, like 'N2.t'.");

    /// <summary>An indexed port or parameter outside its family's declared range.</summary>
    /// <value><c>FS1516</c>, an error.</value>
    public static DiagnosticDescriptor IndexOutsideFamily { get; } = new(
        "FS1516",
        DiagnosticSeverity.Error,
        "'{written}' is outside {kind}'s supported {min}…{max} range.");

    /// <summary>A circuit whose own mode contradicts the project's default.</summary>
    /// <value><c>FS1517</c>, a warning.</value>
    /// <remarks>The circuit's own setting wins; the warning exists so the disagreement is visible.</remarks>
    public static DiagnosticDescriptor ModeContradictsProject { get; } = new(
        "FS1517",
        DiagnosticSeverity.Warning,
        "'{circuit}' is {circuitMode} while the project is {projectMode}; the circuit's own setting is used.");

    /// <summary>A circuit name matching no registered role.</summary>
    /// <value><c>FS1519</c>, informational.</value>
    /// <remarks>
    /// Never an error. A plant is full of circuits whose function has no registry entry, and refusing
    /// to bind one would make the language useless for the plant it describes.
    /// </remarks>
    public static DiagnosticDescriptor UnknownCircuitRole { get; } = new(
        "FS1519",
        DiagnosticSeverity.Info,
        "'{name}' is not a known circuit role, so it is placed neutrally. Known roles: {available}.");

    /// <summary>Two circuits claiming one number.</summary>
    /// <value><c>FS1524</c>, an error.</value>
    public static DiagnosticDescriptor DuplicateCircuitNumber { get; } = new(
        "FS1524",
        DiagnosticSeverity.Error,
        "Circuit {number} is already '{owner}'. Every circuit's number is its own.");

    /// <summary>Two circuits sharing a name.</summary>
    /// <value><c>FS1525</c>, an error.</value>
    /// <remarks>
    /// A name is an identity here as much as a component's is: parent circuits, component membership
    /// and distribution groups all key on it.
    /// </remarks>
    public static DiagnosticDescriptor DuplicateCircuitName { get; } = new(
        "FS1525",
        DiagnosticSeverity.Error,
        "'{name}' is already a circuit at line {line}.");

    /// <summary>An endpoint naming a declared value rather than a component.</summary>
    /// <value><c>FS1504</c>, an error.</value>
    /// <remarks>
    /// The narrow half of "endpoint names an unknown component": the wide half is inference rule I1,
    /// which turns an unknown name into a node. A name that is already a <c>let</c> binding cannot
    /// become one, because the model would then hold two different things under one identifier.
    /// </remarks>
    public static DiagnosticDescriptor ValueUsedAsComponent { get; } = new(
        "FS1504",
        DiagnosticSeverity.Error,
        "'{name}' is a value, not a component.");

    /// <summary>A qualified endpoint naming a port the kind does not have.</summary>
    /// <value><c>FS1505</c>, an error.</value>
    public static DiagnosticDescriptor UnknownPort { get; } = new(
        "FS1505",
        DiagnosticSeverity.Error,
        "A {kind} has no port '{port}'. Ports: {available}.");

    /// <summary>A second connection to a port that already has one.</summary>
    /// <value><c>FS1506</c>, an error.</value>
    /// <remarks>
    /// One port, one connection. A junction of three streams is a node, which is the one kind whose
    /// ports are unlimited — so this never fires on the shape that legitimately needs it.
    /// </remarks>
    public static DiagnosticDescriptor PortAlreadyConnected { get; } = new(
        "FS1506",
        DiagnosticSeverity.Error,
        "Port '{port}' of '{name}' is already connected at line {line}.");

    /// <summary>A declared component that appears in no connection.</summary>
    /// <value><c>FS1507</c>, a warning.</value>
    /// <remarks>
    /// A warning rather than an error because a half-written script is the normal editing state, and
    /// erroring would blank the diagram on every keystroke. The API escalates it for an explicit solve.
    /// </remarks>
    public static DiagnosticDescriptor NotConnected { get; } = new(
        "FS1507",
        DiagnosticSeverity.Warning,
        "'{name}' is not connected to anything.");

    /// <summary>A component the language created rather than the user.</summary>
    /// <value><c>FS1510</c>, informational.</value>
    /// <remarks>
    /// Off by default in the log, because on a large script one per inferred node would drown
    /// everything else — but never suppressed at the source, or the inference is invisible magic.
    /// </remarks>
    public static DiagnosticDescriptor ComponentInferred { get; } = new(
        "FS1510",
        DiagnosticSeverity.Info,
        "Added {kind} '{name}' ({rule}).");

    /// <summary>A group of connected components joined to nothing else in the circuit.</summary>
    /// <value><c>FS1511</c>, a warning.</value>
    /// <remarks>
    /// Distinct from <see cref="NotConnected"/>, and the two never both fire for one component: a
    /// component in no connection at all is <c>FS1507</c>, and this is a cluster of two or more that
    /// are connected to each other and to nothing beyond.
    /// </remarks>
    public static DiagnosticDescriptor DisconnectedGraph { get; } = new(
        "FS1511",
        DiagnosticSeverity.Warning,
        "'{name}' and {count} others are not connected to the rest of the circuit.");

    /// <summary>A <c>supply</c> or <c>return</c> naming something no circuit declares.</summary>
    /// <value><c>FS1518</c>, an error.</value>
    public static DiagnosticDescriptor AttachmentNotDeclared { get; } = new(
        "FS1518",
        DiagnosticSeverity.Error,
        "'{name}' is not declared anywhere. A subcircuit attaches to a node of another circuit.");

    /// <summary>A circuit with one attachment and not the other.</summary>
    /// <value><c>FS1520</c>, a warning.</value>
    /// <remarks>
    /// The message is direction-neutral. <c>15</c>'s original shape — "takes flow at '{node}' and never
    /// returns it" — is false for the half of the trigger where <c>return</c> is the line that was
    /// written, and a message that is wrong half the time is worse than a plainer one.
    /// </remarks>
    public static DiagnosticDescriptor LoneAttachment { get; } = new(
        "FS1520",
        DiagnosticSeverity.Warning,
        "'{circuit}' declares '{present} {node}' and no '{other}'. A subcircuit attaches with both.");

    /// <summary>A <c>control</c> line missing one of its four named arguments.</summary>
    /// <value><c>FS1521</c>, an error.</value>
    public static DiagnosticDescriptor ControlMissingArgument { get; } = new(
        "FS1521",
        DiagnosticSeverity.Error,
        "A 'control' line needs {list}. Missing: {missing}.");

    /// <summary>An <c>actuate=</c> naming something the controller cannot move.</summary>
    /// <value><c>FS1522</c>, an error.</value>
    public static DiagnosticDescriptor ParameterNotControllable { get; } = new(
        "FS1522",
        DiagnosticSeverity.Error,
        "'{param}' of '{component}' cannot be controlled.");

    /// <summary>A <c>by=</c> naming something that is not a controller.</summary>
    /// <value><c>FS1523</c>, an error.</value>
    public static DiagnosticDescriptor NotAController { get; } = new(
        "FS1523",
        DiagnosticSeverity.Error,
        "'{name}' is a {kind}, not a controller.");

    /// <summary>A subcircuit whose two attachments land in two different circuits.</summary>
    /// <value><c>FS1526</c>, an error.</value>
    public static DiagnosticDescriptor AttachmentsDisagree { get; } = new(
        "FS1526",
        DiagnosticSeverity.Error,
        "'{circuit}' takes flow from '{a}' and returns it to '{b}'. A subcircuit attaches to one "
        + "parent; write the second link as a connection.");

    /// <summary>A node with one connection and nothing to fix its state.</summary>
    /// <value><c>FS2107</c>, a warning.</value>
    /// <remarks>
    /// The code belongs to the component model's range because a node is its subject (<c>D-53</c>),
    /// and the binder is the first stage that can count a degree. A node inferred by I3 is exempt: it
    /// <em>is</em> the boundary that rule created, so it terminates a port rather than dead-ending.
    /// </remarks>
    public static DiagnosticDescriptor DeadEndNode { get; } = new(
        "FS2107",
        DiagnosticSeverity.Warning,
        "'{name}' is a dead end. Set t, p or flow to make it a boundary.");

    /// <summary>Gets every code the binder emits, for the registry to collect.</summary>
    /// <value>Thirty-eight descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        ScheduleWithoutTime,
        CannotAddAbsolutes,
        ParameterDimensionMismatch,
        OperandDimensionMismatch,
        ValueOutsideUsualRange,
        DuplicateBinding,
        CyclicDependency,
        DivisionByZero,
        UnknownName,
        UnknownProperty,
        UnknownFunction,
        WrongArgumentCount,
        DuplicateComponent,
        UnknownKind,
        UnknownParameter,
        ValueUsedAsComponent,
        UnknownPort,
        PortAlreadyConnected,
        NotConnected,
        NoCircuitHeader,
        ComponentInferred,
        DisconnectedGraph,
        ResolvedBySimilarity,
        AmbiguousKind,
        UnacceptedSymbol,
        ExpectedReference,
        IndexOutsideFamily,
        ModeContradictsProject,
        AttachmentNotDeclared,
        UnknownCircuitRole,
        LoneAttachment,
        ControlMissingArgument,
        ParameterNotControllable,
        NotAController,
        DuplicateCircuitNumber,
        DuplicateCircuitName,
        AttachmentsDisagree,
        DeadEndNode,
    ];
}
