using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Layout;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Model;

/// <summary>Projects a bound, lowered and possibly solved model into the wire contract (<c>26</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Never throws on user input.</strong> A value that cannot be expressed goes out as
/// <see langword="null"/> with <c>FS2501</c>; a payload past the cap goes out without states and with
/// <c>FS2502</c>; a state that cannot be read is absent. The script under editing is malformed most of
/// the time, and this runs on every debounce.
/// </para>
/// <para>
/// <strong>Values are rounded to six significant digits on the wire.</strong> A Newton solution
/// carries fifteen digits of which the last nine are noise below <c>36</c>'s tolerances; writing them
/// makes two runs of one script differ in the golden file and tells a reader nothing. Six is beyond
/// any instrument on the plant and is this project's choice, not a convention found elsewhere.
/// </para>
/// </remarks>
public static class ModelContractBuilder
{
    /// <summary>The version this builder implements.</summary>
    public const string ContractVersion = "2.0";

    /// <summary>The fluid property package and its exact version, as the provenance names it.</summary>
    public static VersionedId PropertyBackend { get; } = new("sharp-prop", Fluids.PropertyBackend.PackageVersion);

    private const int SignificantDigits = 6;

    /// <summary>Builds the contract.</summary>
    /// <param name="input">What the pipeline produced.</param>
    /// <param name="statesOmitted">
    /// Whether to leave every state out and mark each circuit so. The size cap that decides this is
    /// measured on the serialized form, which Core cannot produce (<c>D-47</c>): the Api serializes,
    /// measures, and asks again with this set when the payload is over the cap, adding <c>FS2502</c>
    /// to <see cref="ModelContractInput.Diagnostics"/>.
    /// </param>
    /// <returns>The contract.</returns>
    public static ModelContract Build(ModelContractInput input, bool statesOmitted = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        var graph = input.Run?.Graph ?? input.Graph;
        var (hints, layoutDiagnostics) = LayoutHintsDerivation.Derive(graph, input.Model, BranchFlows(input));

        return Build(input, hints, [.. input.Diagnostics, .. layoutDiagnostics], statesOmitted);
    }

    private static ModelContract Build(
        ModelContractInput input, LayoutHints hints, ImmutableArray<Diagnostic> diagnostics, bool statesOmitted)
    {
        var raised = ImmutableArray.CreateBuilder<Diagnostic>();
        var graph = input.Run?.Graph ?? input.Graph;
        var model = input.Model;
        var run = input.Run;
        var solved = run is not null && run.Solve.Converged;
        var layout = run is null ? null : SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);

        // A converged run has assembled this system once already; with it a discharging port reads
        // the component's own outlet rather than the mixed node it discharges into (C-103).
        var system = solved ? EquationSystem.Build(run!.Graph, WellPosedness.Check(run.Graph), run.Solve.Solution) : null;
        ImmutableArray<ImmutableArray<SolvedPort?>>? ports = run is null || layout is null || statesOmitted
            ? null
            : SolvedStates.Ports(run.Graph, layout, run.Solve.Solution, system);
        var registry = ComponentRegistry.Default;
        var symbols = model.Components.ToDictionary(static component => component.Name, StringComparer.Ordinal);
        var expansions = graph.Groups
            .SelectMany(group => group.Members.Select(member => (Member: member, Parent: group.Source)))
            .ToDictionary(static pair => pair.Member, static pair => pair.Parent, StringComparer.Ordinal);

        var components = ImmutableArray.CreateBuilder<ComponentWire>();

        for (var i = 0; i < graph.Components.Length; i++)
        {
            var component = graph.Components[i];
            symbols.TryGetValue(component.Name, out var symbol);
            var kind = symbol?.Kind ?? (registry.Resolve(component.Kind) as KindResolution.Exact)?.Kind;

            components.Add(new ComponentWire
            {
                Id = component.Name,
                Kind = component.Kind,
                Mode = component.Mode,
                SymbolId = SymbolCatalog.IdFor(component.Kind),
                Origin = OriginOf(symbol, expansions.ContainsKey(component.Name)),
                // A declared component's declaration, or an implicit pipe's connection line (C-97); null for what nobody wrote.
                SourceSpan = symbol?.DeclarationSpan is { } span ? new SpanWire(span.Start, span.Length) : null,
                Circuit = graph.CircuitOf.GetValueOrDefault(component.Name)
                    ?? symbol?.CircuitName
                    ?? (expansions.TryGetValue(component.Name, out var parent) && symbols.TryGetValue(parent, out var owner) ? owner.CircuitName : model.Circuits[0].Name),
                Tag = symbol?.Tag,
                Parameters = Parameters(component, kind, run, layout, raised),
                State = ports is null || layout is null || run is null
                    ? null
                    : State(graph, i, component, kind, ports.Value[i], layout, run.Solve.Solution, raised),
                Ports = Ports(graph, i, component),
            });
        }

        // Instruments and controllers are drawn but not lowered: they appear here so the canvas can
        // key them by id, and in `layout.nonFlowElements` rather than `layout.order`.
        foreach (var symbol in model.Components.Where(static component => component.Kind is { IsObserver: true } || component.Kind?.Keyword == "controller"))
        {
            components.Add(new ComponentWire
            {
                Id = symbol.Name,
                Kind = symbol.Kind!.Keyword,
                SymbolId = SymbolCatalog.IdFor(symbol.Kind.Keyword),
                Origin = OriginOf(symbol, expanded: false),
                SourceSpan = symbol.DeclarationSpan is { } span ? new SpanWire(span.Start, span.Length) : null,
                Circuit = symbol.CircuitName,
                Tag = symbol.Tag,
                Parameters = StatedOnly(symbol, raised),
                State = null,
                Ports = [],
            });
        }

        var connections = Connections(model, graph, hints, ports, raised);
        var circuits = Circuits(model, graph, hints, solved, statesOmitted);
        var scene = LayoutSolver.Solve(graph, model, hints, LayoutSolver.MarginOf(model));

        // The audit runs on every layout, not only on the ladder's fixtures (C-101): a hard breach of 28 B
        // is reported, one line per breach, so the picture never arrives as if it met the standard.
        foreach (var breach in SceneAudit.Findings(scene, model).Where(static finding => finding.Hard))
        {
            raised.Add(Diagnostic.Create(
                LayoutDiagnostics.LayoutBreach,
                span: null,
                new DiagnosticArgument("rule", breach.Kind),
                new DiagnosticArgument("first", breach.First),
                new DiagnosticArgument("second", breach.Second),
                new DiagnosticArgument("detail", breach.Detail)));
        }
        var styles = new Styles(model, graph);
        var scales = ColourScales.Resolve(input.Root, graph, ports, raised);
        var states = components.ToDictionary(static c => c.Id, static c => c.State, StringComparer.Ordinal);
        var all = Diagnostics(input.Source, [.. diagnostics, .. raised], [.. components]);

        return new ModelContract
        {
            ContractVersion = ContractVersion,
            Provenance = new Provenance
            {
                SourceHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.Source.Text))),
                LanguageMajor = input.Root.Version is { } version && !double.IsNaN(version.Major.Value) ? (int)version.Major.Value : 1,
                Catalog = new VersionedId(input.Catalog.Name, input.Catalog.Version),
                PropertyBackend = PropertyBackend,
                AtmosphereKPaAbsolute = UnitTable.StandardAtmosphere / 1000,
            },
            Project = model.Project.Name is null && model.Project.DefaultMode is null
                ? null
                : new ProjectWire(model.Project.Name, ModeName(model.Project.DefaultMode)),
            Style = new StyleWire(
                [.. model.Style.Tokens.Select(static token => token.Text)],
                model.Style.Spacing,
                Styles.Resolve(model.Style.Default),
                model.Style.Definitions.OrderBy(static d => d.Key, StringComparer.Ordinal).ToDictionary(static d => d.Key, static d => Styles.Resolve(d.Value), StringComparer.Ordinal)),
            Circuits = circuits,
            PressureDatums = [.. HydraulicPartition.Of(graph).Select(static part => part.Datum).Where(static datum => datum.Length > 0)],
            Components = components.ToImmutable(),
            Symbols = SymbolCatalog.All,
            Connections = connections,
            Layout = Layout(hints, scene, styles, scales),
            Visualization = scales.Wire,
            Bindings = [.. model.Bindings.Select(binding => BindingOf(binding, raised))],
            Diagnostics = all,
            Solve = run is null
                ? null
                : new SolveWire
                {
                    Converged = run.Solve.Converged,
                    Iterations = run.Iterations,
                    ResidualNorm = Round(run.Solve.ResidualNorm, significant: 3),
                    ElapsedMs = input.ElapsedMs,
                    SizingPasses = run.Passes,
                },
        };
    }

    // ---- values ---------------------------------------------------------------------------------------

    /// <summary>A value in its canonical unit, rounded, or <see langword="null"/> with <c>FS2501</c>.</summary>
    private static (double? Value, string? Unit) Canonical(
        double si, Dimension dimension, string component, string field, ImmutableArray<Diagnostic>.Builder raised)
    {
        // A dimension with no canonical spelling -- head, Kv, a designation -- goes out in its SI unit,
        // which is what the script's bare number meant for it.
        var unit = UnitTable.CanonicalUnitFor(dimension);
        var text = unit?.Text ?? (dimension.SiUnit.Length > 0 ? dimension.SiUnit : null);
        var value = unit is null ? si : Quantity.FromSi(si, dimension).ValueIn(unit);

        if (!double.IsFinite(value))
        {
            raised.Add(Diagnostic.Create(
                ContractDiagnostics.NotFinite,
                span: null,
                new DiagnosticArgument("component", component),
                new DiagnosticArgument("field", field),
                new DiagnosticArgument("value", value.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticArgument("unit", text ?? "a bare number")));

            return (null, text);
        }

        // Below a nanounit the value is the solver's noise around zero -- a gauge pressure of -1.7e-21
        // kPa at the datum -- and zero is what it means.
        return (Math.Abs(value) < 1e-9 ? 0 : Round(value), text);
    }

    private static QuantityWire Wire(double si, Dimension dimension, string component, string field, ImmutableArray<Diagnostic>.Builder raised)
    {
        var (value, unit) = Canonical(si, dimension, component, field, raised);
        return new QuantityWire(value, unit ?? string.Empty);
    }

    /// <summary>Six significant digits, and a clean zero.</summary>
    private static double Round(double value, int significant = SignificantDigits)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value == 0 ? 0 : value;
        }

        // Through the decimal string and back: the double nearest "0.239213" is what the writer prints
        // as 0.239213 again, where a scaled Math.Round can land one ulp off it and print seventeen
        // digits. Math.Round's fifteen-decimal limit would not reach a residual norm of 1e-11 either.
        return double.Parse(value.ToString("G" + significant.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // ---- components -----------------------------------------------------------------------------------

    private static string OriginOf(ComponentSymbol? symbol, bool expanded) => symbol?.Origin switch
    {
        FluidScript.Core.Binding.Origin.Declared => "declared",
        FluidScript.Core.Binding.Origin.Inferred inferred => "inferred:" + inferred.Rule,
        _ => expanded ? "expanded" : "inferred",
    };

    private static Dictionary<string, ParameterWire> Parameters(
        IComponent component,
        ComponentKindInfo? kind,
        OuterLoopResult? run,
        SystemLayout? layout,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var parameters = new Dictionary<string, ParameterWire>(StringComparer.Ordinal);

        // Stated outranks solved outranks sized outranks default: the factory's own precedence, with
        // the solver's answer above the overlay because a promoted Kv's overlay entry is only its seed.
        // Ordinal order on the wire so two builds of one model agree.
        foreach (var (name, quantity) in component.DefaultParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire
            {
                Value = value,
                Unit = unit,
                Source = "default",
                Basis = kind?.Parameters.GetValueOrDefault(name)?.DefaultBasis ?? "the registry's default",
            };
        }

        foreach (var (name, quantity) in component.SizedParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire
            {
                Value = value,
                Unit = unit,
                Source = "sized",
                Basis = run?.Bases.GetValueOrDefault($"{component.Name}.{name}") ?? "chosen by a sizing rule",
            };
        }

        // A parameter the solver was asked to find -- a promoted head or Kv -- is sized too: not by a
        // rule but by the constraints it holds, and the basis says so (23's promotion).
        if (run is not null && layout is not null)
        {
            foreach (var (name, value, _) in SolvedStates.Parameters(layout, run.Solve.Solution, component.Name))
            {
                var dimension = kind?.Parameters.GetValueOrDefault(name)?.Dimension ?? Dimension.Dimensionless;
                var (canonical, unit) = Canonical(value, dimension, component.Name, name, raised);
                parameters[name] = new ParameterWire
                {
                    Value = canonical,
                    Unit = unit,
                    Source = "sized",
                    Basis = run.Bases.GetValueOrDefault($"{component.Name}.{name}") ?? "found by the solver, to hold the stated constraints",
                };
            }
        }

        foreach (var (name, quantity) in component.StatedParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, component.Name, name, raised);
            parameters[name] = new ParameterWire { Value = value, Unit = unit, Source = "stated" };
        }

        return parameters
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, ParameterWire> StatedOnly(ComponentSymbol symbol, ImmutableArray<Diagnostic>.Builder raised)
    {
        var parameters = new Dictionary<string, ParameterWire>(StringComparer.Ordinal);

        foreach (var (name, parameter) in symbol.Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (parameter.Value is not { } quantity)
            {
                continue;
            }

            var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, symbol.Name, name, raised);
            parameters[name] = new ParameterWire { Value = value, Unit = unit, Source = "stated" };
        }

        return parameters;
    }

    private static ImmutableArray<PortWire> Ports(CircuitGraph graph, int index, IComponent component)
    {
        if (component is not IFlowComponent flow)
        {
            return [];
        }

        var ports = ImmutableArray.CreateBuilder<PortWire>(flow.Ports.Length);

        for (var port = 0; port < flow.Ports.Length; port++)
        {
            var peer = graph.Adjacency.Peer(index, port);

            ports.Add(new PortWire
            {
                Name = flow.Ports[port].Name,
                Role = flow.Ports[port].Role.ToString().ToLowerInvariant(),
                ConnectedTo = peer.Exists ? graph.Components[peer.Component].Name : null,
                Elevation = component is Tank tank ? Round(tank.PortLevels[port]) : null,
                Layer = component is Tank stratified ? stratified.LayerForPort(port) : null,
            });
        }

        return ports.ToImmutable();
    }

    private static ComponentStateWire? State(
        CircuitGraph graph,
        int index,
        IComponent component,
        ComponentKindInfo? kind,
        ImmutableArray<SolvedPort?> ports,
        SystemLayout layout,
        StateVector solution,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var name = component.Name;
        QuantityWire Q(double si, Dimension dimension, string field) => Wire(si, dimension, name, field, raised);

        if (component is CircuitNode)
        {
            return ports.Length > 0 && ports[0] is { } own
                ? new ComponentStateWire { T = Q(own.Temperature, Dimension.Temperature, "t"), P = Q(own.Pressure, Dimension.Pressure, "p") }
                : null;
        }

        if (component is Tank tank)
        {
            var enthalpy = layout.Unknowns.FirstOrDefault(unknown =>
                unknown.Kind == UnknownKind.NodeEnthalpy && string.Equals(unknown.OwnerComponentId, name, StringComparison.Ordinal));
            var reference = ports.FirstOrDefault(static port => port is not null);

            if (enthalpy is null || reference is not { } at || enthalpy.Index >= solution.Values.Length)
            {
                return null;
            }

            var mixed = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(at.Pressure, Dimension.Pressure),
                Quantity.FromSi(solution.Values[enthalpy.Index], Dimension.Enthalpy));

            return mixed.IsSuccess
                ? new ComponentStateWire { T = Q(mixed.Value.Temperature.SiValue, Dimension.Temperature, "t"), P = Q(at.Pressure, Dimension.Pressure, "p") }
                : null;
        }

        if (component is not IFlowComponent flow || ports.Length < 2 || ports[0] is not { } first || ports[1] is not { } second)
        {
            return null;
        }

        // The inlet is whichever of the first pair the flow enters; at zero flow the declared inlet.
        var forward = first.Flow > 0 || (first.Flow == 0 && second.Flow <= 0);
        var (inlet, outlet) = forward ? (first, second) : (second, first);
        var massFlow = Math.Abs(inlet.Flow);
        var solved = Solved(layout, solution, name, kind, raised);

        var state = new ComponentStateWire
        {
            Flow = Q(first.Flow, Dimension.MassFlow, "flow"),
            TIn = Q(inlet.Temperature, Dimension.Temperature, "tIn"),
            TOut = Q(outlet.Temperature, Dimension.Temperature, "tOut"),
            PIn = Q(inlet.Pressure, Dimension.Pressure, "pIn"),
            POut = Q(outlet.Pressure, Dimension.Pressure, "pOut"),
            Dp = Q(inlet.Pressure - outlet.Pressure, Dimension.PressureDelta, "dp"),
            Solved = solved.Count == 0 ? null : solved,
        };

        switch (component)
        {
            case HeatExchanger exchanger:
                state = state with { Power = Q(massFlow * (outlet.Enthalpy - inlet.Enthalpy), Dimension.Power, "power") };

                if (exchanger.SecondarySideConnected && ports.Length >= 4 && ports[2] is { } third && ports[3] is { } fourth)
                {
                    var forward2 = third.Flow > 0 || (third.Flow == 0 && fourth.Flow <= 0);
                    var (in2, out2) = forward2 ? (third, fourth) : (fourth, third);
                    state = state with
                    {
                        Flow2 = Q(third.Flow, Dimension.MassFlow, "flow2"),
                        TIn2 = Q(in2.Temperature, Dimension.Temperature, "tIn2"),
                        TOut2 = Q(out2.Temperature, Dimension.Temperature, "tOut2"),
                    };
                }

                break;

            case Pump:
                state = state with
                {
                    Head = Q((outlet.Pressure - inlet.Pressure) / (inlet.Density * UnitTable.StandardGravity), Dimension.Head, "head"),
                };
                break;
        }

        return state;
    }

    private static Dictionary<string, QuantityWire> Solved(
        SystemLayout layout, StateVector solution, string component, ComponentKindInfo? kind, ImmutableArray<Diagnostic>.Builder raised)
    {
        var solved = new Dictionary<string, QuantityWire>(StringComparer.Ordinal);

        foreach (var (parameter, value, _) in SolvedStates.Parameters(layout, solution, component))
        {
            var dimension = kind?.Parameters.GetValueOrDefault(parameter)?.Dimension ?? Dimension.Dimensionless;
            solved[parameter] = Wire(value, dimension, component, parameter, raised);
        }

        return solved;
    }

    // ---- connections and circuits ----------------------------------------------------------------------

    private static ImmutableArray<ConnectionWire> Connections(
        SemanticModel model,
        CircuitGraph graph,
        LayoutHints hints,
        ImmutableArray<ImmutableArray<SolvedPort?>>? ports,
        ImmutableArray<Diagnostic>.Builder raised)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            index[graph.Components[i].Name] = i;
        }

        var connections = ImmutableArray.CreateBuilder<ConnectionWire>(model.Connections.Length);

        for (var i = 0; i < model.Connections.Length; i++)
        {
            var connection = model.Connections[i];
            var id = $"c{i}";
            ConnectionStateWire? state = null;

            // The flow along the connection as written is the flow leaving the `from` component
            // through the port that faces `to`.
            if (ports is { } solved
                && index.TryGetValue(connection.From.Component, out var from)
                && index.TryGetValue(connection.To.Component, out var to))
            {
                for (var port = 0; port < graph.Adjacency.PortCount(from); port++)
                {
                    var peer = graph.Adjacency.Peer(from, port);

                    if (peer.Exists && peer.Component == to && port < solved[from].Length && solved[from][port] is { } at)
                    {
                        state = new ConnectionStateWire(Wire(-at.Flow, Dimension.MassFlow, id, "flow", raised));
                        break;
                    }
                }
            }

            connections.Add(new ConnectionWire
            {
                Id = id,
                From = new EndpointWire(connection.From.Component, connection.From.Port.Length == 0 ? null : connection.From.Port),
                To = new EndpointWire(connection.To.Component, connection.To.Port.Length == 0 ? null : connection.To.Port),
                Flow = hints.Flow.GetValueOrDefault(id, FlowDirection.None).ToString().ToLowerInvariant(),
                State = state,
            });
        }

        return connections.ToImmutable();
    }

    private static ImmutableArray<CircuitWire> Circuits(
        SemanticModel model, CircuitGraph graph, LayoutHints hints, bool solved, bool statesOmitted)
    {
        var circuits = ImmutableArray.CreateBuilder<CircuitWire>(model.Circuits.Length);

        foreach (var circuit in model.Circuits)
        {
            var hint = hints.Circuits.FirstOrDefault(candidate => string.Equals(candidate.Name, circuit.Name, StringComparison.Ordinal));

            circuits.Add(new CircuitWire
            {
                Name = circuit.Name,
                Number = circuit.Number,
                NumberIsExplicit = circuit.NumberIsExplicit,
                Substance = circuit.Substance ?? graph.Substance.Name,
                Mode = ModeName(circuit.Mode) ?? "steady",
                Role = hint?.Role?.CanonicalName,
                ParentCircuit = hint?.ParentCircuit,
                InletAnchorId = hint?.InletAnchorId,
                OutletAnchorId = hint?.OutletAnchorId,
                Solved = solved,
                StatesOmitted = statesOmitted,
            });
        }

        return circuits.ToImmutable();
    }

    private static string? ModeName(FluidMode? mode) => mode switch
    {
        FluidMode.Static => "steady",
        FluidMode.Dynamic => "transient",
        _ => null,
    };

    // ---- layout ------------------------------------------------------------------------------------------

    private static LayoutWire Layout(LayoutHints hints, Scene scene, Styles styles, ColourScales scales) => new()
    {
        Margin = scene.Margin,
        Extent = Styles.BoxOf(scene.Extent),
        Placements = [.. scene.Placements.Select(p => new PlacementWire
        {
            ComponentId = p.ComponentId,
            SymbolId = p.SymbolId,
            Inner = Styles.BoxOf(p.Inner),
            Outer = Styles.BoxOf(p.Outer),
            Rotation = p.Rotation,
            Mirrored = p.Mirrored,
            Arrangement = p.Arrangement,
            Anchors = p.Anchors.ToDictionary(
                static a => a.Key,
                static a => new AnchorWire { At = [Round(a.Value.At.X), Round(a.Value.At.Y)], Direction = [a.Value.Direction.X, a.Value.Direction.Y] },
                StringComparer.Ordinal),
            LabelAt = [Round(p.LabelAt.X), Round(p.LabelAt.Y)],
            Source = p.Source,
            Style = styles.Of(p.ComponentId),
            Scale = scales.Of(p.ComponentId)[scales.Active].At,
            Scales = scales.Of(p.ComponentId),
        })],
        Routes = [.. scene.Routes.Select(r => new RouteWire
        {
            Id = r.ConnectionId,
            Kind = r.Kind,
            Layer = r.Layer,
            Points = [.. r.Points.SelectMany(static point => new[] { Round(point.X), Round(point.Y) })],
            Hops = [.. r.Hops.SelectMany(static point => new[] { Round(point.X), Round(point.Y) })],
            Style = styles.Of(styles.FromComponentOf(r.ConnectionId)),
            ScaleFrom = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId))[scales.Active].From : null,
            ScaleTo = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId))[scales.Active].To : null,
            Scales = r.Kind == "pipe" ? scales.OfRoute(styles.FromComponentOf(r.ConnectionId), styles.ToComponentOf(r.ConnectionId)) : ImmutableDictionary<string, ScalePositionWire>.Empty,
        })],
        Order = hints.Order,

        ThermalStages = [.. hints.ThermalStages.Select(static stage => new ThermalStageWire(stage.Rank, stage.Role.ToString().ToLowerInvariant(), stage.Components))],
        Flow = hints.Flow
            .OrderBy(static pair => int.Parse(pair.Key[1..], CultureInfo.InvariantCulture))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString().ToLowerInvariant(), StringComparer.Ordinal),

        Groups = [.. hints.Groups.Select(static group => new ComponentGroupWire(group.ParentComponentId, group.Children))],
        NonFlowElements = [.. hints.NonFlowElements.Select(static element => new NonFlowElementWire(
            element.ComponentId, element.PlacementAnchorId, element.MeasurementTargetId, element.ActuationTargetId, element.NavigationOrder))],
        CircuitOf = Ordered(hints.CircuitOf, static circuit => circuit),
        DistributionGroups = [.. hints.DistributionGroups.Select(static group => new DistributionGroupWire(group.ParentCircuit, group.Members))],
        Inferred = [.. hints.Inferred.Order(StringComparer.Ordinal)],

    };

    private static Dictionary<string, TOut> Ordered<TIn, TOut>(IEnumerable<KeyValuePair<string, TIn>> pairs, Func<TIn, TOut> map) =>
        pairs.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, pair => map(pair.Value), StringComparer.Ordinal);

    // ---- visualization -------------------------------------------------------------------------------------

    /// <summary>
    /// The <c>show</c> directive resolved against what was solved (<c>57</c>, <c>D-117</c>): one scale per
    /// available property, each with its domain, and every element's place on each. Core maps because it
    /// holds every value (<c>D-03</c>, <c>D-103</c>); the frontend owns the colours and, with every scale
    /// on the wire, switches between them without a request (<c>57</c> invariant 6).
    /// </summary>
    private sealed class ColourScales
    {
        // The spellings come from the one property table (`D-120`, `L-50`); a scale is keyed by the
        // quantity's name, which is what the wire carries.

        /// <summary>The properties every model offers, after the script's own.</summary>
        private static readonly ImmutableArray<string> Always = ["temperature", "pressure", "flow"];

        private readonly ImmutableDictionary<string, ScaleWire> _scales;
        private readonly ImmutableDictionary<string, ImmutableDictionary<string, ScalePositionWire>> _positions;

        private ColourScales(
            string active,
            ImmutableArray<string> available,
            ImmutableDictionary<string, ScaleWire> scales,
            ImmutableDictionary<string, ImmutableDictionary<string, ScalePositionWire>> positions)
        {
            Active = active;
            Available = available;
            _scales = scales;
            _positions = positions;
        }

        /// <summary>The property the script shows first, or <c>temperature</c>.</summary>
        public string Active { get; }

        /// <summary>The properties the switcher offers: the script's, then <c>temperature</c>, <c>pressure</c>, <c>flow</c>.</summary>
        public ImmutableArray<string> Available { get; }

        /// <summary>The wire's <c>visualization</c> block.</summary>
        public VisualizationWire Wire => new()
        {
            Active = Active,
            Available = Available,
            Scale = _scales[Active],
            Scales = _scales.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
        };

        /// <summary>A component's place on every scale; a component the scales never saw has every entry <see langword="null"/>.</summary>
        public Dictionary<string, ScalePositionWire> Of(string componentId) =>
            _scales.Keys.Order(StringComparer.Ordinal).ToDictionary(
                static property => property,
                property => _positions[property].GetValueOrDefault(componentId) ?? new ScalePositionWire(null, null, null),
                StringComparer.Ordinal);

        /// <summary>A route's ends on every scale: the outlet value of the component it leaves and the inlet value of the one it enters, so a pipe into a pump ends at the suction's colour and the pump's own gradient carries on to the discharge; a node has one value for both.</summary>
        public Dictionary<string, ScalePositionWire> OfRoute(string fromComponentId, string toComponentId) =>
            _scales.Keys.Order(StringComparer.Ordinal).ToDictionary(
                static property => property,
                property =>
                {
                    var from = _positions[property].GetValueOrDefault(fromComponentId);
                    var to = _positions[property].GetValueOrDefault(toComponentId);
                    return new ScalePositionWire(null, from?.To ?? from?.At, to?.From ?? to?.At);
                },
                StringComparer.Ordinal);

        /// <summary>
        /// Reads the first <c>show</c> directive off the syntax (the binder does not bind it, <c>L-50</c>),
        /// raises <c>57</c>'s diagnostics for what it says, and maps every solved port onto every available
        /// scale. Before a solve every domain is <see langword="null"/> and every position with it.
        /// </summary>
        public static ColourScales Resolve(
            ScriptSyntax root, CircuitGraph graph, ImmutableArray<ImmutableArray<SolvedPort?>>? ports, ImmutableArray<Diagnostic>.Builder raised)
        {
            var directives = root.Statements.OfType<ShowDirectiveSyntax>().ToList();
            var directive = directives.FirstOrDefault();

            foreach (var second in directives.Skip(1))
            {
                raised.Add(Diagnostic.Create(StyleDiagnostics.SecondShowDirective, second.Keyword.Span));
            }

            var named = ImmutableArray.CreateBuilder<string>();

            foreach (var property in directive?.Properties ?? [])
            {
                if (PropertyTable.Find(property.Text) is not { } known)
                {
                    raised.Add(Diagnostic.Create(
                        StyleDiagnostics.UnknownShowProperty,
                        property.Span,
                        new DiagnosticArgument("name", property.Text),
                        new DiagnosticArgument("list", string.Join(", ", PropertyTable.All.Select(static p => p.Name).Order(StringComparer.Ordinal)))));
                    continue;
                }

                if (named.Contains(known.Name))
                {
                    raised.Add(Diagnostic.Create(StyleDiagnostics.DuplicateShowProperty, property.Span, new DiagnosticArgument("name", property.Text)));
                    continue;
                }

                named.Add(known.Name);
            }

            var active = named.Count > 0 ? named[0] : "temperature";
            var available = named.Concat(Always).Distinct(StringComparer.Ordinal).ToImmutableArray();
            (double Min, double Max)? stated = directive?.Scale is { From: NumberLiteralSyntax from, To: NumberLiteralSyntax to }
                && double.IsFinite(from.Value) && double.IsFinite(to.Value)
                ? (Math.Min(from.Value, to.Value), Math.Max(from.Value, to.Value))
                : null;
            var scales = ImmutableDictionary.CreateBuilder<string, ScaleWire>(StringComparer.Ordinal);
            var positions = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, ScalePositionWire>>(StringComparer.Ordinal);

            foreach (var property in available)
            {
                var entry = PropertyTable.Find(property)!; // `available` holds names the table produced
                var (display, dimension, diverging) = (entry.Display, entry.Dimension, entry.Diverging);
                var unit = UnitTable.CanonicalUnitFor(dimension);
                var values = ports is { } solved ? Values(property, graph, solved, unit) : [];
                var (scale, placed) = Build(property, display, unit, diverging, property == active ? stated : null, values);
                scales[property] = scale;
                positions[property] = placed;
            }

            return new ColourScales(active, available, scales.ToImmutable(), positions.ToImmutable());
        }

        /// <summary>One element's values for a property, in the scale's unit: the representative, and the gradient's ends where the element has two.</summary>
        private readonly record struct Element(string Id, double? At, double? From, double? To);

        private static (ScaleWire Scale, ImmutableDictionary<string, ScalePositionWire> Positions) Build(
            string property, string display, UnitSymbol? unit, bool diverging, (double Min, double Max)? stated, ImmutableArray<Element> values)
        {
            double? min = null, max = null;

            if (stated is { } range)
            {
                (min, max) = range;
            }
            else
            {
                // The domain is over every element's representative value (57 invariant 2); a null is unsolved, never zero (invariant 9).
                foreach (var value in values.Select(static v => v.At).OfType<double>())
                {
                    min = min is null ? value : Math.Min(min.Value, value);
                    max = max is null ? value : Math.Max(max.Value, value);
                }

                if (min is { } low && max is { } high)
                {
                    (min, max) = diverging ? Symmetric(low, high) : Nice(low, high);
                }
            }

            var domain = min is { } l && max is { } h ? new DomainWire(Round(l), Round(h), Nice: stated is null) : null;
            var positions = ImmutableDictionary.CreateBuilder<string, ScalePositionWire>(StringComparer.Ordinal);

            foreach (var element in values)
            {
                positions[element.Id] = new ScalePositionWire(Position(element.At, domain), Position(element.From, domain), Position(element.To, domain));
            }

            var scale = new ScaleWire
            {
                Property = property,
                DisplayName = display,
                Unit = unit?.Text ?? string.Empty,
                Kind = diverging ? "diverging" : "sequential",
                Domain = domain,
                Degenerate = min is not null && min == max,
            };

            return (scale, positions.ToImmutable());
        }

        /// <summary>0 to 1 on the domain, clamped; the midpoint of a degenerate domain (<c>57</c> invariant 3); <see langword="null"/> without a value or a domain.</summary>
        private static double? Position(double? value, DomainWire? domain)
        {
            if (value is not { } v || domain is null)
            {
                return null;
            }

            return domain.Max <= domain.Min ? 0.5 : Math.Round(Math.Clamp((v - domain.Min) / (domain.Max - domain.Min), 0, 1), 4);
        }

        /// <summary>
        /// Every element's values for a property: a node reads its one state; a component reads its inlet
        /// and its outlet (the ports the fluid enters and leaves by) and represents itself by the outlet
        /// (<c>D-30</c>); flow is the largest through any port, since a junction's net is zero and its
        /// throughput is what the eye asks for; pressure drop is inlet less outlet, a pump's negative.
        /// </summary>
        private static ImmutableArray<Element> Values(
            string property, CircuitGraph graph, ImmutableArray<ImmutableArray<SolvedPort?>> ports, UnitSymbol? unit)
        {
            var elements = ImmutableArray.CreateBuilder<Element>();

            for (var i = 0; i < graph.Components.Length; i++)
            {
                var component = graph.Components[i];
                var solved = ports[i].OfType<SolvedPort>().ToList();

                if (solved.Count == 0)
                {
                    continue;
                }

                double? at = null, from = null, to = null;
                var inlet = solved.Where(static p => p.Flow > 0).Select(static p => (SolvedPort?)p).FirstOrDefault();
                var outlet = solved.Where(static p => p.Flow < 0).Select(static p => (SolvedPort?)p).FirstOrDefault();

                if (property == "flow")
                {
                    at = solved.Max(static p => Math.Abs(p.Flow));
                }
                else if (property == "volume_flow")
                {
                    at = solved.Max(static p => p.Density > 0 ? Math.Abs(p.Flow) / p.Density : 0);
                }
                else if (component is CircuitNode)
                {
                    at = Read(property, solved[0]);
                }
                else if (property == "pressure_drop")
                {
                    at = inlet is { } pIn && outlet is { } pOut ? pIn.Pressure - pOut.Pressure : null;
                }
                else
                {
                    from = inlet is { } a ? Read(property, a) : null;
                    to = outlet is { } b ? Read(property, b) : null;
                    at = to ?? from ?? Read(property, solved[0]);

                    if (from is null || to is null)
                    {
                        (from, to) = (null, null);
                    }
                }

                elements.Add(new Element(component.Name, InUnit(at, unit), InUnit(from, unit), InUnit(to, unit)));
            }

            return elements.ToImmutable();
        }

        private static double? Read(string property, SolvedPort port) => property switch
        {
            "temperature" => port.Temperature,
            "pressure" => port.Pressure,
            "enthalpy" => port.Enthalpy,
            "density" => port.Density,
            _ => null,
        };

        private static double? InUnit(double? si, UnitSymbol? unit) =>
            si is { } value && double.IsFinite(value) ? (unit is null ? value : Quantity.FromSi(value, unit.Dimension).ValueIn(unit)) : null;

        /// <summary>Rounded outward to a 1-2-5 step giving about five ticks (<c>57</c>'s "nice").</summary>
        private static (double Min, double Max) Nice(double min, double max)
        {
            if (max <= min)
            {
                return (min, max);
            }

            var raw = (max - min) / 5;
            var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var fraction = raw / power;
            var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;

            return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
        }

        /// <summary>A diverging domain: niced, then made symmetric about zero so the neutral colour sits at zero (<c>57</c>).</summary>
        private static (double Min, double Max) Symmetric(double min, double max)
        {
            var (low, high) = Nice(Math.Min(min, 0), Math.Max(max, 0));
            var reach = Math.Max(-low, high);
            return (-reach, reach);
        }
    }

    // ---- bindings and diagnostics --------------------------------------------------------------------------

    private static BindingWire BindingOf(BindingSymbol binding, ImmutableArray<Diagnostic>.Builder raised)
    {
        if (binding.Value is not { } quantity)
        {
            // Deferred: the binder does not type an expression it cannot evaluate, so the dimension is
            // unknown here as well (50-frontend/defects.md, F-5).
            return new BindingWire(binding.Name, null, null, null, null);
        }

        var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, binding.Name, "value", raised);
        var dimension = quantity.Dimension;
        return new BindingWire(
            binding.Name,
            value,
            unit,
            dimension.IsNamed && dimension.Name != "Dimensionless" ? dimension.Name : null,
            dimension.IsNamed ? null : dimension.SiUnit);
    }

    /// <summary>Renders diagnostics in <c>44</c>'s wire shape: both position forms from one line index, ordered by severity, then offset, then code.</summary>
    /// <param name="source">The text the spans index.</param>
    /// <param name="diagnostics">The diagnostics, in production order.</param>
    /// <param name="components">The components on the wire, so a diagnostic that names none but sits on a declaration is attributed to it; empty when there is no model.</param>
    /// <returns>The wire records, a diagnostic with no span last within its severity.</returns>
    /// <remarks>
    /// Few producers set <see cref="Diagnostic.ComponentName"/>, yet nearly every diagnostic about a
    /// component is raised on its declaration's span. The log, the canvas badge and the hover card key on
    /// the wire's <c>component</c> (<c>44</c>, <c>54</c>, <c>56</c>), so the declared component whose span
    /// holds the diagnostic's start stands in where the producer said nothing. An inferred component has
    /// no span and is never chosen; a diagnostic on its declaring line is attributed to the declared one.
    /// </remarks>
    public static ImmutableArray<DiagnosticWire> Diagnostics(SourceText source, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<ComponentWire> components = default) =>
    [
        .. diagnostics
            .OrderBy(static diagnostic => diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => 0,
                DiagnosticSeverity.Warning => 1,
                _ => 2,
            })
            .ThenBy(static diagnostic => diagnostic.Span?.Start ?? int.MaxValue)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .Select(diagnostic => new DiagnosticWire
            {
                Code = diagnostic.Code,
                Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                Message = diagnostic.Message,
                Range = diagnostic.Span is { } span ? Range(source, span) : null,
                Component = diagnostic.ComponentName ?? Owner(components, diagnostic.Span),
                Suggestion = diagnostic.Suggestion is { } fix
                    ? new SuggestionWire(fix.Title, Range(source, fix.Span), fix.Replacement)
                    : null,
                Related = [.. diagnostic.Related.Select(related => new RelatedWire(related.Message, Range(source, related.Span)))],
            }),
    ];

    /// <summary>The declared component whose source span holds the start of <paramref name="span"/>, or <see langword="null"/>.</summary>
    private static string? Owner(ImmutableArray<ComponentWire> components, TextSpan? span)
    {
        if (span is not { } at || components.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var component in components)
        {
            // A declared component only: an implicit pipe carries its connection line (C-97), and a
            // diagnostic on that line is about whatever it names, not about the pipe.
            if (component.Origin == "declared" && component.SourceSpan is { } declared && declared.Start <= at.Start && at.Start < declared.Start + declared.Length)
            {
                return component.Id;
            }
        }

        return null;
    }


    /// <summary>Both position forms from the one line index (<c>44</c>).</summary>
    private static RangeWire Range(SourceText source, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, source.Length);
        var end = Math.Clamp(span.End, start, source.Length);
        var first = source.GetLinePosition(start);
        var last = source.GetLinePosition(end);

        return new RangeWire
        {
            Start = new PositionWire(first.Line, first.Character),
            End = new PositionWire(last.Line, last.Character),
            Offset = start,
            Length = end - start,
        };
    }

    private static ImmutableArray<double>? BranchFlows(ModelContractInput input)
    {
        if (input.Run is not { } run || !run.Solve.Converged)
        {
            return null;
        }

        var layout = SystemLayout.Build(run.Graph, WellPosedness.Check(run.Graph).Counting);
        var values = run.Solve.Solution.Values;

        return [.. Enumerable.Range(0, run.Graph.Branches.Length).Select(i => layout.BranchFlow(i) < values.Length ? values[layout.BranchFlow(i)] : 0)];
    }
}
