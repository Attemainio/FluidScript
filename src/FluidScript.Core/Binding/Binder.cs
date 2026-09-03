using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>Everything one run of the binder produced.</summary>
/// <param name="Model">The bound model, always present. A model with errors is still a model.</param>
/// <param name="Diagnostics">Everything the binder reported, in source order.</param>
public sealed record BindResult(SemanticModel Model, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Turns a syntax tree into a semantic model: resolved symbols, known kinds, typed values, and the
/// distinction between absent and given that <c>D-02</c> depends on.
/// </summary>
/// <remarks>
/// <para>
/// This runs <c>15</c>'s binding steps 0 through 5 — partition into circuits, collect declarations,
/// resolve kinds, bind parameters, build the dependency graph, evaluate. Steps 6 through 11 — ports,
/// connections, inference, attachments, control bindings, the schedule, validation and tags — are in
/// <c>BindingRun.Topology.cs</c> and have no notion of expressions, exactly as steps 0–5 have no
/// notion of topology. The split is what keeps each half testable alone.
/// </para>
/// <para>
/// Like every stage, it never throws on user input. A script under editing is malformed most of the
/// time, and a malformed script still binds: an unresolved kind produces a component with no kind, a
/// failed expression produces a parameter with no value, and the rest of the file binds around it.
/// </para>
/// </remarks>
public sealed class Binder
{
    private readonly IComponentRegistry _registry;

    /// <summary>Creates a binder over a component registry.</summary>
    /// <param name="registry">Where kinds, parameters and properties are looked up.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public Binder(IComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <summary>Binds a parsed script.</summary>
    /// <param name="parse">The parse to bind.</param>
    /// <param name="documentName">
    /// What to call the file when the script declares no circuit, used for the implicit circuit's name.
    /// </param>
    /// <returns>The model and every diagnostic binding produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parse"/> is <see langword="null"/>.</exception>
    public BindResult Bind(ParseResult parse, string documentName = "script")
    {
        ArgumentNullException.ThrowIfNull(parse);

        var run = new BindingRun(_registry, parse, documentName);
        return run.Execute();
    }
}

/// <summary>One run of the binder over one parse. Not reusable, and not shared between threads.</summary>
internal sealed partial class BindingRun(IComponentRegistry registry, ParseResult parse, string documentName)
    : IValueScope
{
    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
    private readonly List<CircuitSymbol> _circuits = [];
    private readonly List<ComponentSymbol> _components = [];
    private readonly List<BindingSymbol> _bindings = [];
    private readonly Dictionary<string, BindingSlot> _bindingsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentSlot> _componentsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<ValueId, PendingValue> _pending = [];
    private readonly DependencyGraph _graph = new();
    private readonly List<DeferredExpression> _deferred = [];
    private readonly List<StyleTokenSyntax> _styleTokens = [];

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
        BindTopology(circuits);

        var model = new SemanticModel
        {
            Circuits = [.. _circuits],
            Project = _project with { Design = PublishDesign() },
            Components = [.. _components],
            Bindings = [.. _bindings],
            Style = new StyleSettings([.. _styleTokens], _spacing),
            Connections = [.. _connections],
            ControlBindings = [.. _controlBindings],
            Disturbances = [.. _disturbances],
            SymbolMap = _symbolMap,
            Deferred = [.. _deferred],
            Curves = [.. _curves],
        };

        return new BindResult(model, [.. _diagnostics.OrderBy(static d => d.Span?.Start ?? 0)]);
    }

    // ---- step 0: circuits, and the file-wide settings -------------------------------------------

    private List<CircuitBlock> Partition()
    {
        var blocks = new List<CircuitBlock>();
        CircuitBlock? current = null;

        foreach (var statement in parse.Root.Statements)
        {
            switch (statement)
            {
                case CircuitHeaderSyntax header:
                    current = new CircuitBlock(header, []);
                    blocks.Add(current);
                    break;

                case ProjectDirectiveSyntax project:
                    BindProject(project);
                    break;

                case SpacingDirectiveSyntax spacing:
                    _spacing = spacing.Value.Value;
                    break;

                case StyleDirectiveSyntax style:
                    _styleTokens.AddRange(style.Parts);
                    break;

                // File-wide, and therefore not a circuit's contents. Reaching the default arm would
                // open an implicit circuit for them, which is what `fluidscript 1` did on its own.
                case VersionDirectiveSyntax:
                case CatalogDirectiveSyntax:
                case ShowDirectiveSyntax:

                // Step 0b reads these instead, and it walks the whole file rather than one circuit's
                // block: a curve and a design point belong to no circuit (`D-57`, `D-58`).
                case CurveHeaderSyntax:
                case CurveRowSyntax:
                case DesignDirectiveSyntax:
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
        _project = new ProjectSettings(project.Name.Token.Text, project.Mode);

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
            var name = header?.Name.Token.Text ?? documentName;
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

    // ---- steps 1-3: declarations, kinds, parameters ---------------------------------------------

    private void CollectDeclarations(List<CircuitBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements)
            {
                switch (statement)
                {
                    case LetBindingSyntax let:
                        DeclareBinding(let, block.Circuit!.Name);
                        break;

                    case ComponentDeclarationSyntax declaration:
                        DeclareComponent(declaration, block.Circuit!.Name);
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private void DeclareBinding(LetBindingSyntax let, string circuitName)
    {
        var name = let.Name.Token.Text;

        if (_bindingsByName.TryGetValue(name, out var existing))
        {
            Report(
                BinderDiagnostics.DuplicateBinding,
                let.Span,
                ("name", name),
                ("line", LineOf(existing.Declaration.Span)));
            return;
        }

        _bindingCircuits[name] = circuitName;

        var id = new ValueId.Let(name);
        _bindingsByName[name] = new BindingSlot(let, id);
        _graph.Add(id);
        _pending[id] = new PendingValue(let.Value, id, let.Span, null);
    }

    private void DeclareComponent(ComponentDeclarationSyntax declaration, string circuitName)
    {
        var name = declaration.Name.Token.Text;

        if (_componentsByName.TryGetValue(name, out var existing))
        {
            Report(
                BinderDiagnostics.DuplicateComponent,
                declaration.Span,
                ("name", name),
                ("line", LineOf(_components[existing.Index].DeclarationSpan ?? declaration.Span)));
            return;
        }

        var kind = ResolveKind(declaration);

        // `D-61`: `at` places an observer on a node, and only an observer. A component that carried
        // flow and claimed to observe a node at the same time is a shape no later stage represents,
        // and an instrument nothing placed observes nothing at all.
        if (declaration.AttachedTo is not null && kind is { IsObserver: false })
        {
            Report(
                BinderDiagnostics.NotAnObserver,
                declaration.Span,
                ("name", name),
                ("kind", kind.Keyword));
        }
        else if (declaration.AttachedTo is null && kind is { IsObserver: true })
        {
            Report(BinderDiagnostics.ObserverNotPlaced, declaration.Span, ("name", name));
        }

        var parameters = BindParameters(declaration, kind, name);

        var symbol = new ComponentSymbol
        {
            Name = name,
            Origin = new Origin.Declared(),
            Kind = kind,
            WrittenKind = declaration.Kind.Token.Text,
            Parameters = parameters,
            DeclarationSpan = declaration.Span,
            CircuitName = circuitName,
            AttachedTo = declaration.AttachedTo?.Text,
        };

        _components.Add(symbol);
        _componentsByName[name] = new ComponentSlot(_components.Count - 1, declaration);
    }

    private ComponentKindInfo? ResolveKind(ComponentDeclarationSyntax declaration)
    {
        var written = declaration.Kind.Token.Text;
        var span = declaration.Kind.Span;

        switch (registry.Resolve(written))
        {
            case KindResolution.Exact exact:
                return exact.Kind;

            case KindResolution.Similar similar:
                Report(
                    BinderDiagnostics.ResolvedBySimilarity,
                    span,
                    ("written", written),
                    ("canonical", similar.Kind.Keyword));
                return similar.Kind;

            case KindResolution.Ambiguous ambiguous:
                Report(
                    BinderDiagnostics.AmbiguousKind,
                    span,
                    ("written", written),
                    ("first", ambiguous.Candidates[0].Keyword),
                    ("second", ambiguous.Candidates[1].Keyword));
                return null;

            case KindResolution.Unknown { SuggestedKeyword: { } suggestion }:
                Report(
                    BinderDiagnostics.UnknownKind,
                    span,
                    new Suggestion($"Change it to '{suggestion}'", span, suggestion),
                    ("kind", written));
                return null;

            default:
                Report(BinderDiagnostics.UnknownKind, span, ("kind", written));
                return null;
        }
    }

    private ImmutableDictionary<string, ParameterValue> BindParameters(
        ComponentDeclarationSyntax declaration,
        ComponentKindInfo? kind,
        string componentName)
    {
        var bound = ImmutableDictionary.CreateBuilder<string, ParameterValue>(StringComparer.Ordinal);

        foreach (var parameter in declaration.Parameters)
        {
            var written = parameter.Name.Token.Text;

            // With no kind there is nothing to check a parameter against, so it is kept as written and
            // nothing is reported: the user already has one error on this line about the kind, and a
            // second one per parameter would bury it.
            if (kind is null)
            {
                bound[written] = new ParameterValue
                {
                    WrittenName = written,
                    Expression = parameter.Value,
                    Span = parameter.Span,
                };
                continue;
            }

            if (ResolveParameter(kind, written, parameter) is not { } info)
            {
                continue;
            }

            var value = BindParameterValue(kind, info, parameter, componentName, written);
            if (value is not null)
            {
                bound[info.Name] = value;
            }
        }

        return bound.ToImmutable();
    }

    private ParameterInfo? ResolveParameter(
        ComponentKindInfo kind, string written, ParameterSyntax parameter)
    {
        if (kind.Parameters.TryGetValue(written, out var exact))
        {
            return exact;
        }

        foreach (var candidate in kind.Parameters.Values)
        {
            if (candidate.Aliases.Contains(written, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        // An indexed family member — `t3`, `in2_elevation` — is matched against its pattern before
        // similarity, so a tank's fortieth layer is an index error rather than an unknown parameter.
        foreach (var family in kind.IndexedParameterFamilies)
        {
            if (!Indexed.Matches(family.Pattern, written, out var index))
            {
                continue;
            }

            var max = family.MaxIndex;
            if (index < family.MinIndex || (max is not null && index > max))
            {
                Report(
                    BinderDiagnostics.IndexOutsideFamily,
                    parameter.Span,
                    ("written", written),
                    ("kind", kind.Keyword),
                    ("min", family.MinIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("max", (max ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return null;
            }

            return family.Element with { Name = written };
        }

        var index2 = kind.Parameters.ToImmutableDictionary(
            static entry => NameResolution.Normalize(entry.Key),
            static entry => entry.Value,
            StringComparer.Ordinal);

        var match = NameResolution.Match(written, index2);

        if (!match.IsExact && match.Best is not null && match.BestScore >= NameResolution.ResolveThreshold
            && match.IsClear)
        {
            Report(
                BinderDiagnostics.ResolvedBySimilarity,
                parameter.Span,
                ("written", written),
                ("canonical", match.Best.Name));
            return match.Best;
        }

        Report(
            BinderDiagnostics.UnknownParameter,
            parameter.Span,
            ("kind", kind.Keyword),
            ("parameter", written),
            ("available", string.Join(", ", kind.Parameters.Keys.Order(StringComparer.Ordinal))));

        return null;
    }

    private ParameterValue? BindParameterValue(
        ComponentKindInfo kind,
        ParameterInfo info,
        ParameterSyntax parameter,
        string componentName,
        string written)
    {
        var span = parameter.Span;

        switch (info.ValueKind)
        {
            case ParameterValueKind.Symbol:
            {
                var name = (parameter.Value as ReferenceSyntax)?.Head.Token.Text;

                if (name is null || !info.AcceptedSymbols.Contains(name, StringComparer.Ordinal))
                {
                    Report(
                        BinderDiagnostics.UnacceptedSymbol,
                        span,
                        ("parameter", info.Name),
                        ("available", string.Join(", ", info.AcceptedSymbols)),
                        ("written", name ?? parse.Source.ToString(parameter.Value.Span).Trim()));
                    return null;
                }

                return new ParameterValue
                {
                    WrittenName = written,
                    Symbol = name,
                    Expression = parameter.Value,
                    Span = span,
                };
            }

            case ParameterValueKind.Reference:
            {
                if (parameter.Value is not ReferenceSyntax { Parts.Length: > 0 } reference)
                {
                    Report(BinderDiagnostics.ExpectedReference, span, ("parameter", info.Name));
                    return null;
                }

                return new ParameterValue
                {
                    WrittenName = written,
                    Reference = new PropertyReference(
                        reference.Head.Token.Text,
                        reference.Parts[^1].Name.Token.Text),
                    Expression = parameter.Value,
                    Span = span,
                };
            }

            default:
            {
                var id = new ValueId.ComponentParameter(componentName, info.Name);
                _graph.Add(id);
                _pending[id] = new PendingValue(parameter.Value, id, span, new ParameterTarget(componentName, kind, info));

                return new ParameterValue
                {
                    WrittenName = written,
                    Expression = parameter.Value,
                    Span = span,
                };
            }
        }
    }

    // ---- steps 4-5: the dependency graph, then evaluation ---------------------------------------

    private void Evaluate()
    {
        // Dependencies are discovered by evaluating, and evaluation needs the order dependencies give.
        // The way out is that a first pass records edges without using any value: an expression that
        // reads an unevaluated binding sees no value and reports nothing, because nothing is asked of
        // it yet.
        foreach (var pending in _pending.Values)
        {
            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                ImmutableArray.CreateBuilder<Diagnostic>(),
                pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);
            evaluator.Evaluate(pending.Expression);

            // Kept on the pending value as well, so a later pass can ask what an expression read
            // without evaluating it a third time. That is how a curve reference is found again at the
            // site that made it, which is where reading one has to be reported.
            pending.Dependencies = evaluator.Dependencies;

            foreach (var dependency in evaluator.Dependencies)
            {
                _graph.AddDependency(pending.Id, dependency);
            }
        }

        if (_graph.TopologicalOrder() is OrderResult.Cyclic cyclic)
        {
            ReportCycle(cyclic);
            return;
        }

        var order = ((OrderResult.Ordered)_graph.TopologicalOrder()).Order;

        foreach (var id in order)
        {
            // A curve is evaluated here rather than in step 0b, because the order it needs is the one
            // the graph gives: a curve driving another has already been read by the time this reaches
            // the second (`D-57`).
            if (id is ValueId.Curve curve)
            {
                EvaluateCurve(curve);
                continue;
            }

            if (!_pending.TryGetValue(id, out var pending))
            {
                continue;
            }

            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                _diagnostics,
                pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);

            switch (evaluator.Evaluate(pending.Expression))
            {
                case EvaluationResult.Value value:
                    Store(pending, value);
                    break;

                case EvaluationResult.Deferred deferred:
                    _deferred.Add(new DeferredExpression(pending.Expression, id, null, deferred.Dependencies));
                    break;

                default:
                    break;
            }
        }

        Publish();
    }

    private void Store(PendingValue pending, EvaluationResult.Value value)
    {
        var quantity = value.Quantity;

        // A design value is not a parameter, and its driver's role is what checks it (`D-59`). The
        // role's dimension makes `design tout=-26` and `design tout=-26 C` the same point, and
        // `design tout=3 bar` a mismatch rather than a silent reinterpretation; a role with no
        // dimension takes its value bare and checks nothing.
        if (pending.IsDesign)
        {
            if (pending.DesignRole?.Dimension is { } dimension)
            {
                if (value.IsBare)
                {
                    quantity = Quantity.FromBareNumber(quantity.SiValue, dimension);
                }
                else if (quantity.Dimension != dimension)
                {
                    Report(
                        BinderDiagnostics.ParameterDimensionMismatch,
                        pending.Span,
                        ("parameter", pending.DesignRole.CanonicalName),
                        ("expected", dimension.Name.ToLowerInvariant()),
                        ("value", parse.Source.ToString(pending.Expression.Span).Trim()),
                        ("actual", quantity.Dimension.Name.ToLowerInvariant()));
                    return;
                }
            }

            pending.Value = quantity;
            return;
        }

        if (pending.Target is { } target)
        {
            // A bare number takes the parameter's canonical unit (`D-14`), which is what makes
            // `power=30` thirty kilowatts and `power=30 kW` the same quantity. Reinterpreting here
            // rather than in the evaluator keeps the evaluator ignorant of what it is being assigned
            // to, so the same expression means the same thing wherever it is written.
            if (value.IsBare && target.Info.ValueKind == ParameterValueKind.Quantity)
            {
                quantity = Quantity.FromBareNumber(quantity.SiValue, target.Info.Dimension);
            }
            else if (quantity.Dimension != target.Info.Dimension)
            {
                Report(
                    BinderDiagnostics.ParameterDimensionMismatch,
                    pending.Span,
                    ("parameter", target.Info.Name),
                    ("expected", target.Info.Dimension.Name.ToLowerInvariant()),
                    ("value", parse.Source.ToString(pending.Expression.Span).Trim()),
                    ("actual", quantity.Dimension.Name.ToLowerInvariant()));
                return;
            }

            CheckRange(target, quantity, pending.Span);
        }

        pending.Value = quantity;
    }

    private void CheckRange(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        // Order matters, and it is the order of severity. A value outside a hard bound or with the
        // wrong sign is already reported as an error, and adding "and it is outside the usual range"
        // underneath would be two diagnostics for one mistake, the second of them redundant.
        if (CheckValidity(target, quantity, span) || CheckSign(target, quantity, span))
        {
            return;
        }

        if (target.Info.UsualRange is not { } range || range.Contains(quantity.SiValue))
        {
            return;
        }

        var unit = UnitTable.CanonicalUnitFor(target.Info.Dimension);
        var shown = unit is null ? quantity.SiValue : quantity.ValueIn(unit);
        var low = unit is null ? range.Min : Quantity.FromSi(range.Min, target.Info.Dimension).ValueIn(unit);
        var high = unit is null ? range.Max : Quantity.FromSi(range.Max, target.Info.Dimension).ValueIn(unit);

        Report(
            BinderDiagnostics.ValueOutsideUsualRange,
            span,
            ("parameter", target.Info.Name),
            ("value", Format(shown, unit?.Text)),
            ("low", Format(low, null)),
            ("high", Format(high, unit?.Text)));
    }

    /// <summary>Reports a value outside the range in which its parameter means anything.</summary>
    /// <param name="target">The component and parameter the value was written for.</param>
    /// <param name="quantity">The evaluated value.</param>
    /// <param name="span">Where the assignment sits in the source.</param>
    /// <returns><see langword="true"/> when a diagnostic was reported.</returns>
    /// <remarks>
    /// Which code is raised comes from the registry rather than from a branch here, so <c>FS2105</c>,
    /// <c>FS2108</c>, <c>FS2114</c> and <c>FS2115</c> are one check site and four rows.
    /// </remarks>
    private bool CheckValidity(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        if (target.Info.Validity is not { } validity)
        {
            return false;
        }

        var value = quantity.SiValue;

        if (validity.Range.Contains(value) && (!validity.RequiresWholeNumber || double.IsInteger(value)))
        {
            return false;
        }

        Report(
            validity.Descriptor,
            span,
            ("name", target.Owner),
            ("parameter", target.Info.Name),
            ("value", Format(value, null)),
            ("low", Format(validity.Range.Min, null)),
            ("high", Format(validity.Range.Max, null)));

        return true;
    }

    /// <summary>Reports a negative value for a parameter whose declared range starts at or above zero.</summary>
    /// <param name="target">The component and parameter the value was written for.</param>
    /// <param name="quantity">The evaluated value.</param>
    /// <param name="span">Where the assignment sits in the source.</param>
    /// <returns><see langword="true"/> when a diagnostic was reported.</returns>
    /// <remarks>
    /// <para>
    /// The usual range doubles as the declaration of sign, which is why <c>power</c> (-100 to 100 kW)
    /// takes a negative and <c>dt</c> (0.1 to 200 K) does not. A duty's direction is <c>power</c>'s
    /// sign and nothing else's, so <c>power=-70 dt=20</c> is a cooler and <c>dt=-20</c> is an error.
    /// </para>
    /// <para>
    /// <strong>Absolute temperatures are exempt, and the exemption is not a convenience.</strong> A
    /// temperature parameter's range is stated in °C and held in K, so its lower bound is 223.15 and
    /// every ordinary value is positive; a value that did reach below zero would be below absolute
    /// zero, and "t cannot be negative" is the wrong sentence for it when <c>t=-50</c> is legal.
    /// <c>FS1306</c> reports that case as the out-of-range value it is.
    /// </para>
    /// </remarks>
    private bool CheckSign(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        if (quantity.SiValue >= 0
            || target.Info.Dimension == Dimension.Temperature
            || target.Info.UsualRange is not { Min: >= 0 })
        {
            return false;
        }

        Report(BinderDiagnostics.NegativeValue, span, ("parameter", target.Info.Name));

        return true;
    }

    private void ReportCycle(OrderResult.Cyclic cyclic)
    {
        var first = cyclic.Cycle[0];
        var span = _pending.TryGetValue(first, out var pending) ? pending.Span : new TextSpan(0, 0);

        Report(
            BinderDiagnostics.CyclicDependency,
            span,
            ("name", first.ToString()!),
            ("cycle", string.Join(" → ", cyclic.Cycle)));
    }

    private void Publish()
    {
        foreach (var (name, slot) in _bindingsByName)
        {
            _pending.TryGetValue(slot.Id, out var pending);
            _bindings.Add(new BindingSymbol(
                name, slot.Declaration.Value, slot.Id, pending?.Value, slot.Declaration.Span));
        }

        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];
            var parameters = component.Parameters.ToBuilder();

            foreach (var (canonical, value) in component.Parameters)
            {
                var id = new ValueId.ComponentParameter(component.Name, canonical);

                if (_pending.TryGetValue(id, out var pending) && pending.Value is { } quantity)
                {
                    parameters[canonical] = value with { Value = quantity };
                }
            }

            _components[i] = component with { Parameters = parameters.ToImmutable() };
        }
    }

    // ---- the scope an expression is evaluated against -------------------------------------------

    public ScopeLookup Lookup(ReferenceSyntax reference)
    {
        var head = reference.Head.Token.Text;

        if (reference.Parts.IsEmpty)
        {
            if (!_bindingsByName.TryGetValue(head, out var binding))
            {
                // A curve reference is an ordinary value source, which is the whole reason the feature
                // costs so little here: it resolves exactly as a `let` does and yields a *bare*
                // number, so `D-14`'s rule reinterprets it in the target parameter's canonical unit at
                // assignment. That is what lets one curve drive a power, a percentage and a
                // temperature without being told which.
                if (_curvesByName.ContainsKey(head))
                {
                    var curve = new ValueId.Curve(head);

                    return _curveValues.GetValueOrDefault(head) is { } y
                        ? new ScopeLookup.Value(
                            Quantity.FromSi(y, Dimension.Dimensionless), IsBare: true, curve)
                        : new ScopeLookup.Deferred(curve);
                }

                return new ScopeLookup.UnknownName(ClosestName(head));
            }

            return _pending.TryGetValue(binding.Id, out var pending) && pending.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, binding.Id)
                : new ScopeLookup.Deferred(binding.Id);
        }

        var property = reference.Parts[^1].Name.Token.Text;

        if (!_componentsByName.TryGetValue(head, out var slot))
        {
            return new ScopeLookup.UnknownName(ClosestName(head));
        }

        var component = _components[slot.Index];
        var kind = component.Kind;
        if (kind is null)
        {
            return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, property));
        }

        // Through the kind rather than against `Properties` directly, so an indexed family member --
        // a tank's `t3`, `in2_t` -- resolves like the fixed names beside it.
        if (kind.ResolveProperty(property) is null)
        {
            return new ScopeLookup.UnknownProperty(kind.Keyword, [.. kind.ReadableNames]);
        }

        // A parameter the user stated is readable at once, whatever the property's availability says:
        // `14`'s table reads "declared parameters: always, immediately", and availability describes
        // where the value comes from when nobody stated it. Reading the pending value rather than the
        // symbol's is what makes it work during evaluation, before anything has been published.
        var parameterId = new ValueId.ComponentParameter(head, property);

        if (component.Parameters.ContainsKey(property))
        {
            return _pending.TryGetValue(parameterId, out var stated) && stated.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, parameterId)
                : new ScopeLookup.Deferred(parameterId);
        }

        // Sized or solved, and nobody stated it: this is the deferral `14`'s two-phase evaluation
        // exists for, not an error.
        return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, property));
    }

    private string? ClosestName(string written)
    {
        var index = _bindingsByName.Keys.Concat(_componentsByName.Keys)
            .ToImmutableDictionary(NameResolution.Normalize, static name => name, StringComparer.Ordinal);

        if (index.IsEmpty)
        {
            return null;
        }

        var match = NameResolution.Match(written, index);

        return match.Best is not null && match.BestScore >= NameResolution.SuggestionFloor
            ? match.Best
            : null;
    }

    // ---- reporting -------------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, params (string Name, string Value)[] arguments) =>
        Report(descriptor, span, null, arguments);

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

/// <summary>Matches an indexed family pattern such as <c>t{index}</c> against a written name.</summary>
internal static class Indexed
{
    /// <summary>Tells whether a written name is a member of a pattern's family, and which one.</summary>
    /// <param name="pattern">The canonical pattern, with one <c>{index}</c> placeholder.</param>
    /// <param name="written">The name to test.</param>
    /// <param name="index">The index it carries, or zero when it is not a member.</param>
    /// <returns><see langword="true"/> when the name matches the pattern.</returns>
    /// <remarks>
    /// A forwarder, kept because the parameter path reads better calling <c>Indexed.Matches</c> in a
    /// file about binding. The rule itself moved to the registry when property families needed it
    /// too: <see cref="ComponentKindInfo.ResolveProperty"/> is read by the model contract as well as
    /// by this class, and two copies of the pattern rule is one place for the two halves to diverge.
    /// </remarks>
    public static bool Matches(string pattern, string written, out int index) =>
        IndexedName.Matches(pattern, written, out index);
}
