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
/// <para>
/// The binder also raises the component-model codes that are decided by counting and comparing what a
/// script <em>stated</em> — <c>FS2101</c>, <c>FS2103</c>, <c>FS2105</c>, <c>FS2107</c>, <c>FS2108</c>
/// and <c>FS2113</c>–<c>FS2115</c>. The rest of that range needs a stage this one is not:
/// <c>FS2102</c> needs the sizing loop; <c>FS2104</c>, <c>FS2111</c> and <c>FS2116</c> each need a
/// resolved substance, and the binder holds a fluid's <em>name</em>; <c>FS2106</c> belongs to whatever
/// actually clamps a pipe's discretization, which is lowering; and <c>FS2109</c>, <c>FS2110</c> and
/// <c>FS2112</c> are the exchanger's Rated and Coupled modes (<c>C-23</c>).
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

    /// <summary>A negative value for a parameter whose declared range starts at or above zero.</summary>
    /// <value><c>FS1307</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// An error rather than another <see cref="ValueOutsideUsualRange"/> warning, because a sign is not
    /// a plausibility judgement. <c>dt=-20</c> on an exchanger is not an unusual temperature rise; it is
    /// the wrong parameter for the intent, and the one that expresses it is <c>power</c>, whose sign
    /// <em>is</em> the direction of the duty. That is what makes <c>power=-70 dt=20</c> a cooler and
    /// <c>dt=-20</c> a mistake.
    /// </para>
    /// <para>
    /// <strong>Absolute temperatures are exempt.</strong> −50 °C is 223 K, so a parameter whose zero is
    /// a scale offset has no "negative" worth reporting; a value genuinely below absolute zero falls out
    /// of the usual range and is reported as that instead.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor NegativeValue { get; } = new(
        "FS1307",
        DiagnosticSeverity.Error,
        "{parameter} cannot be negative.");

    /// <summary>More parameters of one relation stated than the relation has freedoms.</summary>
    /// <value><c>FS2101</c>, an error.</value>
    /// <remarks>
    /// Registry data decides this, not a rule per kind: a group names the parameters and how many of
    /// them are independent, so an exchanger's <c>power</c>/<c>in</c>/<c>out</c>/<c>flow</c> and its
    /// <c>ua</c>/<c>area</c>/<c>u</c> are the same check twice rather than two special cases.
    /// </remarks>
    public static DiagnosticDescriptor OverDetermined { get; } = new(
        "FS2101",
        DiagnosticSeverity.Error,
        "'{name}': {parameters} cannot all be set. Any {count} of them fix the rest.");

    /// <summary>A valve given both a flow coefficient and a pressure drop.</summary>
    /// <value><c>FS2103</c>, a warning.</value>
    /// <remarks>
    /// A warning rather than an error because the two are not contradictory, only redundant: the drop a
    /// valve produces follows from its Kv and the flow through it, so the stated <c>dp</c> is a design
    /// intention the solve will confirm or refute rather than a second constraint.
    /// </remarks>
    public static DiagnosticDescriptor RedundantValveDrop { get; } = new(
        "FS2103",
        DiagnosticSeverity.Warning,
        "'{name}': using kv={kv}; dp is implied by it.");

    /// <summary>A valve opening outside 0 to 1.</summary>
    /// <value><c>FS2105</c>, an error.</value>
    public static DiagnosticDescriptor PositionOutsideRange { get; } = new(
        "FS2105",
        DiagnosticSeverity.Error,
        "'{name}': position must be between 0 and 1.");

    /// <summary>An efficiency outside 0 to 1.</summary>
    /// <value><c>FS2108</c>, an error.</value>
    public static DiagnosticDescriptor EfficiencyOutsideRange { get; } = new(
        "FS2108",
        DiagnosticSeverity.Error,
        "'{name}': efficiency must be between 0 and 1.");

    /// <summary>A tank that mixes its bulk temperature with per-layer ones, or states only some layers.</summary>
    /// <value><c>FS2113</c>, an error.</value>
    /// <remarks>
    /// The two spellings mean different things — one initial state for the whole vessel, or a stated
    /// profile — and a script holding both leaves no rule for which wins. A partial profile is the same
    /// error seen from the other side: the layers left out have no value and no default that is not an
    /// invention.
    /// </remarks>
    public static DiagnosticDescriptor MixedTankTemperatures { get; } = new(
        "FS2113",
        DiagnosticSeverity.Error,
        "'{name}': state either t for every layer, or all of t1…t{layers}; do not mix them.");

    /// <summary>A layer count that is not a whole number in range.</summary>
    /// <value><c>FS2114</c>, an error.</value>
    public static DiagnosticDescriptor InvalidLayerCount { get; } = new(
        "FS2114",
        DiagnosticSeverity.Error,
        "'{name}': layers must be a whole number from 1 to 100.");

    /// <summary>A tank port height outside the vessel.</summary>
    /// <value><c>FS2115</c>, an error.</value>
    public static DiagnosticDescriptor ElevationOutsideRange { get; } = new(
        "FS2115",
        DiagnosticSeverity.Error,
        "'{name}': {parameter} is normalized height and must be between 0 (bottom) and 1 (top).");

    /// <summary>A parameter whose absence the kind has no answer for.</summary>
    /// <value><c>FS2117</c>, an error.</value>
    /// <remarks>
    /// <strong>The third omission policy, and the rarest</strong> (<c>D-64</c>). Almost every parameter
    /// is sized or defaulted, because almost every parameter has a defensible substitute. This fires
    /// only where there is none: a boundary with no temperature has no state to give the fluid entering
    /// there, and inventing one yields a solved circuit whose every downstream temperature is wrong.
    /// </remarks>
    public static DiagnosticDescriptor MissingRequiredParameter { get; } = new(
        "FS2117",
        DiagnosticSeverity.Error,
        "'{name}': a {kind} must state {parameter}.");

    /// <summary>A parameter group with too few of its members stated to determine it.</summary>
    /// <value><c>FS2118</c>, an error.</value>
    /// <remarks>
    /// The lower bound to <see cref="OverDetermined"/>'s upper one. A <c>supply</c> states exactly one
    /// of <c>flow</c> and <c>p</c> — <c>FS2101</c> catches both, this catches neither. Neither member is
    /// individually required, so this cannot be expressed as a policy on either of them.
    /// </remarks>
    public static DiagnosticDescriptor UnderDetermined { get; } = new(
        "FS2118",
        DiagnosticSeverity.Error,
        "'{name}': a {kind} must state {count} of {parameters}.");

    /// <summary>A curve driver that names nothing at all.</summary>
    /// <value><c>FS1527</c>, an error.</value>
    /// <remarks>
    /// <para>
    /// A driver has to supply a number, and there are exactly three things that can: another curve,
    /// the clock, or a <c>design</c> line. <c>D-59</c>'s registry decides only what <em>name</em> a
    /// design value may be written under, so an unregistered driver with a design value behind it is
    /// fine and this fires for a name with nothing behind it anywhere.
    /// </para>
    /// <para>
    /// <c>D-57</c> leans on this: the three positions of a curve header are not symmetrical, and
    /// writing <c>curve outdoor heating</c> for <c>curve heating outdoor</c> is caught here in every
    /// case where both names exist, which is the ordinary one.
    /// </para>
    /// </remarks>
    public static DiagnosticDescriptor UnknownCurveDriver { get; } = new(
        "FS1527",
        DiagnosticSeverity.Error,
        "'{driver}' is not something '{curve}' can depend on. Name a curve, a known driver, or 'time'.");

    /// <summary>A curve read in a static circuit whose driver has no design value.</summary>
    /// <value><c>FS1528</c>, an error.</value>
    /// <remarks>
    /// An error rather than a default, per <c>D-58</c>: guessing zero, or the table's first row, would
    /// put a number in front of an engineer that nothing chose. The curve and driver named are the one
    /// the expression referenced and its own driver, not the far end of the chain, so the suggested
    /// <c>design</c> line is one the user can write as it stands.
    /// </remarks>
    public static DiagnosticDescriptor CurveWithoutDesignPoint { get; } = new(
        "FS1528",
        DiagnosticSeverity.Error,
        "'{curve}' depends on '{driver}', which has no value here. "
        + "Add 'design {driver}=...' or solve in time.");

    /// <summary>Two rows of one curve at the same <c>x</c>.</summary>
    /// <value><c>FS1529</c>, informational.</value>
    /// <remarks>Information rather than an error: a step is a legitimate thing to write.</remarks>
    public static DiagnosticDescriptor DuplicateCurveRow { get; } = new(
        "FS1529",
        DiagnosticSeverity.Info,
        "'{curve}' has two rows at {x}; the later one is used.");

    /// <summary>A curve with nothing to interpolate between.</summary>
    /// <value><c>FS1530</c>, an error.</value>
    public static DiagnosticDescriptor CurveTooShort { get; } = new(
        "FS1530",
        DiagnosticSeverity.Error,
        "'{curve}' needs at least two rows to interpolate between.");

    /// <summary>A bare <c>control</c> endpoint whose kind names no single parameter or property.</summary>
    /// <value><c>FS1531</c>, an error.</value>
    /// <remarks>
    /// The other half of <c>D-61</c>'s amendment to <c>D-43</c>. Where the registry names exactly one
    /// actuated parameter or measured property, the bare form is unambiguous by construction; where it
    /// names none, the qualified form is required and this says so with an example of it.
    /// </remarks>
    public static DiagnosticDescriptor NoSingleEndpoint { get; } = new(
        "FS1531",
        DiagnosticSeverity.Error,
        "A {kind} has no single {role} to use here. Write it out, such as '{example}'.");

    /// <summary>An <c>at</c> clause on a kind that carries flow rather than observing it.</summary>
    /// <value><c>FS1532</c>, an error.</value>
    /// <remarks>
    /// <c>D-61</c> settles what <c>at</c> means — a sensor attaches to a node and stays out of the
    /// hydraulic graph — and says nothing about writing it on a pump. Accepting it silently would let
    /// a component claim to observe a node and carry flow at the same time, which no later stage has a
    /// way to represent. Recorded in <c>plan/10-language/defects.md</c> as a gap this filled.
    /// </remarks>
    public static DiagnosticDescriptor NotAnObserver { get; } = new(
        "FS1532",
        DiagnosticSeverity.Error,
        "'{name}' is a {kind}, which is not placed with 'at'. Connect it with '-' instead.");

    /// <summary>An instrument that was declared and never placed.</summary>
    /// <value><c>FS1533</c>, a warning.</value>
    /// <remarks>
    /// A warning, not an error: an unplaced sensor is the ordinary state of a line half typed, and the
    /// model binds around it. It exists because an observer is exempt from <c>FS1507</c> — a sensor is
    /// not connected to anything and never will be — so without it an instrument attached to nothing
    /// would bind in silence.
    /// </remarks>
    public static DiagnosticDescriptor ObserverNotPlaced { get; } = new(
        "FS1533",
        DiagnosticSeverity.Warning,
        "'{name}' observes nothing. Place it with 'at' and the name of a node.");

    /// <summary>Gets every code the binder emits, for the registry to collect.</summary>
    /// <value>Forty-five descriptors. Order does not matter; the registry sorts.</value>
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
        UnknownCurveDriver,
        CurveWithoutDesignPoint,
        DuplicateCurveRow,
        CurveTooShort,
        NoSingleEndpoint,
        NotAnObserver,
        ObserverNotPlaced,
        DeadEndNode,
        NegativeValue,
        OverDetermined,
        RedundantValveDrop,
        PositionOutsideRange,
        EfficiencyOutsideRange,
        MixedTankTemperatures,
        InvalidLayerCount,
        ElevationOutsideRange,
        MissingRequiredParameter,
        UnderDetermined,
    ];
}
