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
        ImmutableArray<ImmutableArray<SolvedPort?>>? ports = run is null || layout is null || statesOmitted
            ? null
            : SolvedStates.Ports(run.Graph, layout, run.Solve.Solution);
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
                SourceSpan = symbol is { Origin: FluidScript.Core.Binding.Origin.Declared, DeclarationSpan: { } span }
                    ? new SpanWire(span.Start, span.Length)
                    : null,
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
        var all = Diagnostics(input.Source, [.. diagnostics, .. raised]);

        return new ModelContract
        {
            ContractVersion = ContractVersion,
            Provenance = new Provenance
            {
                SourceHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.Source.Text))),
                LanguageMajor = input.Root.Version is { } version && !double.IsNaN(version.Major.Value) ? (int)version.Major.Value : 1,
                Catalog = new VersionedId(input.Catalog.Name, input.Catalog.Version),
                PropertyBackend = new VersionedId("sharp-prop", Fluids.PropertyBackend.PackageVersion),
                AtmosphereKPaAbsolute = UnitTable.StandardAtmosphere / 1000,
            },
            Project = model.Project.Name is null && model.Project.DefaultMode is null
                ? null
                : new ProjectWire(model.Project.Name, ModeName(model.Project.DefaultMode)),
            Style = new StyleWire([.. model.Style.Tokens.Select(static token => token.Text)], model.Style.Spacing),
            Circuits = circuits,
            PressureDatums = [.. HydraulicPartition.Of(graph).Select(static part => part.Datum).Where(static datum => datum.Length > 0)],
            Components = components.ToImmutable(),
            Symbols = SymbolCatalog.All,
            Connections = connections,
            Layout = Layout(hints),
            Visualization = Visualization(input.Root, graph, ports, raised),
            Bindings = [.. model.Bindings.Select(binding => BindingOf(binding, raised))],
            Diagnostics = all,
            Solve = run is null
                ? null
                : new SolveWire
                {
                    Converged = run.Solve.Converged,
                    Iterations = run.Solve.Iterations,
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
                SupplyAnchorId = hint?.SupplyAnchorId,
                ReturnAnchorId = hint?.ReturnAnchorId,
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

    private static LayoutWire Layout(LayoutHints hints) => new()
    {
        Order = hints.Order,
        Rank = Ordered(hints.Rank, static rank => rank),
        ThermalStages = [.. hints.ThermalStages.Select(static stage => new ThermalStageWire(stage.Rank, stage.Role.ToString().ToLowerInvariant(), stage.Components))],
        Flow = hints.Flow
            .OrderBy(static pair => int.Parse(pair.Key[1..], CultureInfo.InvariantCulture))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString().ToLowerInvariant(), StringComparer.Ordinal),
        PortSides = Ordered(hints.PortSides, static side => side.ToString().ToLowerInvariant()),
        Loops = hints.Loops,
        LoopOrientations = [.. hints.LoopOrientations.Select(static orientation => orientation.ToString().ToLowerInvariant())],
        Groups = [.. hints.Groups.Select(static group => new ComponentGroupWire(group.ParentComponentId, group.Children))],
        NonFlowElements = [.. hints.NonFlowElements.Select(static element => new NonFlowElementWire(
            element.ComponentId, element.PlacementAnchorId, element.MeasurementTargetId, element.ActuationTargetId, element.NavigationOrder))],
        CircuitOf = Ordered(hints.CircuitOf, static circuit => circuit),
        DistributionGroups = [.. hints.DistributionGroups.Select(static group => new DistributionGroupWire(group.ParentCircuit, group.Members))],
        Inferred = [.. hints.Inferred.Order(StringComparer.Ordinal)],
        BranchShapes = Ordered(hints.BranchShapes, static shape => shape),
    };

    private static Dictionary<string, TOut> Ordered<TIn, TOut>(IEnumerable<KeyValuePair<string, TIn>> pairs, Func<TIn, TOut> map) =>
        pairs.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, pair => map(pair.Value), StringComparer.Ordinal);

    // ---- visualization -------------------------------------------------------------------------------------

    private static readonly ImmutableDictionary<string, (string Name, string Display, Dimension Dimension, bool Diverging)> Properties =
        new Dictionary<string, (string, string, Dimension, bool)>(StringComparer.Ordinal)
        {
            ["temperature"] = ("temperature", "Temperature", Dimension.Temperature, false),
            ["t"] = ("temperature", "Temperature", Dimension.Temperature, false),
            ["pressure"] = ("pressure", "Pressure", Dimension.Pressure, false),
            ["p"] = ("pressure", "Pressure", Dimension.Pressure, false),
            ["flow"] = ("flow", "Mass flow", Dimension.MassFlow, false),
            ["mdot"] = ("flow", "Mass flow", Dimension.MassFlow, false),
            ["pressure_drop"] = ("pressure_drop", "Pressure drop", Dimension.PressureDelta, true),
            ["dp"] = ("pressure_drop", "Pressure drop", Dimension.PressureDelta, true),
            ["enthalpy"] = ("enthalpy", "Enthalpy", Dimension.Enthalpy, false),
            ["h"] = ("enthalpy", "Enthalpy", Dimension.Enthalpy, false),
            ["density"] = ("density", "Density", Dimension.Density, false),
            ["rho"] = ("density", "Density", Dimension.Density, false),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>The <c>show</c> directive resolved against what was solved (<c>57</c>).</summary>
    /// <remarks>
    /// The binder does not read <c>show</c> yet, so this reads the syntax: the first directive, its
    /// properties by long or short name, and its range when it states one. A property with no
    /// solved values has a <see langword="null"/> domain, which is <c>57</c>'s "before the first solve".
    /// </remarks>
    private static VisualizationWire Visualization(
        ScriptSyntax root, CircuitGraph graph, ImmutableArray<ImmutableArray<SolvedPort?>>? ports, ImmutableArray<Diagnostic>.Builder raised)
    {
        var directive = root.Statements.OfType<ShowDirectiveSyntax>().FirstOrDefault();
        var named = directive?.Properties
            .Select(property => Properties.GetValueOrDefault(property.Text).Name)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray() ?? [];
        var active = named.Length > 0 ? named[0] : "temperature";
        var available = named.Concat(["temperature", "pressure", "flow"]).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var (_, display, dimension, diverging) = Properties[active];
        var unit = UnitTable.CanonicalUnitFor(dimension);

        double? min = null, max = null;

        if (directive?.Scale is { From: NumberLiteralSyntax from, To: NumberLiteralSyntax to }
            && double.IsFinite(from.Value) && double.IsFinite(to.Value))
        {
            (min, max) = (Math.Min(from.Value, to.Value), Math.Max(from.Value, to.Value));
        }
        else if (ports is { } solved)
        {
            foreach (var value in Values(active, graph, solved, unit))
            {
                min = min is null ? value : Math.Min(min.Value, value);
                max = max is null ? value : Math.Max(max.Value, value);
            }

            if (min is not null && max is not null)
            {
                (min, max) = diverging ? Symmetric(min.Value, max.Value) : Nice(min.Value, max.Value);
            }
        }

        _ = raised;

        return new VisualizationWire
        {
            Active = active,
            Available = available,
            Scale = new ScaleWire
            {
                Property = active,
                DisplayName = display,
                Unit = unit?.Text ?? string.Empty,
                Kind = diverging ? "diverging" : "sequential",
                Domain = min is { } low && max is { } high ? new DomainWire(Round(low), Round(high), Nice: directive?.Scale is null) : null,
                Degenerate = min is not null && min == max,
            },
        };
    }

    private static IEnumerable<double> Values(
        string property, CircuitGraph graph, ImmutableArray<ImmutableArray<SolvedPort?>> ports, UnitSymbol? unit)
    {
        for (var i = 0; i < graph.Components.Length; i++)
        {
            var component = graph.Components[i];

            foreach (var port in ports[i])
            {
                if (port is not { } at)
                {
                    continue;
                }

                double? si = property switch
                {
                    "temperature" when component is CircuitNode => at.Temperature,
                    "pressure" when component is CircuitNode => at.Pressure,
                    "enthalpy" when component is CircuitNode => at.Enthalpy,
                    "density" when component is CircuitNode => at.Density,
                    "flow" when component is not CircuitNode => Math.Abs(at.Flow),
                    _ => null,
                };

                if (si is { } value && double.IsFinite(value))
                {
                    yield return unit is null ? value : Quantity.FromSi(value, unit.Dimension).ValueIn(unit);
                }

                if (component is CircuitNode)
                {
                    break;
                }
            }
        }
    }

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

    private static (double Min, double Max) Symmetric(double min, double max)
    {
        var (low, high) = Nice(Math.Min(min, 0), Math.Max(max, 0));
        var reach = Math.Max(-low, high);
        return (-reach, reach);
    }

    // ---- bindings and diagnostics --------------------------------------------------------------------------

    private static BindingWire BindingOf(BindingSymbol binding, ImmutableArray<Diagnostic>.Builder raised)
    {
        if (binding.Value is not { } quantity)
        {
            return new BindingWire(binding.Name, null, null);
        }

        var (value, unit) = Canonical(quantity.SiValue, quantity.Dimension, binding.Name, "value", raised);
        return new BindingWire(binding.Name, value, unit);
    }

    private static ImmutableArray<DiagnosticWire> Diagnostics(SourceText source, ImmutableArray<Diagnostic> diagnostics) =>
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
                Component = diagnostic.ComponentName,
                Suggestion = diagnostic.Suggestion is { } fix
                    ? new SuggestionWire(fix.Title, Range(source, fix.Span), fix.Replacement)
                    : null,
                Related = [.. diagnostic.Related.Select(related => new RelatedWire(related.Message, Range(source, related.Span)))],
            }),
    ];

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
