using System.Collections.Immutable;
using System.Globalization;

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

    // ---- steps 1-3: declarations, kinds, parameters ---------------------------------------------

    private void CollectDeclarations(List<CircuitBlock> blocks)
    {
        // I7 (D-110): the implicit pipe each connection on a line carrying properties lowers to, declared with
        // the components so its parameters are evaluated with everyone else's. Named after its two ends as an
        // I2 node is, with an ordinal when the pair recurs; the line's properties are its stated parameters,
        // bound against the pipe's registry entry like a declaration's. A length it does not state is zero,
        // the factory's decided default for an implicit pipe. BindConnections wires it in by its key: the
        // line's position and the pair's index, which is what makes the name stable for the same script.
        void DeclareImplicitPipes(ConnectionSyntax connection, string circuit)
        {
            var endpoints = connection.Endpoints;

            for (var i = 0; i + 1 < endpoints.Length; i++)
            {
                var stem = $"{endpoints[i].Component.Token.Text}__{endpoints[i + 1].Component.Token.Text}";
                var name = stem;

                for (var ordinal = 2; _componentsByName.ContainsKey(name); ordinal++)
                {
                    name = $"{stem}_{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                var kind = registry.Resolve("pipe") is KindResolution.Exact exact ? exact.Kind : null;
                var key = $"{connection.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                var pipe = new ComponentSymbol
                {
                    Name = name,
                    Origin = new Origin.Inferred("I7", key),
                    Kind = kind,
                    WrittenKind = "pipe",
                    Parameters = BindParameters(connection.Parameters, kind, name),
                    // The connection line is the pipe's declaration (C-97): a click on the drawn pipe lands there.
                    DeclarationSpan = connection.Span,
                    CircuitName = circuit,
                    Ports = [.. (kind?.Ports ?? []).Select(static port => port.Key)],
                };

                Register(pipe, null);
                Report(BinderDiagnostics.ComponentInferred, connection.Span, ("kind", "pipe"), ("name", name), ("rule", "I7"));
            }
        }

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

                    case ConnectionSyntax { Parameters.Length: > 0 } connection:
                        DeclareImplicitPipes(connection, block.Circuit!.Name);
                        break;

                    default:
                        break;
                }
            }
        }

        // I1, brought forward (L-63): an endpoint naming nothing declared is a node, and it was one
        // only once the topology pass ran -- after every expression had been evaluated, so `N3.t` on a
        // node the script never declared was "nothing named". The node is declared here, with every
        // `let` and component already known, and the topology pass finds it as it would a declared one.
        // A name a `let` holds is left for the topology pass to refuse (FS1523), not absorbed.
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is not ConnectionSyntax connection)
                {
                    continue;
                }

                foreach (var endpoint in connection.Endpoints)
                {
                    var name = endpoint.Component.Token.Text;

                    if (!_componentsByName.ContainsKey(name) && !_bindingsByName.ContainsKey(name))
                    {
                        Infer(name, "I1", block.Circuit!.Name, endpoint.Span);
                    }
                }
            }
        }
    }

    private void DeclareBinding(LetBindingSyntax let, string circuitName)
    {
        var name = let.Name.Text;

        if (Constants.TryGet(name, out _))
        {
            Report(
                BinderDiagnostics.BuiltInConstantRedefined,
                let.Span,
                ("name", name),
                ("what", Constants.Describe(name)),
                ("value", Constants.Spell(name)));
            return;
        }

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
        var name = declaration.Name.Text;

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

        var parameters = BindParameters(declaration.Parameters, kind, name);
        DeclareSizingPoint(declaration, name, parameters);

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
            Style = StyleOf(declaration),
        };

        Register(symbol, declaration);
    }

    /// <summary>Adds a component to the symbol table: its slot is its index in the component list, and the name map claims it in the same step, so the two can never disagree.</summary>
    /// <param name="symbol">The component.</param>
    /// <param name="declaration">Its declaration, or <see langword="null"/> when a rule inferred it.</param>
    /// <returns>The slot.</returns>
    private ComponentSlot Register(ComponentSymbol symbol, ComponentDeclarationSyntax? declaration)
    {
        _components.Add(symbol);
        var slot = new ComponentSlot(_components.Count - 1, declaration);
        _componentsByName[symbol.Name] = slot;
        return slot;
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
        ImmutableArray<ParameterSyntax> parameters,
        ComponentKindInfo? kind,
        string componentName)
    {
        var bound = ImmutableDictionary.CreateBuilder<string, ParameterValue>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            var written = parameter.Name.Text;

            // `style=name` is presentation every kind accepts (D-104); it is read by DeclareComponent
            // and is not a registry parameter, so it is neither bound nor reported here.
            if (string.Equals(written, "style", StringComparison.Ordinal))
            {
                continue;
            }

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
                // Stored under the key, which is what every reader of a component's stated
                // parameters has always used; the name is the script's and the docs' (`D-120`).
                bound[info.Key] = value;
            }
        }

        return bound.ToImmutable();
    }

    /// <summary>The style a declaration carries: the one in force where it was written, then its own <c>style=</c>.</summary>
    private StyleSpec? StyleOf(ComponentDeclarationSyntax declaration)
    {
        var style = _styleAt.GetValueOrDefault(declaration);

        foreach (var parameter in declaration.Parameters)
        {
            if (!string.Equals(parameter.Name.Text, "style", StringComparison.Ordinal))
            {
                continue;
            }

            var name = (parameter.Value as ReferenceSyntax)?.Head.Token.Text ?? parse.Source.ToString(parameter.Value.Span).Trim();

            if (_styleDefinitions.TryGetValue(name, out var defined))
            {
                style = (style ?? StyleSpec.Empty).Merge(defined);
            }
            else
            {
                Report(StyleDiagnostics.UndefinedStyle, parameter.Span, ("name", name));
            }
        }

        return style;
    }

    private void ReadStyle(StyleDirectiveSyntax style)
    {
        var reported = (DiagnosticDescriptor descriptor, TextSpan span, (string Name, string Value)[] arguments) => Report(descriptor, span, arguments);

        if (style.Name is { } name)
        {
            if (_styleDefinitions.ContainsKey(name.Text))
            {
                Report(StyleDiagnostics.RedefinedStyle, name.Span, ("name", name.Text));
            }

            _styleDefinitions[name.Text] = StyleTokens.Classify(style.Parts, reported);
            return;
        }

        // A single bare word that names a defined style applies it; any other token list is an
        // anonymous style read for what its tokens are.
        if (style.Parts is [{ Kind: StyleTokenKind.Word } word] && !NamedColours.TryGet(word.Text, out _)
            && word.Text is not ("fillet" or "round" or "sharp"))
        {
            if (_styleDefinitions.TryGetValue(word.Text, out var defined))
            {
                _currentStyle = _currentStyle.Merge(defined);
            }
            else
            {
                Report(StyleDiagnostics.UndefinedStyle, word.Span, ("name", word.Text));
            }

            return;
        }

        _currentStyle = _currentStyle.Merge(StyleTokens.Classify(style.Parts, reported));
    }

    private ParameterInfo? ResolveParameter(
        ComponentKindInfo kind, string written, ParameterSyntax parameter)
    {
        // A node has one state and no ports (`D-120` rule 4): `in.t=` on one is the one shape the
        // registry cannot explain by listing what it accepts, because the quantity is right and the
        // port is the mistake.
        if (kind.HasUnlimitedPorts && parameter.Name.Parts.Length > 0)
        {
            Report(
                BinderDiagnostics.PortStateOnNode,
                parameter.Name.Span,
                ("kind", kind.Keyword),
                ("quantity", parameter.Name.Parts[^1].Name.Text),
                ("written", written));
            return null;
        }

        // Name, alias, the spelling `D-120` retired, or an indexed family member -- `layer[3].t`,
        // `in[2].level`, the old `t3` -- all before similarity, so a tank's fortieth layer is an index
        // error rather than an unknown parameter, and an old spelling is a suggestion rather than a
        // near miss.
        if (kind.ResolveParameter(written, out var suggestion, out var outside) is { } resolved)
        {
            if (suggestion is not null)
            {
                ReportLegacySpelling(parameter.Name.Span, written, suggestion);
            }

            return resolved;
        }

        if (outside is { } family)
        {
            Report(
                BinderDiagnostics.IndexOutsideFamily,
                parameter.Span,
                ("written", written),
                ("kind", kind.Keyword),
                ("min", Math.Min(1, family.MinIndex).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("max", (family.MaxIndex ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return null;
        }

        // A port state whose quantity the table names is never a near miss: `in.p` is one edit from
        // `in.t` and was read as it, so a stated pressure became a temperature of 300 °C under an
        // information notice (FS1512). The answer is what this port takes.
        if (parameter.Name.Parts.Length > 0
            && written.LastIndexOf('.') is var dot and > 0
            && PropertyTable.Find(written[(dot + 1)..]) is { } quantity)
        {
            var port = written[..dot];
            var takes = kind.Parameters.Values
                .SelectMany(static info => info.Aliases.Prepend(info.Name))
                .Concat(kind.IndexedParameterFamilies.Select(static family => family.Pattern))
                .Where(name => name.StartsWith(port + ".", StringComparison.Ordinal))
                .Select(name => name[(port.Length + 1)..])
                .Order(StringComparer.Ordinal)
                .ToArray();

            Report(
                takes.Length == 0 ? BinderDiagnostics.UnknownParameter : BinderDiagnostics.UnknownPortQuantity,
                parameter.Name.Span,
                ("kind", kind.Keyword),
                ("parameter", written),
                ("port", port),
                ("quantity", quantity.Symbol),
                ("available", string.Join(", ", takes.Length == 0 ? kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal) : takes)));
            return null;
        }

        var index2 = kind.Parameters.Values.ToImmutableDictionary(
            static info => NameResolution.Normalize(info.Name),
            static info => info,
            StringComparer.Ordinal);

        var match = NameResolution.Match(written, index2);

        // A diagnostic about a name underlines the name (44), so a quick fix that replaces the range
        // replaces the name and keeps the value (L-53).
        if (!match.IsExact && match.Best is not null && match.BestScore >= NameResolution.ResolveThreshold
            && match.IsClear)
        {
            Report(
                BinderDiagnostics.ResolvedBySimilarity,
                parameter.Name.Span,
                ("written", written),
                ("canonical", match.Best.Name));
            return match.Best;
        }

        Report(
            BinderDiagnostics.UnknownParameter,
            parameter.Name.Span,
            ("kind", kind.Keyword),
            ("parameter", written),
            ("available", string.Join(", ", kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal))));
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
                        reference.PropertyPath()),
                    Expression = parameter.Value,
                    Span = span,
                };
            }

            default:
            {
                if (parameter.Value is ScenarioListSyntax list)
                {
                    return BindScenarioList(kind, info, parameter, list, componentName, written);
                }

                var id = new ValueId.ComponentParameter(componentName, info.Key);
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

    /// <summary>Binds one value per declared scenario (<c>D-143</c>).</summary>
    /// <param name="kind">The component's kind.</param>
    /// <param name="info">The parameter's registry row.</param>
    /// <param name="parameter">The whole <c>name=[...]</c>.</param>
    /// <param name="list">Its bracketed values.</param>
    /// <param name="componentName">The component's name.</param>
    /// <param name="written">The spelling the file used.</param>
    /// <returns>The bound parameter, or <see langword="null"/> when the list binds to nothing.</returns>
    /// <remarks>
    /// <para>
    /// Each element becomes its own node in the dependency graph, because each may read a curve or a
    /// <c>let</c> the others do not. The design scenario's element also takes the ordinary
    /// <see cref="ValueId.ComponentParameter"/> id, which is what keeps <see cref="ParameterValue.Value"/>
    /// a scalar filled by the path that already existed -- so nothing downstream of the binder learns
    /// that scenarios exist, and projecting to another case is one rewrite of that one field.
    /// </para>
    /// <para>
    /// <strong>Length is checked before anything binds, and nothing is padded.</strong> A list of the
    /// wrong length binds no value at all rather than a partial one: a parameter half-bound across
    /// cases would size a plant from cases the file never stated.
    /// </para>
    /// </remarks>
    private ParameterValue? BindScenarioList(
        ComponentKindInfo kind,
        ParameterInfo info,
        ParameterSyntax parameter,
        ScenarioListSyntax list,
        string componentName,
        string written)
    {
        var span = parameter.Span;
        var declared = _scenarios.Count;

        if (declared == 0)
        {
            Report(BinderDiagnostics.ScenarioListWithoutScenarios, span, ("written", written));
            return null;
        }

        if (list.Elements.Length != declared)
        {
            Report(
                BinderDiagnostics.ScenarioCountMismatch,
                span,
                ("written", written),
                ("given", list.Elements.Length.ToString(CultureInfo.InvariantCulture)),
                ("count", declared.ToString(CultureInfo.InvariantCulture)),
                ("names", string.Join(", ", _scenarios)));
            return null;
        }

        // Which element fills `Value`. Already reported as FS1542 or FS1543 when it is not settled,
        // and the first element stands in so the rest of the bind has a scalar to work from rather
        // than a second failure on every parameter in the file.
        var design = _designScenario is { } named
            ? Math.Max(0, _scenarios.IndexOf(named.Name))
            : 0;

        var target = new ParameterTarget(componentName, kind, info);
        var elements = ImmutableArray.CreateBuilder<ParameterValue>(declared);

        for (var index = 0; index < declared; index++)
        {
            var element = list.Elements[index].Value;
            ValueId id = index == design
                ? new ValueId.ComponentParameter(componentName, info.Key)
                : new ValueId.ScenarioParameter(componentName, info.Key, index);

            _graph.Add(id);
            _pending[id] = new PendingValue(element, id, element.Span, target);

            elements.Add(new ParameterValue
            {
                WrittenName = written,
                Expression = element,
                Span = element.Span,
            });
        }

        return new ParameterValue
        {
            WrittenName = written,
            Expression = parameter.Value,
            Span = span,
            Scenarios = elements.MoveToImmutable(),
        };
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
            _evaluating = pending.Id;
            evaluator.Evaluate(pending.Expression);
            _evaluating = null;

            // Kept on the pending value as well, so a later pass can ask what an expression read
            // without evaluating it a third time. That is how a curve reference is found again at the
            // site that made it, which is where reading one has to be reported.
            pending.Dependencies = evaluator.Dependencies;

            foreach (var dependency in evaluator.Dependencies)
            {
                _graph.AddDependency(pending.Id, dependency);
            }
        }

        switch (_graph.TopologicalOrder())
        {
            case OrderResult.Cyclic cyclic:
                ReportCycle(cyclic);
                return;

            case OrderResult.Ordered ordered:
                EvaluateInOrder(ordered.Order);
                return;
        }
    }

    /// <summary>Step 5: evaluates every value in the order the graph gave, curves included.</summary>
    private void EvaluateInOrder(ImmutableArray<ValueId> order)
    {
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

            // Named while it reads, so a curve reference can tell whose parameter is asking and read
            // the curve at that component's own sizing point (`D-94`).
            _evaluating = pending.Id;
            var result = evaluator.Evaluate(pending.Expression);
            _evaluating = null;

            switch (result)
            {
                case EvaluationResult.Value value:
                    Store(pending, value);
                    break;

                case EvaluationResult.Deferred deferred:
                    _deferred.Add(new DeferredExpression(pending.Expression, id, null, deferred.Dependencies) { Source = parse.Source });
                    _deferredTargets.Add(id);
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
            else if (!Quantity.TryAssign(quantity, target.Info.Dimension, out quantity))
            {
                Report(
                    BinderDiagnostics.ParameterDimensionMismatch,
                    pending.Span,
                    ("parameter", target.Info.Name),
                    ("expected", BinderDiagnostics.Expected(target.Info.Dimension)),
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
                name,
                slot.Declaration.Value,
                slot.Id,
                pending?.Value,
                slot.Declaration.Span,
                pending?.Value?.Dimension ?? DimensionOf(slot.Declaration.Value, [name])));
        }

        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];
            var parameters = component.Parameters.ToBuilder();

            foreach (var (canonical, value) in component.Parameters)
            {
                var bound = value;
                var id = new ValueId.ComponentParameter(component.Name, canonical);

                if (_pending.TryGetValue(id, out var pending) && pending.Value is { } quantity)
                {
                    bound = bound with
                    {
                        Value = quantity,
                        Basis = SizingBasis(component.Name, pending),
                    };
                    CheckRoleSign(component, canonical, quantity, pending.Span);
                }

                // The other cases of a list (`D-143`). The design case's element shares the id above,
                // so it is filled from that same evaluation rather than a second one -- one expression
                // with two homes, and a projection that found the design slot empty would read the
                // case the file operates at as unstated.
                if (!bound.Scenarios.IsEmpty)
                {
                    var design = Math.Max(0, _scenarios.IndexOf(_designScenario?.Name ?? string.Empty));
                    var elements = bound.Scenarios.ToBuilder();

                    for (var slot = 0; slot < elements.Count; slot++)
                    {
                        var (element, at) = slot == design
                            ? (bound.Value, pending)
                            : (_pending.TryGetValue(
                                   new ValueId.ScenarioParameter(component.Name, canonical, slot),
                                   out var each)
                                   ? each.Value
                                   : null,
                               null);

                        if (element is { } settled)
                        {
                            elements[slot] = elements[slot] with
                            {
                                Value = settled,
                                Basis = at is null ? elements[slot].Basis : SizingBasis(component.Name, at),
                            };
                        }
                    }

                    bound = bound with { Scenarios = elements.ToImmutable() };
                }

                if (!ReferenceEquals(bound, value))
                {
                    parameters[canonical] = bound;
                }
            }

            _components[i] = component with
            {
                Parameters = parameters.ToImmutable(),
                SizingPoint = PublishSizingPoint(component.Name),
            };
        }
    }

    /// <summary>Reports a negative <c>power</c> on a role spelling, where the word already carries the sign.</summary>
    /// <remarks>
    /// Checked at publication rather than in <see cref="CheckRange"/> because the range check knows the
    /// parameter and not the word the component was declared with; the lowering that applies the
    /// magnitude (<c>D-91</c>) reads <see cref="ComponentSymbol.WrittenKind"/> the same way.
    /// </remarks>
    private void CheckRoleSign(ComponentSymbol component, string canonical, Quantity quantity, TextSpan span)
    {
        if (!string.Equals(canonical, "power", StringComparison.Ordinal) || quantity.SiValue >= 0)
        {
            return;
        }

        var written = NameResolution.Normalize(component.WrittenKind);

        if (written is not ("load" or "cooler" or "radiator" or "chiller" or "heater" or "boiler"))
        {
            return;
        }

        var unit = UnitTable.CanonicalUnitFor(quantity.Dimension);
        var shown = unit is null ? quantity.SiValue : quantity.ValueIn(unit);

        Report(
            BinderDiagnostics.SignedRoleCapacity,
            span,
            ("component", component.Name),
            ("kind", written),
            ("value", Format(shown, unit?.Text)),
            ("magnitude", Format(Math.Abs(shown), unit?.Text)));
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

                    return CurveValueFor(head) is { } y
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

        var written = reference.PropertyPath();

        if (!_componentsByName.TryGetValue(head, out var slot))
        {
            return new ScopeLookup.UnknownName(ClosestName(head));
        }

        var component = _components[slot.Index];
        var kind = component.Kind;
        if (kind is null)
        {
            return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, written));
        }

        // Through the kind rather than against `Properties` directly, so an indexed family member --
        // a tank's `layer[3].t`, `in[2].t` -- resolves like the fixed names beside it. The old
        // spellings resolve too; `ReviewLegacyReferences` is what says so, once per site.
        if (kind.ResolveProperty(written) is not { } property)
        {
            return new ScopeLookup.UnknownProperty(kind.Keyword, [.. kind.ReadableNames]);
        }

        // A parameter the user stated is readable at once, whatever the property's availability says:
        // `14`'s table reads "declared parameters: always, immediately", and availability describes
        // where the value comes from when nobody stated it. Reading the pending value rather than the
        // symbol's is what makes it work during evaluation, before anything has been published. The
        // stated value is keyed by the *parameter's* key, which `D-120` lets differ from the
        // property's: `in[2].t` is the parameter `in2` when stated and the property `t_in2` when solved.
        if (StatedParameterKey(kind, written) is { } key && component.Parameters.ContainsKey(key))
        {
            var parameterId = new ValueId.ComponentParameter(head, key);

            return _pending.TryGetValue(parameterId, out var stated) && stated.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, parameterId)
                : new ScopeLookup.Deferred(parameterId);
        }

        // Sized or solved, and nobody stated it: this is the deferral `14`'s two-phase evaluation
        // exists for, not an error.
        return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, property.Key));
    }

    /// <summary>
    /// The dimension an expression has, without evaluating it (<c>U-5</c>): a deferred <c>let</c> has no value
    /// until the solve, but <c>1.2*HE1.dp</c> is a pressure difference as soon as <c>HE1</c>'s kind is known,
    /// and completion after <c>dp=</c> should offer it while completion after <c>power=</c> should not.
    /// </summary>
    /// <param name="expression">The expression to type.</param>
    /// <param name="visiting">The bindings on the path here, so a cycle types as unknown rather than recursing.</param>
    /// <returns>
    /// The dimension, or <see langword="null"/> when the expression does not say: a bare number alone, a curve,
    /// a call, or a reference nothing resolves. A bare number beside a dimensioned operand is dimensionless,
    /// so <c>2*HE1.dp</c> types; a curve is unknown, so <c>heating*2</c> does not -- the two kinds of "no
    /// dimension" are kept apart, and only the first is treated as a number.
    /// </returns>
    private Dimension? DimensionOf(ExpressionSyntax expression, HashSet<string> visiting)
    {
        var (known, dimension) = Type(expression, visiting);
        return known && dimension.IsNamed && dimension.Name != "Dimensionless" ? dimension : known && !dimension.IsNamed ? dimension : null;
    }

    /// <summary>The typing behind <see cref="DimensionOf(ExpressionSyntax, HashSet{string})"/>: whether the dimension is known, and what it is when it is.</summary>
    private (bool Known, Dimension Dimension) Type(ExpressionSyntax expression, HashSet<string> visiting)
    {
        switch (expression)
        {
            case NumberLiteralSyntax:
                return (true, Dimension.Dimensionless);

            case QuantityLiteralSyntax literal:
                return UnitTable.Resolve(literal.Unit, null) is { } unit ? (true, unit.Dimension) : (false, default);

            case QuantityReferenceSyntax quantity:
            {
                // `HE1.dp kPa`: the reference's own dimension picks between a shared spelling's readings.
                var inner = TypeReference(quantity.Reference, visiting);
                return UnitTable.Resolve(quantity.Unit, inner.Known ? inner.Dimension : null) is { } stated ? (true, stated.Dimension) : (false, default);
            }

            case ParenthesizedExpressionSyntax parenthesized:
                return Type(parenthesized.Inner, visiting);

            case UnaryExpressionSyntax unary:
                return Type(unary.Operand, visiting);

            case BinaryExpressionSyntax binary:
            {
                var left = Type(binary.Left, visiting);
                var right = Type(binary.Right, visiting);

                if (!left.Known || !right.Known)
                {
                    return (false, default);
                }

                switch (binary.Operator)
                {
                    case BinaryOperator.Multiply:
                        return (true, Dimension.FromVector(left.Dimension.Vector + right.Dimension.Vector));

                    case BinaryOperator.Divide:
                        return (true, Dimension.FromVector(left.Dimension.Vector - right.Dimension.Vector));

                    default:
                        // A sum keeps the dimensioned side: `HE1.dp + 5` reads the 5 in the other's unit (D-14).
                        if (left.Dimension.IsNamed && left.Dimension.Name == "Dimensionless")
                        {
                            return right;
                        }

                        if (left.Dimension == right.Dimension || (right.Dimension.IsNamed && right.Dimension.Name == "Dimensionless"))
                        {
                            return left;
                        }

                        // `HE1.dp + 5 kPa`: the literal's spelling is shared by a reading and a difference, and
                        // with no destination to consult it typed as the reading; the evaluator reads it against
                        // the other operand, and a sum of like vectors is the difference dimension (FromVector).
                        return left.Dimension.Vector == right.Dimension.Vector ? (true, Dimension.FromVector(left.Dimension.Vector)) : (false, default);
                }
            }

            case ReferenceSyntax reference:
                return TypeReference(reference, visiting);

            default:
                // A call: the closed set has functions of every shape, and typing them is a second evaluator.
                return (false, default);
        }
    }

    private (bool Known, Dimension Dimension) TypeReference(ReferenceSyntax reference, HashSet<string> visiting)
    {
        var head = reference.Head.Token.Text;

        if (reference.Parts.IsEmpty)
        {
            if (Constants.TryGet(head, out var constant))
            {
                return (true, constant.Dimension);
            }

            if (!_bindingsByName.TryGetValue(head, out var binding))
            {
                // A curve, or nothing: a curve's numbers are bare until something reads them (D-57).
                return (false, default);
            }

            if (_pending.TryGetValue(binding.Id, out var pending) && pending.Value is { } value)
            {
                return (true, value.Dimension);
            }

            return visiting.Add(head) ? Type(binding.Declaration.Value, visiting) : (false, default);
        }

        if (!_componentsByName.TryGetValue(head, out var slot) || _components[slot.Index].Kind is not { } kind)
        {
            return (false, default);
        }

        var written = reference.PropertyPath();

        if (StatedParameterKey(kind, written) is { } key
            && _components[slot.Index].Parameters.ContainsKey(key)
            && kind.Parameters.GetValueOrDefault(key) is { ValueKind: ParameterValueKind.Quantity } parameter)
        {
            return (true, parameter.Dimension);
        }

        return kind.ResolveProperty(written) is { } property ? (true, property.Dimension) : (false, default);
    }

    /// <summary>The key a stated parameter spelled like a property is stored under, or null when no parameter is spelled so.</summary>
    /// <remarks>
    /// Names, aliases and legacy spellings, and the indexed families -- the same walk
    /// <see cref="ResolveParameter"/> makes, without its diagnostics, because this is a read and the
    /// declaration already said what it had to.
    /// </remarks>
    private static string? StatedParameterKey(ComponentKindInfo kind, string written) =>
        kind.ResolveParameter(written, out _, out _)?.Key;

    /// <summary>Says, once per site, which property references were written in a spelling <c>D-120</c> retired.</summary>
    /// <remarks>
    /// Not in <see cref="Lookup"/>: a reference is evaluated as often as the fixed point needs, and a
    /// diagnostic raised there would repeat with it. One walk over the statements after evaluation is
    /// one message per written name, at the reference's own span, with the current spelling of the
    /// whole reference as the quick fix -- <c>HX1.t_in2</c> becomes <c>HX1.in[2].t</c>.
    /// </remarks>
    private void ReviewLegacyReferences()
    {
        foreach (var statement in parse.Root.Statements)
        {
            foreach (var expression in Expressions(statement))
            {
                foreach (var reference in References(expression))
                {
                    if (reference.Parts.IsEmpty
                        || !_componentsByName.TryGetValue(reference.Head.Token.Text, out var slot)
                        || _components[slot.Index].Kind is not { } kind)
                    {
                        continue;
                    }

                    var written = reference.PropertyPath();
                    kind.ResolveProperty(written, out var suggestion);

                    if (suggestion is not null)
                    {
                        var span = TextSpan.FromBounds(reference.Parts[0].Name.Span.Start, reference.Span.End);
                        ReportLegacySpelling(span, written, suggestion);
                    }
                }
            }
        }
    }

    /// <summary>Every expression a statement carries: its parameters' values and a <c>let</c>'s right-hand side.</summary>
    private static IEnumerable<ExpressionSyntax> Expressions(StatementSyntax statement)
    {
        var parameters = statement switch
        {
            ComponentDeclarationSyntax declaration => declaration.Parameters.Concat(declaration.SizingPoint),
            ConnectionSyntax connection => connection.Parameters,
            ControlBindingSyntax control => control.Arguments,
            DesignDirectiveSyntax design => design.Arguments,
            CurveHeaderSyntax curve => curve.Arguments,
            _ => [],
        };

        foreach (var parameter in parameters)
        {
            // A list is not an expression that evaluates; its elements are (`D-143`). Yielding the
            // list itself would make the graph depend on a node nothing ever produces a value for.
            if (parameter.Value is ScenarioListSyntax list)
            {
                foreach (var element in list.Elements)
                {
                    yield return element.Value;
                }

                continue;
            }

            yield return parameter.Value;
        }

        if (statement is LetBindingSyntax let)
        {
            yield return let.Value;
        }
    }

    /// <summary>Every reference inside an expression, in source order.</summary>
    private static IEnumerable<ReferenceSyntax> References(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ReferenceSyntax reference:
                yield return reference;
                break;

            case QuantityReferenceSyntax quantity:
                yield return quantity.Reference;
                break;

            case BinaryExpressionSyntax binary:
                foreach (var inner in References(binary.Left).Concat(References(binary.Right)))
                {
                    yield return inner;
                }

                break;

            case UnaryExpressionSyntax unary:
                foreach (var inner in References(unary.Operand))
                {
                    yield return inner;
                }

                break;

            case ParenthesizedExpressionSyntax parenthesized:
                foreach (var inner in References(parenthesized.Inner))
                {
                    yield return inner;
                }

                break;

            case CallSyntax call:
                foreach (var inner in call.Arguments.SelectMany(static argument => References(argument.Value)))
                {
                    yield return inner;
                }

                break;
        }
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
