using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Solvers;

/// <summary>One node whose enthalpy can reach a node port, and the port it arrives through.</summary>
/// <param name="Node">The node the enthalpy is read from.</param>
/// <param name="Component">The component carrying it across, in graph order.</param>
/// <param name="Port">The port of that component the flow enters by.</param>
internal readonly record struct ArrivingSource(int Node, int Component, int Port);

/// <summary>The assembled residual function: everything the solver drives to zero, at one iterate.</summary>
/// <remarks>
/// <para>
/// <strong>It consumes the counting table rather than re-deriving it</strong> (<c>S-9</c>). Both
/// layouts are built against <see cref="CountingTable"/> and this holds them together: the residual
/// vector is <see cref="EquationLayout"/> long, the state vector is <see cref="SystemLayout"/> long,
/// and a disagreement between them is a bug in one of two places rather than a mystery in the solve.
/// </para>
/// <para>
/// <strong>Every port state is evaluated once per iterate, never once per residual</strong>
/// (<c>S-2</c>). A seven-property state fix is ~204 µs and allocates; a residual runs N+1 times per
/// Newton iteration for a numerical Jacobian. Fixing states inside <c>EvaluateResiduals</c> would put
/// a 20-unknown circuit past <c>07</c>'s whole interactive budget before any linear algebra, which is
/// why <see cref="SolveContext"/> carries evaluated properties and no way to produce them.
/// </para>
/// <para>
/// <strong>Energy is added by the assembler, not claimed by a component</strong> (<c>D-69</c>). A node
/// writes <c>Σ ṁᵢ h(ṁᵢ)</c> over its own ports and knows nothing about the duty of the exchanger
/// discharging into it; this collects each component's injection and adds it to the node rows it
/// reaches. That is what keeps the heat following the flow through a reversal instead of being nailed
/// to a port.
/// </para>
/// </remarks>
public sealed class EquationSystem
{
    private const double MixingFloor = 1e-6;

    /// <summary>The state of a port with nothing on it: zeros, and never read by a residual.</summary>
    /// <remarks>
    /// An optional port left open carries no flow, so every term it could enter is multiplied by zero.
    /// A component that reads it anyway is the defect <c>S-14a</c> was, and the reconciliation test
    /// that closed it is what keeps this unreachable rather than merely unlikely.
    /// </remarks>
    private static readonly PortState Vacant = new()
    {
        Pressure = 0,
        Enthalpy = 0,
        Temperature = 0,
        Density = 0,
        SpecificHeat = 0,
        DynamicViscosity = 0,
        ThermalConductivity = 0,
    };

    private readonly CircuitGraph _graph;
    private readonly PortMap _ports;
    private readonly int[] _nodeOf;
    private readonly int[] _energyRow;
    private readonly ArrivingSource[][][] _arriving;
    private readonly (int Node, double Value)[] _stated;
    private readonly int[] _datums;
    private readonly (int From, int To)[] _links;
    private readonly (int Offset, int Count)[] _owned;
    private readonly (int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)[] _fluxes;
    private readonly double[][] _parameters;
    private readonly (int Element, int Slot, int Column)[] _promoted;
    private readonly Constraint[] _constraints;

    private readonly PortState[] _nodeStates;
    private readonly double[] _nodeInjection;

    /// <summary>One stated constraint, resolved to the node states its residual reads.</summary>
    /// <param name="Row">The row it writes, or the row of one nothing resolved.</param>
    /// <param name="Node">The node whose temperature it reads, or −1 when nothing resolved it.</param>
    /// <param name="Reference">The node subtracted from it, or −1 for an absolute temperature.</param>
    /// <param name="Target">K. An absolute temperature, or the magnitude of a difference.</param>
    /// <param name="Sign">+1 where the difference is a rise across the component, −1 where it is a drop.</param>
    private readonly record struct Constraint(int Row, int Node, int Reference, double Target, double Sign);
    private readonly PortState[] _portScratch;
    private readonly double[] _flowScratch;
    private readonly double[] _ownScratch;
    private readonly double[] _residualScratch;
    private readonly double[] _injectionScratch;

    private EquationSystem(
        CircuitGraph graph,
        SystemLayout unknowns,
        EquationLayout equations,
        PortMap ports,
        ImmutableArray<double> unknownScales,
        ImmutableArray<double> residualScales,
        int[] nodeOf,
        int[] energyRow,
        ArrivingSource[][][] arriving,
        (int Node, double Value)[] stated,
        int[] datums,
        (int From, int To)[] links,
        (int Offset, int Count)[] owned,
        (int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)[] fluxes,
        double[][] parameters,
        (int Element, int Slot, int Column)[] promoted,
        Constraint[] constraints)
    {
        _graph = graph;
        _ports = ports;
        _nodeOf = nodeOf;
        _energyRow = energyRow;
        _arriving = arriving;
        _stated = stated;
        _datums = datums;
        _links = links;
        _owned = owned;
        _fluxes = fluxes;
        _parameters = parameters;
        _promoted = promoted;
        _constraints = constraints;

        Unknowns = unknowns;
        Equations = equations;
        UnknownScales = unknownScales;
        ResidualScales = residualScales;

        var widest = 0;
        var tallest = 0;

        foreach (var component in graph.Components)
        {
            widest = Math.Max(widest, component.Ports.Length);
            tallest = Math.Max(tallest, component.EquationCount);
        }

        _nodeStates = new PortState[graph.Nodes.Length];
        _nodeInjection = new double[graph.Nodes.Length];
        _portScratch = new PortState[widest];
        _flowScratch = new double[widest];
        _injectionScratch = new double[widest];
        _residualScratch = new double[tallest];
        _ownScratch = new double[2];
        OutOfDomainNode = -1;
    }

    /// <summary>Gets the state vector's layout.</summary>
    public SystemLayout Unknowns { get; }

    /// <summary>Gets the residual vector's layout.</summary>
    public EquationLayout Equations { get; }

    /// <summary>Gets the reference magnitude of every unknown, in the state vector's order.</summary>
    public ImmutableArray<double> UnknownScales { get; }

    /// <summary>Gets the reference magnitude of every residual, in the residual vector's order.</summary>
    public ImmutableArray<double> ResidualScales { get; }

    /// <summary>Gets how many unknowns the system has.</summary>
    public int Columns => Unknowns.Count;

    /// <summary>Gets how many equations the system has.</summary>
    public int Rows => Equations.Count;

    /// <summary>Gets whether the graph is solved as an equilibrium or in time.</summary>
    /// <remarks>
    /// Carried so a solver can refuse a system it is the wrong kind for before iterating on it, which
    /// turns a divergence into a sentence.
    /// </remarks>
    public SolveMode Mode => _graph.Mode;

    /// <summary>Names a node, for a message about it.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <returns>Its identifier, or a placeholder when the index names none.</returns>
    public string NodeName(int node) =>
        (uint)node < (uint)_graph.Nodes.Length ? _graph.Nodes[node].Name : "the circuit";

    /// <summary>Gets the node whose state could not be evaluated, or <c>-1</c>.</summary>
    /// <value>
    /// Set by a failed evaluation and meaningful only then. A Newton step that leaves the property
    /// domain is an ordinary event on the path to a solution — it is what the line search's domain
    /// guard exists for — so it is reported rather than thrown.
    /// </value>
    public int OutOfDomainNode { get; private set; }

    /// <summary>Gets the rows nothing evaluates yet, with the package that will.</summary>
    /// <value>
    /// The promotion pairings. A stated <c>in</c> is met by moving a sized parameter, and <c>P3.7</c>
    /// is what promotes one for real; until then the row exists — it has to, or the system is not
    /// square — and its residual is left at zero.
    /// <para>
    /// <strong>Named rather than silently zero.</strong> A row of zeros makes a singular Jacobian, and
    /// a singular Jacobian with no explanation is the single most expensive thing to debug in a solver.
    /// </para>
    /// </value>
    public ImmutableArray<EquationDeclaration> Unevaluated =>
        [.. Equations.Rows
            .Skip(Equations.ConstraintOffset)
            .Where((_, index) => _constraints[index].Node < 0)];

    /// <summary>Assembles the system of a lowered graph.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">The counting table and the hydraulic partition.</param>
    /// <param name="seed">The starting iterate, whose flow magnitudes set the flow scales.</param>
    /// <returns>The assembled system.</returns>
    public static EquationSystem Build(CircuitGraph graph, WellPosednessResult posedness, StateVector seed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(posedness);
        ArgumentNullException.ThrowIfNull(seed);

        var unknowns = SystemLayout.Build(graph, posedness.Counting);
        var equations = EquationLayout.Build(graph, posedness);
        var ports = PortMap.Build(graph);
        var unknownScales = FluidScript.Core.Solvers.UnknownScales.Build(unknowns, seed);
        var residualScales = FluidScript.Core.Solvers.ResidualScales.Build(
            graph, equations, ports, unknownScales, unknowns);

        var nodeOf = new int[graph.Components.Length];
        var byComponent = new Dictionary<object, int>(graph.Nodes.Length, ReferenceEqualityComparer.Instance);

        Array.Fill(nodeOf, -1);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            byComponent[graph.Nodes[node].Component] = node;
        }

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (byComponent.TryGetValue(graph.Components[element], out var node))
            {
                nodeOf[element] = node;
            }
        }

        var energyRow = new int[graph.Nodes.Length];
        var arriving = new ArrivingSource[graph.Components.Length][][];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is not CircuitNode node)
            {
                arriving[element] = [];
                continue;
            }

            energyRow[nodeOf[element]] =
                equations.Row(element, node.CarriesMassBalance ? 1 : 0);

            arriving[element] = new ArrivingSource[node.Ports.Length][];

            for (var port = 0; port < node.Ports.Length; port++)
            {
                arriving[element][port] = Sources(graph, ports, byComponent, element, port);
            }
        }

        var stated = new List<(int, double)>(posedness.Counting.PressureNodes.Length);

        foreach (var node in posedness.Counting.PressureNodes)
        {
            stated.Add((
                byComponent[node.Component],
                HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) ?? 0));
        }

        var datums = new List<int>(posedness.Counting.DatumComponents.Length);

        foreach (var hydraulic in posedness.Counting.DatumComponents)
        {
            var datum = graph.Nodes.FirstOrDefault(candidate => candidate.Name == hydraulic.Datum);

            datums.Add(datum is null ? -1 : byComponent[datum.Component]);
        }

        var links = posedness.Counting.IdealLinks
            .Select(link => (byComponent[link.From.Component], byComponent[link.To.Component]))
            .ToArray();

        // Where each component's own unknowns sit, walked in the order WellPosedness gathered them --
        // graph order over the non-node components. Two walks of one list, and they have to agree.
        var owned = new (int Offset, int Count)[graph.Components.Length];
        var running = 0;

        for (var element = 0; element < graph.Components.Length; element++)
        {
            if (graph.Components[element] is CircuitNode)
            {
                continue;
            }

            var count = graph.Components[element].DeclareUnknowns().Length;

            owned[element] = (running, count);
            running += count;
        }

        // An external mass flux enters a node's own balances, and until it does the column influences
        // nothing and the Jacobian is singular at it. The enthalpy it carries is settled once, here: a
        // boundary stating a temperature delivers fluid at that temperature, and one that does not is a
        // return, whose stream leaves carrying whatever the node holds.
        //
        // A *stated* flow is the same term with no column behind it (`S-22`). Well-posedness leaves it
        // out of `FluxNodes` because it declares no unknown, which is right for the count and was wrong
        // for the residuals: the flux then entered no equation at all, so `m4-storage-header` -- whose
        // every boundary is a stated flow -- had a circuit at rest as its exact solution, and reported
        // convergence for it.
        var fluxes = new List<(int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)>(
            posedness.Counting.FluxNodes.Length);

        foreach (var boundary in graph.Nodes)
        {
            var column = posedness.Counting.FluxNodes.IndexOf(boundary);
            var given = HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Flow);

            if (column < 0 && (given is null || !boundary.Component.CarriesMassBalance))
            {
                continue;
            }

            var element = Array.IndexOf([.. graph.Components], (IFlowComponent)boundary.Component);
            var temperature = HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Temperature);
            var enthalpy = 0.0;
            var known = false;

            if (temperature is not null)
            {
                var state = graph.Substance.FromPressureTemperature(
                    Quantity.FromSi(
                        HydraulicPartition.Stated(boundary.Component, HydraulicPartition.Pressure) ?? 0,
                        Dimension.Pressure),
                    Quantity.FromSi(temperature.Value, Dimension.Temperature));

                if (state.TryGetValue(out var fluid))
                {
                    enthalpy = fluid.Enthalpy.SiValue;
                    known = true;
                }
            }

            fluxes.Add((
                byComponent[boundary.Component],
                equations.Row(element, 0),
                column < 0 ? -1 : unknowns.ExternalFluxOffset + column,
                boundary.Component.Boundary is BoundaryRole.Return ? -(given ?? 0) : given ?? 0,
                enthalpy,
                known));
        }

        // Every component gets a parameter buffer whether or not anything promotes into it, holding
        // what it would have used itself. That is what lets a residual read `context.Parameter` with no
        // idea whether the number came from the solve or from its own constructor.
        var parameters = new double[graph.Components.Length][];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var resolvable = graph.Components[element].Resolvable;

            parameters[element] = new double[resolvable.Length];

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                parameters[element][slot] = resolvable[slot].Value;
            }
        }

        var promoted = new List<(int Element, int Slot, int Column)>(posedness.Counting.Promotions.Length);

        for (var index = 0; index < posedness.Counting.Promotions.Length; index++)
        {
            var promotion = posedness.Counting.Promotions[index];
            var element = Array.FindIndex(
                [.. graph.Components],
                candidate => string.Equals(candidate.Name, promotion.Component, StringComparison.Ordinal));

            if (element < 0)
            {
                continue;
            }

            var resolvable = graph.Components[element].Resolvable;

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                if (string.Equals(resolvable[slot].Name, promotion.Parameter, StringComparison.Ordinal))
                {
                    promoted.Add((element, slot, unknowns.PromotionOffset + index));

                    break;
                }
            }
        }

        return new EquationSystem(
            graph, unknowns, equations, ports, unknownScales, residualScales,
            nodeOf, energyRow, arriving, [.. stated], [.. datums], links, owned, [.. fluxes],
            parameters, [.. promoted], Constraints(graph, posedness, equations, ports, byComponent));
    }

    /// <summary>Resolves each stated constraint to the node states its residual reads.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">The counting table, whose constraint order the rows follow.</param>
    /// <param name="equations">The row layout, for the offset the constraint block starts at.</param>
    /// <param name="ports">Which node each component port attaches to.</param>
    /// <param name="byComponent">Each node component's index among the graph's nodes.</param>
    /// <returns>One entry per constraint, in row order; <c>Node</c> is −1 for one nothing resolved.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Every constraint is a stated temperature, whatever <c>ConstraintKind</c> calls it.</strong>
    /// The kind names what the statement <em>achieves</em> — <c>power</c> beside <c>out</c> determines a
    /// flow — and the flow is determined through the node energy balances that already relate the two.
    /// Writing a second, flow-shaped residual for it would need the duty and both terminal enthalpies as
    /// constants, and a stated <c>out</c> alone supplies neither.
    /// </para>
    /// <para>
    /// A parameter's name is its port's name (<c>in</c>, <c>out</c>, <c>in2</c>, <c>out2</c>), which is
    /// why no kind-specific table appears here; <c>dt</c> and <c>dt2</c> are the difference across the
    /// matching pair. <strong><c>dt</c> is a magnitude and <c>power</c> carries the sign</strong>
    /// (<c>22</c>), so a consumer's residual reads <c>−(T_out − T_in) − dt</c> and a source's the other
    /// way round; taking <c>|T_out − T_in|</c> instead would put a kink at the one place the solver
    /// spends its time.
    /// </para>
    /// </remarks>
    private static Constraint[] Constraints(
        CircuitGraph graph,
        WellPosednessResult posedness,
        EquationLayout equations,
        PortMap ports,
        Dictionary<object, int> byComponent)
    {
        var resolved = new Constraint[posedness.Counting.Constraints.Length];

        for (var index = 0; index < resolved.Length; index++)
        {
            var constraint = posedness.Counting.Constraints[index];
            var row = equations.ConstraintOffset + index;
            var target = 0.0;
            var element = Array.FindIndex(
                [.. graph.Components],
                candidate => string.Equals(candidate.Name, constraint.Component, StringComparison.Ordinal));

            resolved[index] = new Constraint(row, -1, -1, 0, 1);

            if (element < 0
                || HydraulicPartition.Stated(graph.Components[element], constraint.Parameter) is not { } stated)
            {
                continue;
            }

            target = stated;

            if (graph.Components[element] is CircuitNode node)
            {
                resolved[index] = new Constraint(row, byComponent[node], -1, target, 1);

                continue;
            }

            var difference = constraint.Parameter is "dt" or "dt2";
            var suffix = constraint.Parameter.EndsWith('2') ? "2" : string.Empty;
            var outlet = Attached(graph, ports, element, "out" + suffix);

            if (!difference)
            {
                resolved[index] = new Constraint(
                    row, Attached(graph, ports, element, constraint.Parameter), -1, target, 1);

                continue;
            }

            var duty = HydraulicPartition.Stated(graph.Components[element], "power") ?? 0;

            resolved[index] = new Constraint(
                row,
                outlet,
                Attached(graph, ports, element, "in" + suffix),
                target,
                duty < 0 ? -1 : 1);
        }

        return resolved;
    }

    /// <summary>The node a named port of one component attaches to.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">Which node each component port attaches to.</param>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="name">The port's name.</param>
    /// <returns>The node's index among the graph's nodes, or −1 when the port has none.</returns>
    private static int Attached(CircuitGraph graph, PortMap ports, int element, string name)
    {
        var declared = graph.Components[element].Ports;

        for (var port = 0; port < declared.Length; port++)
        {
            if (string.Equals(declared[port].Name, name, StringComparison.Ordinal))
            {
                return ports[element, port].Node;
            }
        }

        return -1;
    }

    /// <summary>Evaluates every residual at one iterate, in SI.</summary>
    /// <param name="x">The iterate, <see cref="Columns"/> long.</param>
    /// <param name="residuals">Destination, <see cref="Rows"/> long.</param>
    /// <returns>
    /// <see langword="false"/> when a node's state left the property domain, naming it in
    /// <see cref="OutOfDomainNode"/>; the residuals are then meaningless and the caller must shorten
    /// its step rather than read them.
    /// </returns>
    /// <exception cref="ArgumentException">A span is the wrong length.</exception>
    public bool TryEvaluateResiduals(ReadOnlySpan<double> x, Span<double> residuals)
    {
        if (x.Length != Columns)
        {
            throw new ArgumentException($"Expected {Columns} unknowns, got {x.Length}.", nameof(x));
        }

        if (residuals.Length != Rows)
        {
            throw new ArgumentException($"Expected {Rows} residuals, got {residuals.Length}.", nameof(residuals));
        }

        OutOfDomainNode = -1;

        for (var node = 0; node < _graph.Nodes.Length; node++)
        {
            if (!Refresh(node, x))
            {
                return false;
            }
        }

        Assemble(x, residuals);

        return true;
    }

    /// <summary>Evaluates at an iterate that differs from the last full one in a single unknown.</summary>
    /// <param name="x">The perturbed iterate, <see cref="Columns"/> long.</param>
    /// <param name="column">The unknown that moved.</param>
    /// <param name="residuals">Destination, <see cref="Rows"/> long.</param>
    /// <returns><see langword="false"/> when the perturbed node left the property domain.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The finite-difference Jacobian's whole cost is here.</strong> A forward-difference column
    /// is one residual evaluation, and the naive one re-fixes every node's state — so an N-column sweep
    /// costs N² property calls where N of them changed anything. Perturbing a branch flow, an external
    /// flux, a promoted parameter or a component's own unknown changes <em>no</em> fluid state at all,
    /// and perturbing a node's pressure or enthalpy changes exactly one.
    /// </para>
    /// <para>
    /// On a 200-component model that is the difference between roughly a second and roughly fifty
    /// milliseconds per Newton iteration, against <c>07</c>'s whole interactive budget. It is built in
    /// rather than retrofitted because the shape of the saving decides the shape of the cache, and a
    /// cache added afterwards is a cache the residual path was not written for (<c>S-2</c>).
    /// </para>
    /// <para>
    /// <strong>It requires the cache to hold the base iterate.</strong> Call
    /// <see cref="TryEvaluateResiduals"/> at the base point first; this restores the node it touched, so
    /// columns may be swept in any order without a refresh between them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A span is the wrong length.</exception>
    public bool TryEvaluateAt(ReadOnlySpan<double> x, int column, Span<double> residuals)
    {
        if (x.Length != Columns)
        {
            throw new ArgumentException($"Expected {Columns} unknowns, got {x.Length}.", nameof(x));
        }

        if (residuals.Length != Rows)
        {
            throw new ArgumentException($"Expected {Rows} residuals, got {residuals.Length}.", nameof(residuals));
        }

        OutOfDomainNode = -1;

        var dirty = NodeOfUnknown(column);

        if (dirty < 0)
        {
            Assemble(x, residuals);

            return true;
        }

        var saved = _nodeStates[dirty];

        if (!Refresh(dirty, x))
        {
            _nodeStates[dirty] = saved;

            return false;
        }

        Assemble(x, residuals);
        _nodeStates[dirty] = saved;

        return true;
    }

    /// <summary>Which node's fluid state one unknown decides, or <c>-1</c> when it decides none.</summary>
    /// <param name="column">The unknown's index in the state vector.</param>
    /// <returns>The node index, or <c>-1</c>.</returns>
    /// <remarks>
    /// A state is fixed from a node's own pressure and enthalpy and from nothing else. A flow, a flux, a
    /// promoted parameter and a control volume's own enthalpy all enter residuals directly and never a
    /// property call — which is what makes most Jacobian columns free of the property backend entirely.
    /// </remarks>
    public int NodeOfUnknown(int column)
    {
        var pressure = column - Unknowns.NodePressureOffset;

        if (pressure >= 0 && pressure < _graph.Nodes.Length)
        {
            return pressure;
        }

        var enthalpy = column - Unknowns.NodeEnthalpyOffset;

        return enthalpy >= 0 && enthalpy < _graph.Nodes.Length ? enthalpy : -1;
    }

    /// <summary>Writes every residual from the node states the cache currently holds.</summary>
    /// <param name="x">The iterate.</param>
    /// <param name="residuals">Destination.</param>
    private void Assemble(ReadOnlySpan<double> x, Span<double> residuals)
    {
        residuals.Clear();
        Array.Clear(_nodeInjection);

        // Promoted parameters, before anything reads one. A component's residual cannot tell a value
        // the solver is varying from one its constructor chose, which is the point (`D-76`).
        foreach (var (element, slot, column) in _promoted)
        {
            _parameters[element][slot] = x[column];
        }

        for (var element = 0; element < _graph.Components.Length; element++)
        {
            var component = _graph.Components[element];

            if (!component.InjectsEnergy)
            {
                continue;
            }

            var ports = Fill(element, x);
            var injection = _injectionScratch.AsSpan(0, ports);

            component.EvaluateEnergyInjection(Context(element, ports, x), injection);

            for (var port = 0; port < ports; port++)
            {
                var node = _ports[element, port].Node;

                if (node >= 0)
                {
                    _nodeInjection[node] += injection[port];
                }
            }
        }

        for (var element = 0; element < _graph.Components.Length; element++)
        {
            var component = _graph.Components[element];
            var ports = Fill(element, x);
            var written = _residualScratch.AsSpan(0, component.EquationCount);

            written.Clear();
            component.EvaluateResiduals(Context(element, ports, x), written);

            for (var local = 0; local < written.Length; local++)
            {
                var row = Equations.Row(element, local);

                if (row >= 0)
                {
                    residuals[row] = written[local];
                }
            }
        }

        // A node whose energy balance was dropped as redundant takes no injection and no boundary
        // stream (`D-75`): its row does not exist, and the duty it would have carried is already in the
        // rows that remain, since dropping a row that is the negated sum of the others loses nothing.
        for (var node = 0; node < _graph.Nodes.Length; node++)
        {
            if (_energyRow[node] >= 0)
            {
                residuals[_energyRow[node]] += _nodeInjection[node];
            }
        }

        // Mass crossing the circuit's boundary, and the energy it carries with it. The upwind blend is
        // the one a node already uses on its ports: an inflow arrives at the stated temperature, an
        // outflow leaves with what the node holds, and the two blend smoothly so a boundary may reverse.
        foreach (var (node, massRow, column, magnitude, boundary, known) in _fluxes)
        {
            var flux = column >= 0 ? x[column] : magnitude;
            var own = x[Unknowns.NodeEnthalpy(node)];

            if (massRow >= 0)
            {
                residuals[massRow] += flux;
            }

            if (_energyRow[node] >= 0)
            {
                residuals[_energyRow[node]] += flux * Smoothing.Upwind(flux, known ? boundary : own, own);
            }
        }

        // A stated temperature, held against the node it was stated about. The node states are current:
        // every one of them was refreshed before this method ran, so this costs no property call.
        foreach (var (row, node, reference, target, sign) in _constraints)
        {
            if (node < 0)
            {
                continue;
            }

            residuals[row] = reference < 0
                ? _nodeStates[node].Temperature - target
                : (sign * (_nodeStates[node].Temperature - _nodeStates[reference].Temperature)) - target;
        }

        var assembly = Equations.LinkOffset;

        // D-25's zero-drop connection: two nodes with nothing between them are one pressure, and no
        // component is there to say so.
        foreach (var (from, to) in _links)
        {
            residuals[assembly++] = x[Unknowns.NodePressure(from)] - x[Unknowns.NodePressure(to)];
        }

        foreach (var (node, value) in _stated)
        {
            residuals[assembly++] = x[Unknowns.NodePressure(node)] - value;
        }

        foreach (var datum in _datums)
        {
            residuals[assembly++] = datum < 0 ? 0 : x[Unknowns.NodePressure(datum)];
        }
    }

    /// <summary>Evaluates every residual and divides each by its own reference magnitude.</summary>
    /// <param name="x">The iterate, <see cref="Columns"/> long.</param>
    /// <param name="residuals">Destination, <see cref="Rows"/> long.</param>
    /// <returns><see langword="false"/> when a node's state left the property domain.</returns>
    /// <remarks>
    /// This is the vector a convergence test may take a norm of. The unscaled one is what a message
    /// quotes — "off by 4.2 kW" is only sayable in watts.
    /// </remarks>
    public bool TryEvaluateScaled(ReadOnlySpan<double> x, Span<double> residuals)
    {
        if (!TryEvaluateResiduals(x, residuals))
        {
            return false;
        }

        Scale(residuals);

        return true;
    }

    /// <summary>Evaluates scaled at an iterate differing from the last full one in a single unknown.</summary>
    /// <param name="x">The perturbed iterate, <see cref="Columns"/> long.</param>
    /// <param name="column">The unknown that moved.</param>
    /// <param name="residuals">Destination, <see cref="Rows"/> long.</param>
    /// <returns><see langword="false"/> when the perturbed node left the property domain.</returns>
    /// <remarks>
    /// <strong>The scaled pair exists so a Jacobian cannot mix the two.</strong> A forward difference
    /// takes the base residuals from one call and the perturbed ones from another, and if one is scaled
    /// and the other is not, every entry is off by that row's reference magnitude — around <c>1e5</c>
    /// here, which produces a Jacobian of order <c>1e12</c> and a singularity report on a circuit that
    /// is fine. That is what happened first, and it is why the unscaled overload is not simply reused
    /// with a division bolted on at the call site.
    /// </remarks>
    public bool TryEvaluateScaledAt(ReadOnlySpan<double> x, int column, Span<double> residuals)
    {
        if (!TryEvaluateAt(x, column, residuals))
        {
            return false;
        }

        Scale(residuals);

        return true;
    }

    /// <summary>Divides every residual by its own reference magnitude, in place.</summary>
    /// <param name="residuals">The residuals, in SI.</param>
    private void Scale(Span<double> residuals)
    {
        for (var row = 0; row < residuals.Length; row++)
        {
            residuals[row] /= ResidualScales[row];
        }
    }

    /// <summary>Re-evaluates one node's fluid state at the current iterate.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <param name="x">The iterate.</param>
    /// <returns><see langword="false"/> when it left the property domain.</returns>
    /// <remarks>
    /// One node rather than all of them, because that is what a Jacobian column needs
    /// (<see cref="TryEvaluateAt"/>). This is the only place in a solve that calls the property backend,
    /// and every microsecond of a solve that is not linear algebra is spent here.
    /// </remarks>
    private bool Refresh(int node, ReadOnlySpan<double> x)
    {
        var state = _graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(x[Unknowns.NodePressure(node)], Dimension.Pressure),
            Quantity.FromSi(x[Unknowns.NodeEnthalpy(node)], Dimension.Enthalpy));

        if (!state.TryGetValue(out var fluid))
        {
            OutOfDomainNode = node;

            return false;
        }

        _nodeStates[node] = new PortState
        {
            Pressure = fluid.Pressure.SiValue,
            Enthalpy = fluid.Enthalpy.SiValue,
            Temperature = fluid.Temperature.SiValue,
            Density = fluid.Density.SiValue,
            SpecificHeat = fluid.SpecificHeat.SiValue,
            DynamicViscosity = fluid.DynamicViscosity.SiValue,
            ThermalConductivity = fluid.ThermalConductivity.SiValue,
        };

        return true;
    }

    /// <summary>Fills the scratch buffers with one component's port states and flows.</summary>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="x">The iterate.</param>
    /// <returns>How many ports were filled.</returns>
    private int Fill(int element, ReadOnlySpan<double> x)
    {
        var component = _graph.Components[element];
        var node = _nodeOf[element];

        for (var port = 0; port < component.Ports.Length; port++)
        {
            var binding = _ports[element, port];

            _flowScratch[port] = binding.CarriesFlow
                ? binding.Sign * x[Unknowns.BranchFlow(binding.Branch)]
                : 0;

            _portScratch[port] = node >= 0
                ? _nodeStates[node] with { Enthalpy = Arriving(element, port, x, node) }
                : binding.Node >= 0 ? _nodeStates[binding.Node] : Vacant;
        }

        if (node >= 0)
        {
            _ownScratch[CircuitNode.PressureIndex] = x[Unknowns.NodePressure(node)];
            _ownScratch[CircuitNode.EnthalpyIndex] = x[Unknowns.NodeEnthalpy(node)];
        }

        return component.Ports.Length;
    }

    /// <summary>Builds the context over the buffers <see cref="Fill"/> just wrote.</summary>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="ports">How many ports it has.</param>
    /// <param name="x">The iterate, which a component's own unknowns are sliced straight out of.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// A node's two unknowns are copied into a scratch pair because they are not adjacent in the state
    /// vector — pressures and enthalpies are separate blocks, which is what gives the Jacobian its
    /// structure. A component's own unknowns <em>are</em> adjacent, by construction, so they are a slice
    /// of the iterate and cost nothing to pass (<c>D-74</c>).
    /// </remarks>
    private SolveContext Context(int element, int ports, ReadOnlySpan<double> x)
    {
        var unknowns = _nodeOf[element] >= 0
            ? _ownScratch.AsSpan()
            : x.Slice(Unknowns.ComponentUnknownOffset + _owned[element].Offset, _owned[element].Count);

        return new SolveContext(
            _graph.Substance,
            _portScratch.AsSpan(0, ports),
            _flowScratch.AsSpan(0, ports),
            unknowns,
            _parameters[element]);
    }

    /// <summary>The enthalpy arriving at one node port from whatever is attached to it.</summary>
    /// <param name="element">The node's index in the graph.</param>
    /// <param name="port">The port.</param>
    /// <param name="x">The iterate.</param>
    /// <param name="node">The node's own index.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// <para>
    /// A node's port states belong to what is attached to it, and a node's energy balance reads them as
    /// the enthalpy an inflow carries. Crossing a two-port flow group gives that unambiguously — it is
    /// the node on the component's far side, and 68 of the corpus's 92 node ports are this case. A
    /// junction element has no single far side, so the arriving enthalpy is the **inflow-weighted mix**
    /// of the nodes at its other ports, which is what a mixing tee physically does.
    /// </para>
    /// <para>
    /// The weights are <see cref="Smoothing.ForwardShare"/> rather than <c>max(0, ṁ)</c>, so the mix is
    /// smooth through a reversal as <c>36</c> requires, and a small floor keeps the quotient defined
    /// when every other port is an outflow — a state the junction's own mass balance forbids at the
    /// solution but not on the path to it.
    /// </para>
    /// </remarks>
    private double Arriving(int element, int port, ReadOnlySpan<double> x, int node)
    {
        var sources = _arriving[element][port];

        if (sources.Length == 0)
        {
            return x[Unknowns.NodeEnthalpy(node)];
        }

        if (sources.Length == 1)
        {
            return x[Unknowns.NodeEnthalpy(sources[0].Node)];
        }

        var numerator = MixingFloor * x[Unknowns.NodeEnthalpy(node)];
        var denominator = MixingFloor;

        foreach (var source in sources)
        {
            var binding = _ports[source.Component, source.Port];
            var inflow = binding.CarriesFlow
                ? binding.Sign * x[Unknowns.BranchFlow(binding.Branch)]
                : 0;

            var weight = Smoothing.ForwardShare(inflow);

            numerator += weight * x[Unknowns.NodeEnthalpy(source.Node)];
            denominator += weight;
        }

        return numerator / denominator;
    }

    /// <summary>Which nodes can deliver enthalpy to one node port.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">The port map.</param>
    /// <param name="byComponent">Each node's index, by the component carrying its unknowns.</param>
    /// <param name="element">The node's index in the graph.</param>
    /// <param name="port">The port.</param>
    /// <returns>One entry per node that can, empty when nothing is attached.</returns>
    private static ArrivingSource[] Sources(
        CircuitGraph graph,
        PortMap ports,
        Dictionary<object, int> byComponent,
        int element,
        int port)
    {
        var peer = graph.Adjacency.Peer(element, port);

        if (!peer.Exists)
        {
            return [];
        }

        var attached = graph.Components[peer.Component];

        if (byComponent.TryGetValue(attached, out var direct))
        {
            return [new ArrivingSource(direct, peer.Component, peer.Port)];
        }

        var groups = attached.FlowGroups;
        var sources = new List<ArrivingSource>(groups.Length);

        for (var candidate = 0; candidate < groups.Length; candidate++)
        {
            if (candidate == peer.Port || groups[candidate] != groups[peer.Port])
            {
                continue;
            }

            var far = ports[peer.Component, candidate].Node;

            if (far >= 0)
            {
                sources.Add(new ArrivingSource(far, peer.Component, candidate));
            }
        }

        return [.. sources];
    }
}
