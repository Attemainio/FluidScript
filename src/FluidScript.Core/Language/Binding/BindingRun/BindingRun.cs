using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>One run of the binder over one parse. Not reusable, and not shared between threads.</summary>
internal sealed partial class BindingRun(IComponentRegistry registry, ParseResult parse, string documentName)
    : IValueScope
{
    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

    /// <summary>Diagnostics whose <c>name</c>, <c>node</c> or <c>component</c> argument may name a component, resolved once every component exists (<c>L-55</c>).</summary>
    private readonly List<(int Index, string Candidate)> _attributions = [];
    private readonly List<CircuitSymbol> _circuits = [];
    private readonly List<ComponentSymbol> _components = [];
    private readonly List<BindingSymbol> _bindings = [];
    private readonly Dictionary<string, BindingSlot> _bindingsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentSlot> _componentsByName = new(StringComparer.Ordinal);
    private ImmutableDictionary<string, string>? _suggestionIndex;
    private int _suggestionIndexDeclared;
    private readonly Dictionary<ValueId, PendingValue> _pending = [];
    private readonly DependencyGraph _graph = new();
    private readonly List<DeferredExpression> _deferred = [];
    private readonly HashSet<ValueId> _deferredTargets = [];
    private readonly List<StyleTokenSyntax> _styleTokens = [];
    private readonly Dictionary<string, StyleSpec> _styleDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<StatementSyntax, StyleSpec> _styleAt = new(ReferenceEqualityComparer.Instance);
    private StyleSpec _currentStyle = StyleSpec.Empty;
    private StyleSpec _projectStyle = StyleSpec.Empty;

    // Where each `let` was written, which is the only thing that can say whether a curve it reads is
    // read in a static circuit or a dynamic one.
    private readonly Dictionary<string, string> _bindingCircuits = new(StringComparer.Ordinal);

    private ProjectSettings _project = new(null, null);

    /// <summary>Where <c>start=</c> was written, for a start that has no clock to act on (<c>FS1547</c>).</summary>
    private TextSpan? _startSpan;
    private double? _spacing;

    /// <summary>The <c>show</c> lines, carried to the contract builder as written (<c>L-50</c>).</summary>
    private readonly List<VisualizationSymbol> _visualizations = [];

    public BindResult Execute()
    {
        var circuits = ReadLanguage2();

        CollectCurves();
        CollectDeclarations(circuits);
        Evaluate();
        ReviewComponents();
        ReviewCurveReferences();
        ReviewSizingPoints();
        ReviewStart();
        ReviewLegacyReferences();
        BindTopology(circuits);
        BindRuns();

        var model = new SemanticModel
        {
            Circuits = [.. _circuits],
            Project = _project with
            {
                Design = PublishDesign(),
                Scenarios = [.. _scenarios],
                DesignScenario = SettleDesignScenario(),
            },
            Components = [.. _components],
            Bindings = [.. _bindings],
            Style = new StyleSettings([.. _styleTokens], _spacing, _projectStyle, _styleDefinitions.ToImmutableDictionary(StringComparer.Ordinal)),
            Connections = [.. _connections],
            ControlBindings = [.. _controlBindings],
            Disturbances = [.. _disturbances],
            SymbolMap = _symbolMap,
            Deferred = [.. _deferred],
            Curves = [.. _curves],
            Visualizations = [.. _visualizations],
            Heights = _heights,
            Runs = [.. _runs],
        };

        // A diagnostic about a component carries the component (44): the producers name it in their
        // arguments, so the name is read from there rather than restated at every site, and resolved
        // here because a node's FS1510 is raised before the node it names has a slot (L-55).
        foreach (var (index, candidate) in _attributions)
        {
            if (_diagnostics[index].ComponentName is null && _componentsByName.ContainsKey(candidate))
            {
                _diagnostics[index] = _diagnostics[index] with { ComponentName = candidate };
            }
        }

        return new BindResult(model, [.. _diagnostics.OrderBy(static d => d.Span?.Start ?? 0)]);
    }

    // ---- step 0: circuits, and the file-wide settings -------------------------------------------

    /// <summary>What the front end read from the tree (<c>D-177</c>); set by step 0, before anything reads it.</summary>
    private Language2Reading _language2 = null!;

    /// <summary>Step 0: the tree, read into the binder's records (<c>D-177</c>, <c>D-178</c>).</summary>
    /// <remarks>
    /// The project and its presentation first, then each circuit from the project's style with its own merged over it,
    /// which is what a declaration written in it carries (<c>D-171</c>).
    /// </remarks>
    private List<CircuitBlock> ReadLanguage2()
    {
        var reading = new Language2Reader(parse, registry).Execute();
        _language2 = reading;
        _diagnostics.AddRange(reading.Diagnostics);

        foreach (var project in reading.Projects)
        {
            BindProject(project, null, []);
        }

        _spacing = reading.Spacing;

        foreach (var style in reading.ProjectStyles)
        {
            _styleTokens.AddRange(style);
            ReadStyle(null, style);
            _projectStyle = _currentStyle;
        }

        _visualizations.AddRange(reading.Visualizations);

        foreach (var block in reading.Circuits)
        {
            _currentStyle = _projectStyle;

            if (!block.Style.IsEmpty)
            {
                _styleTokens.AddRange(block.Style);
                ReadStyle(null, block.Style);
            }

            foreach (var statement in block.Statements.Where(_ => !_currentStyle.IsEmpty))
            {
                _styleAt[statement] = _currentStyle;
            }
        }

        AssignCircuits(reading.Circuits);
        return reading.Circuits;
    }

    /// <summary>Binds the <c>project</c> line: its name, its default mode, and <c>start=</c> (<c>D-149</c>).</summary>
    /// <param name="name">The project's name, language 1's identifier or language 2's quoted title.</param>
    /// <param name="mode">The mode every circuit takes unless it states its own, or <see langword="null"/>.</param>
    /// <param name="arguments">Its named arguments, <c>start</c> alone being one it takes.</param>
    private void BindProject(string? name, FluidMode? mode, ImmutableArray<ParameterSyntax> arguments)
    {
        double? start = null;

        foreach (var argument in arguments)
        {
            if (!string.Equals(argument.Name.Text, "start", StringComparison.Ordinal))
            {
                Report(
                    BinderDiagnostics.UnknownParameter,
                    argument.Span,
                    ("kind", "project"),
                    ("parameter", argument.Name.Text),
                    ("available", "start"));
                continue;
            }

            // `D-149`: the same reader as a time curve's rows, so a start and a curve cannot disagree
            // about how a date is read. Quoted, because `2026-01-15` is also a subtraction.
            var written = argument.Value switch
            {
                StringLiteralSyntax quoted => quoted.Value,
                NumberLiteralSyntax number => parse.Source.ToString(number.Span).Trim(),
                _ => null,
            };

            start = written is null ? null : ReadTimestamp(written, format: null);
            _startSpan = argument.Span;

            if (start is null)
            {
                Report(
                    BinderDiagnostics.StartUnreadable,
                    argument.Span,
                    ("value", parse.Source.ToString(argument.Value.Span).Trim()));
            }
        }

        _project = new ProjectSettings(name, mode) { Start = start };
    }

    private void AssignCircuits(List<CircuitBlock> blocks)
    {
        var used = new HashSet<int>();
        var byName = new Dictionary<string, TextSpan>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            if (block.Head?.Number is { } stated)
            {
                used.Add(stated);
            }
        }

        var next = 100;

        foreach (var block in blocks)
        {
            var head = block.Head;
            var name = head?.Name ?? documentName;
            var span = head?.Span ?? new TextSpan(0, 0);

            int number;
            var explicitNumber = head?.Number is not null;

            if (explicitNumber)
            {
                number = head!.Number!.Value;

                if (_circuits.Any(circuit => circuit.Number == number))
                {
                    Report(
                        BinderDiagnostics.DuplicateCircuitNumber,
                        span,
                        ("number", number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        ("owner", _circuits.First(circuit => circuit.Number == number).Name));
                }
            }
            else
            {
                // The lowest unused multiple of 100 in declaration order, so a file whose first
                // circuit states 200 still gives its second circuit 100 rather than colliding.
                while (used.Contains(next))
                {
                    next += 100;
                }

                number = next;
                used.Add(number);
            }

            if (!byName.TryAdd(name, span))
            {
                Report(
                    BinderDiagnostics.DuplicateCircuitName,
                    span,
                    ("name", name),
                    ("line", LineOf(byName[name])));
            }

            var mode = ModeOf(block, name, span);

            // A schedule in a circuit with no time to run in. The parser cannot see this: which mode a
            // circuit ends up in is the circuit's own directive resolved against the project's.
            if (mode == FluidMode.Static
                && block.Schedule is { } schedule)
            {
                Report(BinderDiagnostics.ScheduleWithoutTime, schedule, ("circuit", name));
            }

            _circuits.Add(new CircuitSymbol
            {
                Name = name,
                Number = number,
                NumberIsExplicit = explicitNumber,
                Substance = Substance(block),
                Mode = mode,
                Role = RoleOfCircuit(head, name, span),
                DeclarationSpan = span,
            });

            block.Circuit = _circuits[^1];
        }
    }

    private static string? Substance(CircuitBlock block) => block.Fluid?.Substance;

    private FluidMode ModeOf(CircuitBlock block, string name, TextSpan span)
    {
        var fluid = block.Fluid;
        var stated = fluid?.Mode;

        if (stated is null)
        {
            return _project.DefaultMode ?? FluidMode.Static;
        }

        // The circuit's own setting wins, and the disagreement is reported rather than resolved
        // quietly: a file that says dynamic once and static once means one of them by mistake.
        if (_project.DefaultMode is { } projectMode && projectMode != stated)
        {
            Report(
                BinderDiagnostics.ModeContradictsProject,
                fluid!.Span,
                ("circuit", name),
                ("circuitMode", stated.Value.ToString().ToLowerInvariant()),
                ("projectMode", projectMode.ToString().ToLowerInvariant()));
        }

        return stated.Value;
    }

    /// <summary>The role a circuit is drawn in: stated in language 2, read from the name in language 1 (<c>D-35</c>).</summary>
    /// <remarks>
    /// A language 2 circuit's title is free text in quotes, so it says nothing about the role; a circuit that
    /// states no <c>role</c> is neutral without a word, where language 1 would report that its name is no role.
    /// </remarks>
    private CircuitRole RoleOfCircuit(CircuitHead? head, string name, TextSpan span) =>
        head?.Role is { } stated
            ? RoleOf(stated.Text, stated.Span)
            : CircuitRoleRegistry.Neutral;

    private CircuitRole RoleOf(string name, TextSpan span)
    {
        var resolution = CircuitRoleRegistry.Resolve(name);

        // `D-170`: language 2 binds a name only by its spelling, so a near miss is placed neutrally and the
        // role it was near is the one-click fix.
        if (resolution.BySimilarity)
        {
            Report(
                BinderDiagnostics.UnknownCircuitRole,
                span,
                new Suggestion($"Change it to '{resolution.Role.CanonicalName}'", span, resolution.Role.CanonicalName),
                ("name", name),
                ("available", CircuitRoleRegistry.Names()));
            return CircuitRoleRegistry.Neutral;
        }

        if (!resolution.WasResolved)
        {
            Report(
                BinderDiagnostics.UnknownCircuitRole,
                span,
                ("name", name),
                ("available", CircuitRoleRegistry.Names()));
        }
        else if (resolution.BySimilarity)
        {
            Report(
                BinderDiagnostics.ResolvedBySimilarity,
                span,
                ("written", name),
                ("canonical", resolution.Role.CanonicalName));
        }

        return resolution.Role;
    }

    private string? ClosestName(string written)
    {
        var index = SuggestionIndex();

        if (index.IsEmpty)
        {
            return null;
        }

        var match = NameResolution.Match(written, index);

        return match.Best is not null && match.BestScore >= NameResolution.SuggestionFloor
            ? match.Best
            : null;
    }

    /// <summary>Every declared name by its normalised spelling, built once per set of declarations.</summary>
    /// <remarks>
    /// <para>
    /// Rebuilt only when a name has been declared since the last build. An unresolved reference is the
    /// normal state of a line being typed, so this is asked for once per unknown name per keystroke,
    /// and rebuilding it each time was a second pass over every declaration on top of the scoring.
    /// </para>
    /// <para>
    /// Names are ordinal -- <c>total</c> and <c>Total</c> are two bindings, <c>PU1</c> and <c>pu1</c>
    /// two components -- but normalising folds case and underscores, so two names can share one key.
    /// The first declared keeps it (`L-48`): a suggestion is a hint, not a resolution, and either
    /// spelling is the right hint for a typo of both. A throwing dictionary here took the binder down.
    /// </para>
    /// </remarks>
    private ImmutableDictionary<string, string> SuggestionIndex()
    {
        var declared = _bindingsByName.Count + _componentsByName.Count;

        if (_suggestionIndex is { } index && _suggestionIndexDeclared == declared)
        {
            return index;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var name in _bindingsByName.Keys.Concat(_componentsByName.Keys))
        {
            builder.TryAdd(NameResolution.Normalize(name), name);
        }

        _suggestionIndex = builder.ToImmutable();
        _suggestionIndexDeclared = declared;

        return _suggestionIndex;
    }

    // ---- reporting -------------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, params (string Name, string Value)[] arguments) =>
        Report(descriptor, span, null, arguments);

    /// <summary>Says a name was written in a spelling <c>D-120</c> retired, with the current one as the quick fix.</summary>
    /// <param name="span">The name alone, so the fix replaces the name and keeps the value (<c>L-53</c>).</param>
    /// <param name="written">What the script wrote.</param>
    /// <param name="current">The spelling it is now written in.</param>
    private void ReportLegacySpelling(TextSpan span, string written, string current) =>
        Report(
            BinderDiagnostics.LegacySpelling,
            span,
            new Suggestion($"Write '{current}'", span, current),
            ("written", written),
            ("current", current));

    private void Report(
        DiagnosticDescriptor descriptor,
        TextSpan span,
        Suggestion? suggestion,
        params (string Name, string Value)[] arguments)
    {
        var built = new DiagnosticArgument[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            built[i] = new DiagnosticArgument(arguments[i].Name, arguments[i].Value);
        }

        var diagnostic = Diagnostic.Create(descriptor, span, built);

        // A value evaluated once per case reports the same mistake once, not once per case.
        if (_case is not null && Reported(diagnostic))
        {
            return;
        }

        foreach (var (argumentName, value) in arguments)
        {
            if (argumentName is "name" or "node" or "component")
            {
                _attributions.Add((_diagnostics.Count, value));
                break;
            }
        }

        _diagnostics.Add(suggestion is null ? diagnostic : diagnostic with { Suggestion = suggestion });
    }

    private string LineOf(TextSpan span) =>
        (parse.Source.GetLinePosition(span.Start).Line + 1)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Format(double value, string? unit)
    {
        var text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return unit is null ? text : $"{text} {unit}";
    }

    /// <summary>One circuit's statements, and the circuit as the binder reads it from either language (<c>D-177</c>).</summary>
    /// <param name="Head">The header, or <see langword="null"/> for the implicit circuit a file with none gets.</param>
    /// <param name="Statements">The statements inside it.</param>
    internal sealed record CircuitBlock(CircuitHead? Head, List<StatementSyntax> Statements)
    {
        public CircuitSymbol? Circuit { get; set; }

        /// <summary>Gets or sets the circuit's fluid: language 1's line in the block, language 2's setting.</summary>
        public CircuitFluid? Fluid { get; set; }

        /// <summary>Gets or sets where the block's schedule section begins, or <see langword="null"/> when it has none.</summary>
        public TextSpan? Schedule { get; set; }

        /// <summary>Gets or sets the style tokens a language 2 circuit's <c>style:</c> block states; language 1 writes its style as a line.</summary>
        public ImmutableArray<StyleTokenSyntax> Style { get; set; } = [];

        /// <summary>Gets the lines each statement writes, keyed by the statement so they are read in written order.</summary>
        public Dictionary<StatementSyntax, List<BlockLine>> Lines { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Gets the lines a statement writes: none, for a statement the binder still reads as syntax.</summary>
        public List<BlockLine> LinesAt(StatementSyntax statement) =>
            Lines.TryGetValue(statement, out var lines) ? lines : [];

        /// <summary>Gets every line of one kind in the block, in the order written.</summary>
        public IEnumerable<T> LinesOf<T>()
            where T : BlockLine => Statements.SelectMany(LinesAt).OfType<T>();
    }

    /// <summary>A line in a circuit as the binder reads it (<c>D-177</c>): no syntax, so either language can fill it.</summary>
    /// <param name="Span">The line, which what it binds reports against.</param>
    internal abstract record BlockLine(TextSpan Span)
    {
        /// <summary>Gets the ends whose component names the symbol map records, so go-to-definition works from a use.</summary>
        public virtual IEnumerable<LineEnd> Mapped => [];

        /// <summary>Reads a statement that is one line, or <see langword="null"/> for one that is not read as a line.</summary>
        public static BlockLine? Of(StatementSyntax statement) => statement switch
        {
            ConnectionSyntax connection => new ConnectionLine(
                [.. connection.Endpoints.Select(LineEnd.Of)], connection.Parameters, connection.Span),
            DisturbanceSyntax disturbance => ChangeLine.Of(disturbance),
            _ => null,
        };
    }

    /// <summary>A connection line: its ends in order, and the pipe it describes.</summary>
    /// <param name="Ends">The ends; link <c>i</c> runs from end <c>i</c> to end <c>i + 1</c>.</param>
    /// <param name="Pipe">The pipe's properties the line states, lowered to an implicit pipe per link (<c>D-110</c>).</param>
    /// <param name="Span">The line, which every link on it reports against and which keys its implicit pipes.</param>
    internal sealed record ConnectionLine(ImmutableArray<LineEnd> Ends, ImmutableArray<ParameterSyntax> Pipe, TextSpan Span)
        : BlockLine(Span)
    {
        /// <inheritdoc/>
        public override IEnumerable<LineEnd> Mapped => Ends;
    }

    /// <summary>A control line (<c>D-61</c>): the named form, or the short form with what it moves, reads and is run by.</summary>
    /// <param name="Actuator">What the loop moves, in the short form; <see langword="null"/> in the named form.</param>
    /// <param name="Sensor">What the loop reads, in the short form.</param>
    /// <param name="Controller">The controller and where it is written, in the short form.</param>
    /// <param name="Arguments">The named arguments: all four in the named form, the setpoint and tuning in the short.</param>
    /// <param name="Span">The line.</param>
    internal sealed record ControlLine(
        LineEnd? Actuator,
        LineEnd? Sensor,
        (string Name, TextSpan Span)? Controller,
        ImmutableArray<ParameterSyntax> Arguments,
        TextSpan Span) : BlockLine(Span)
    {
        /// <summary>Gets whether the line was written in the short form.</summary>
        public bool IsShortForm => Actuator is not null;
    }

    /// <summary>A circuit's inlet or outlet (<c>D-41</c>).</summary>
    /// <param name="Direction">Which side it declares.</param>
    /// <param name="End">The component it names.</param>
    /// <param name="Span">The line.</param>
    internal sealed record AttachmentLine(AttachmentDirection Direction, LineEnd End, TextSpan Span) : BlockLine(Span)
    {
        /// <inheritdoc/>
        public override IEnumerable<LineEnd> Mapped => [End];
    }

    /// <summary>A step or ramp in a schedule or a run: the parameter it changes, when, and to what.</summary>
    /// <param name="Target">The component and parameter changed.</param>
    /// <param name="When">The instant, or the span a ramp takes.</param>
    /// <param name="Value">The value it ends at, or the range it runs through.</param>
    /// <param name="Span">The line.</param>
    internal sealed record ChangeLine(LineEnd Target, RangeOrPointSyntax When, RangeOrPointSyntax Value, TextSpan Span)
        : BlockLine(Span)
    {
        /// <inheritdoc/>
        public override IEnumerable<LineEnd> Mapped => [Target];

        public static ChangeLine Of(DisturbanceSyntax disturbance) =>
            new(LineEnd.Of(disturbance.Target), disturbance.When, disturbance.Value, disturbance.Span);
    }

    /// <summary>One end of a line: a component, and the port or property it names (<c>D-177</c>).</summary>
    /// <param name="Component">The component or node named.</param>
    /// <param name="ComponentSpan">Where its name is written.</param>
    /// <param name="Port">The port or property as written, or <see langword="null"/> when the end names none.</param>
    /// <param name="PortSpan">Where the port is written; the end's span when it names none.</param>
    /// <param name="Span">The whole end.</param>
    internal sealed record LineEnd(string Component, TextSpan ComponentSpan, string? Port, TextSpan PortSpan, TextSpan Span)
    {
        public static LineEnd Of(EndpointSyntax endpoint) => new(
            endpoint.Component.Token.Text,
            endpoint.Component.Span,
            endpoint.Port?.Text,
            endpoint.Port?.Span ?? endpoint.Span,
            endpoint.Span);
    }

    /// <summary>A circuit's header as the binder reads it (<c>D-177</c>): no syntax, so either language can fill it.</summary>
    /// <param name="Name">The name, language 1's identifier or language 2's quoted title.</param>
    /// <param name="Span">Where the header is written.</param>
    /// <param name="Number">The number written, if any.</param>
    /// <param name="Role">The role written, with where, if any.</param>
    internal sealed record CircuitHead(string Name, TextSpan Span, int? Number, (string Text, TextSpan Span)? Role)
;

    /// <summary>A circuit's fluid as the binder reads it (<c>D-177</c>).</summary>
    /// <param name="Substance">The substance as written.</param>
    /// <param name="Mode">The solve mode stated with it, or <see langword="null"/> for the project's.</param>
    /// <param name="Span">Where it is written, which a contradicted mode is reported against.</param>
    internal sealed record CircuitFluid(string Substance, FluidMode? Mode, TextSpan Span);

    private sealed record BindingSlot(LetBindingSyntax Declaration, ValueId Id);

    /// <summary>Where a component lives in <c>_components</c>, which is the one place it lives.</summary>
    /// <remarks>
    /// An index rather than the symbol itself: a <see cref="ComponentSymbol"/> is a record, and steps
    /// 6 and 11 replace it with a modified copy. A slot holding its own copy would be handing out a
    /// component whose ports and tag had been assigned to a different object.
    /// </remarks>
    private sealed record ComponentSlot(int Index, ComponentDeclarationSyntax? Declaration);

    private sealed record ParameterTarget(string Owner, ComponentKindInfo Kind, ParameterInfo Info);

    private sealed record PendingValue(
        ExpressionSyntax Expression, ValueId Id, TextSpan Span, ParameterTarget? Target)
    {
        public Quantity? Value { get; set; }

        /// <summary>Everything the expression read, from the pass that recorded the graph's edges.</summary>
        public ImmutableHashSet<ValueId> Dependencies { get; set; } = [];

        /// <summary>The driver this is the design value of, when it is one and the name resolved.</summary>
        public ScheduleRole? DesignRole { get; init; }

        /// <summary>Whether this is a <c>design</c> value rather than a parameter or a binding.</summary>
        public bool IsDesign { get; init; }
    }
}
