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
/// resolve kinds, bind parameters, build the dependency graph, evaluate. Steps 6 through 11 are
/// topology, inference, attachments, control bindings, validation and tags; they are P2.8 and have no
/// notion of expressions, exactly as steps 0–5 have no notion of topology. The split is what keeps
/// each half testable alone.
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
internal sealed class BindingRun(IComponentRegistry registry, ParseResult parse, string documentName)
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

    private ProjectSettings _project = new(null, null);
    private double? _spacing;

    public BindResult Execute()
    {
        var circuits = Partition();

        CollectDeclarations(circuits);
        Evaluate();

        var model = new SemanticModel
        {
            Circuits = [.. _circuits],
            Project = _project,
            Components = [.. _components],
            Bindings = [.. _bindings],
            Style = new StyleSettings([.. _styleTokens], _spacing),
            Deferred = [.. _deferred],
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
                        DeclareBinding(let);
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

    private void DeclareBinding(LetBindingSyntax let)
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
                ("line", LineOf(existing.Symbol.DeclarationSpan ?? declaration.Span)));
            return;
        }

        var kind = ResolveKind(declaration);
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
        };

        _components.Add(symbol);
        _componentsByName[name] = new ComponentSlot(symbol, declaration);
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
                _pending[id] = new PendingValue(parameter.Value, id, span, new ParameterTarget(kind, info));

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
            var evaluator = new ExpressionEvaluator(this, parse.Source, ImmutableArray.CreateBuilder<Diagnostic>());
            evaluator.Evaluate(pending.Expression);

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
            if (!_pending.TryGetValue(id, out var pending))
            {
                continue;
            }

            var evaluator = new ExpressionEvaluator(this, parse.Source, _diagnostics);

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
                return new ScopeLookup.UnknownName(ClosestName(head));
            }

            return _pending.TryGetValue(binding.Id, out var pending) && pending.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, binding.Id)
                : new ScopeLookup.Deferred(binding.Id);
        }

        var property = reference.Parts[^1].Name.Token.Text;

        if (!_componentsByName.TryGetValue(head, out var component))
        {
            return new ScopeLookup.UnknownName(ClosestName(head));
        }

        var kind = component.Symbol.Kind;
        if (kind is null)
        {
            return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, property));
        }

        if (!kind.Properties.ContainsKey(property))
        {
            return new ScopeLookup.UnknownProperty(
                kind.Keyword,
                [.. kind.Properties.Keys.Order(StringComparer.Ordinal)]);
        }

        // A parameter the user stated is readable at once, whatever the property's availability says:
        // `14`'s table reads "declared parameters: always, immediately", and availability describes
        // where the value comes from when nobody stated it. Reading the pending value rather than the
        // symbol's is what makes it work during evaluation, before anything has been published.
        var parameterId = new ValueId.ComponentParameter(head, property);

        if (component.Symbol.Parameters.ContainsKey(property))
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

    private sealed record ComponentSlot(ComponentSymbol Symbol, ComponentDeclarationSyntax Declaration);

    private sealed record ParameterTarget(ComponentKindInfo Kind, ParameterInfo Info);

    private sealed record PendingValue(
        ExpressionSyntax Expression, ValueId Id, TextSpan Span, ParameterTarget? Target)
    {
        public Quantity? Value { get; set; }
    }
}

/// <summary>Matches an indexed family pattern such as <c>t{index}</c> against a written name.</summary>
internal static class Indexed
{
    public static bool Matches(string pattern, string written, out int index)
    {
        index = 0;

        var placeholder = pattern.IndexOf("{index}", StringComparison.Ordinal);
        if (placeholder < 0)
        {
            return false;
        }

        var prefix = pattern[..placeholder];
        var suffix = pattern[(placeholder + "{index}".Length)..];

        if (!written.StartsWith(prefix, StringComparison.Ordinal)
            || !written.EndsWith(suffix, StringComparison.Ordinal)
            || written.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var digits = written[prefix.Length..(written.Length - suffix.Length)];

        return int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out index);
    }
}
