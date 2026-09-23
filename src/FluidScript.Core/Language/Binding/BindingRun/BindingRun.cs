using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
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
    private double? _spacing;

    public BindResult Execute()
    {
        var circuits = Partition();

        CollectCurves();
        CollectDeclarations(circuits);
        Evaluate();
        ReviewComponents();
        ReviewCurveReferences();
        ReviewLegacyReferences();
        BindTopology(circuits);

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
            Heights = _heights,
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

    private List<CircuitBlock> Partition()
    {
        var blocks = new List<CircuitBlock>();
        CircuitBlock? current = null;

        // Definitions first, so `style hot` may precede `style hot = ...` the way a `let` may be used
        // before its line (D-104).
        foreach (var definition in parse.Root.Statements.OfType<StyleDirectiveSyntax>().Where(static s => s.IsDefinition))
        {
            ReadStyle(definition);
        }

        foreach (var statement in parse.Root.Statements)
        {
            switch (statement)
            {
                case CircuitHeaderSyntax header:
                    current = new CircuitBlock(header, []);
                    blocks.Add(current);

                    // A circuit starts from the project's style, not from the previous circuit's.
                    _currentStyle = _projectStyle;
                    break;

                case ProjectDirectiveSyntax project:
                    BindProject(project);
                    break;

                case SpacingDirectiveSyntax spacing:
                    _spacing = spacing.Value.Value;
                    break;

                case StyleDirectiveSyntax { IsDefinition: true }:
                    break;

                case StyleDirectiveSyntax style:
                    _styleTokens.AddRange(style.Parts);
                    ReadStyle(style);

                    if (current is null)
                    {
                        _projectStyle = _currentStyle;
                    }

                    break;

                // File-wide, and therefore not a circuit's contents. Reaching the default arm would
                // open an implicit circuit for them, which is what `fluidscript 1` did on its own.
                case VersionDirectiveSyntax:
                case CatalogDirectiveSyntax:
                case ShowDirectiveSyntax:

                // Step 0b reads these instead, and it walks the whole file rather than one circuit's
                // block: a curve, a design point and a scenario list belong to no circuit (`D-57`,
                // `D-58`, `D-143`). Left out of this list, a file-wide line before the first `circuit`
                // header makes an implicit circuit of its own -- which is what `FS1508` was reporting.
                case CurveHeaderSyntax:
                case CurveRowSyntax:
                case DesignDirectiveSyntax:
                case ScenariosDirectiveSyntax:
                case MalformedStatementSyntax:
                    break;

                default:
                    // A statement before the first `circuit` header belongs to the implicit circuit
                    // the script gets anyway, so it is collected rather than dropped.
                    if (current is null)
                    {
                        current = new CircuitBlock(null, []);
                        blocks.Insert(0, current);
                    }

                    if (!_currentStyle.IsEmpty)
                    {
                        _styleAt[statement] = _currentStyle;
                    }

                    current.Statements.Add(statement);
                    break;
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new CircuitBlock(null, []));
        }

        AssignCircuits(blocks);

        return blocks;
    }

    private void BindProject(ProjectDirectiveSyntax project) =>
        _project = new ProjectSettings(project.Name.Text, project.Mode);

    private void AssignCircuits(List<CircuitBlock> blocks)
    {
        var used = new HashSet<int>();
        var byName = new Dictionary<string, TextSpan>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            if (block.Header?.Number?.Value is { } stated)
            {
                used.Add((int)stated);
            }
        }

        var next = 100;

        foreach (var block in blocks)
        {
            var header = block.Header;
            var name = header?.Name.Text ?? documentName;
            var span = header?.Span ?? new TextSpan(0, 0);

            if (header is null)
            {
                Report(BinderDiagnostics.NoCircuitHeader, span, ("name", name));
            }

            int number;
            var explicitNumber = header?.Number is not null;

            if (explicitNumber)
            {
                number = (int)header!.Number!.Value;

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
                && block.Statements.OfType<ScheduleHeaderSyntax>().FirstOrDefault() is { } schedule)
            {
                Report(BinderDiagnostics.ScheduleWithoutTime, schedule.Span, ("circuit", name));
            }

            _circuits.Add(new CircuitSymbol
            {
                Name = name,
                Number = number,
                NumberIsExplicit = explicitNumber,
                Substance = Substance(block),
                Mode = mode,
                Role = RoleOf(name, span),
                DeclarationSpan = span,
            });

            block.Circuit = _circuits[^1];
        }
    }

    private static string? Substance(CircuitBlock block) =>
        block.Statements.OfType<FluidDirectiveSyntax>().FirstOrDefault()?.Substance.Token.Text;

    private FluidMode ModeOf(CircuitBlock block, string name, TextSpan span)
    {
        var fluid = block.Statements.OfType<FluidDirectiveSyntax>().FirstOrDefault();
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

    private CircuitRole RoleOf(string name, TextSpan span)
    {
        var resolution = CircuitRoleRegistry.Resolve(name);

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

        foreach (var (argumentName, value) in arguments)
        {
            if (argumentName is "name" or "node" or "component")
            {
                _attributions.Add((_diagnostics.Count, value));
                break;
            }
        }

        var diagnostic = Diagnostic.Create(descriptor, span, built);
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

    private sealed record CircuitBlock(CircuitHeaderSyntax? Header, List<StatementSyntax> Statements)
    {
        public CircuitSymbol? Circuit { get; set; }
    }

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
