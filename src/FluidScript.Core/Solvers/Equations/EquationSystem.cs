using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Transient;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Equations;

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
    /// <summary>kg/s of the node's own enthalpy mixed into a junction's arriving stream.</summary>
    /// <remarks>
    /// One milligram per second. It keeps the mixing quotient defined while every other port of a
    /// junction is an outflow -- a state the junction's mass balance forbids at the solution and Newton
    /// passes through on the way -- and is a thousandth of the upwind band, so at any flow the circuit
    /// can carry it is invisible.
    /// </remarks>
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
    private readonly (int From, int To, double Rise)[] _links;
    private readonly (int Offset, int Count)[] _owned;
    private readonly (int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)[] _fluxes;
    private readonly double[][] _parameters;
    private readonly (int Element, int Slot, int Column, string? Holds)[] _promoted;
    private readonly Constraint[] _constraints;
    private readonly (int Node, int Anchor)[] _stagnant;

    private readonly PortState[] _nodeStates;
    private readonly double[] _nodeInjection;

    // The pinned view (D-139, D-140): a differential state's value replaces its energy balance, a
    // tank's ports read their layers, and a frozen promotion replaces the constraint row that promoted
    // it. NaN is "not pinned", so the steady path pays one comparison per row and no allocation.
    private readonly double[] _nodePin;
    private readonly double[][] _portPin;
    private readonly double[] _ownPin;
    private readonly (int Component, int Port)[][] _attached;
    private readonly double[] _frozen;
    private readonly int[] _promotedRow;

    // What the integrator reads off a pinned evaluation (33): each pinned node's energy balance before
    // the pin overwrote its row, each tank's layer states, and the two totals the drift accumulator
    // integrates. Watts throughout; nothing here is scaled.
    private readonly double[] _balance;
    private readonly int[] _elementOfNode;
    private readonly double[][] _layerPin;
    private readonly double[][] _layerMass;
    private readonly double[] _rateScratch;
    private double _injected;
    private double _boundary;

    /// <summary>One stated constraint, resolved to either node temperatures or a derived branch flow.</summary>
    /// <param name="Row">The row it writes, or the row of one nothing resolved.</param>
    /// <param name="Node">The node whose temperature it reads, or −1 when this is a flow constraint.</param>
    /// <param name="Reference">The node subtracted from it, or −1 for an absolute temperature.</param>
    /// <param name="Target">K. An absolute temperature, or the magnitude of a difference.</param>
    /// <param name="Sign">+1 where the difference is a rise, −1 where it is a drop.</param>
    /// <param name="FlowBranch">The fixed-flow branch, or −1 for a temperature residual.</param>
    /// <param name="FlowTarget">kg/s in the component's inlet-to-outlet direction.</param>
    /// <param name="FlowScale">K per (kg/s), preserving this constraint row's temperature scaling.</param>
    /// <param name="VolumeNode">For a stated volume flow, the node whose solved density converts it; −1 otherwise.</param>
    /// <param name="VolumeTarget">m³/s, the stated volume flow, in the component's inlet-to-outlet direction.</param>
    private readonly record struct Constraint(
        int Row,
        int Node,
        int Reference,
        double Target,
        double Sign,
        int FlowBranch = -1,
        double FlowTarget = 0,
        double FlowScale = 1,
        int VolumeNode = -1,
        double VolumeTarget = 0);
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
        (int From, int To, double Rise)[] links,
        (int Offset, int Count)[] owned,
        (int Node, int MassRow, int Column, double Magnitude, double Enthalpy, bool Known)[] fluxes,
        double[][] parameters,
        (int Element, int Slot, int Column, string? Holds)[] promoted,
        Constraint[] constraints,
        (int Node, int Anchor)[] stagnant,
        int[] promotedRow)
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
        _stagnant = stagnant;

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
        _nodePin = new double[graph.Nodes.Length];
        _ownPin = new double[graph.Components.Length];
        _portPin = new double[graph.Components.Length][];
        _attached = new (int Component, int Port)[graph.Components.Length][];
        _frozen = new double[promoted.Length];
        _promotedRow = new int[promoted.Length];
        _balance = new double[graph.Nodes.Length];
        _elementOfNode = new int[graph.Nodes.Length];
        _layerPin = new double[graph.Components.Length][];
        _layerMass = new double[graph.Components.Length][];
        Array.Fill(_elementOfNode, -1);

        for (var element = 0; element < nodeOf.Length; element++)
        {
            if (nodeOf[element] >= 0)
            {
                _elementOfNode[nodeOf[element]] = element;
            }
        }

        _rateScratch = new double[equations.Count];

        for (var element = 0; element < graph.Components.Length; element++)
        {
            _layerPin[element] = graph.Components[element] is Tank tank ? new double[tank.Layers] : [];
            _layerMass[element] = graph.Components[element] is Tank vessel ? new double[vessel.Layers] : [];

            Array.Fill(_layerMass[element], 1.0);
        }

        Array.Fill(_nodePin, double.NaN);
        Array.Fill(_ownPin, double.NaN);
        Array.Fill(_frozen, double.NaN);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var count = graph.Components[element].Ports.Length;

            _portPin[element] = new double[count];
            Array.Fill(_portPin[element], double.NaN);
            _attached[element] = new (int Component, int Port)[count];

            for (var port = 0; port < count; port++)
            {
                var peer = graph.Adjacency.Peer(element, port);

                _attached[element][port] = peer.Exists ? (peer.Component, peer.Port) : (-1, -1);
            }
        }

        // A frozen promotion writes over the row of the constraint that promoted it: the constraint is
        // released at t > 0 and the actuator it chose holds its design value (D-140).
        Array.Copy(promotedRow, _promotedRow, promotedRow.Length);

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
    /// <para>
    /// A fixed-flow constraint carries no node on purpose — its residual is written against a branch
    /// flow rather than a node temperature — so a missing node alone is not the test. A row is
    /// unevaluated only when it names neither.
    /// </para>
    /// </value>
    public ImmutableArray<EquationDeclaration> Unevaluated =>
        [.. Equations.Rows
            .Skip(Equations.ConstraintOffset)
            .Where((_, index) => _constraints[index].Node < 0 && _constraints[index].FlowBranch < 0)];

    /// <summary>Gets whether the system is currently the pinned view rather than the equilibrium.</summary>
    /// <value>
    /// <see langword="true"/> after <see cref="Pin"/> or <see cref="Freeze"/> until <see cref="Release"/>.
    /// Unpinned, a transient graph's system is its equilibrium — the design solve every run starts from
    /// (<c>D-141</c>); pinned, it is one step's algebraic problem (<c>D-139</c>).
    /// </value>
    public bool Pinned { get; private set; }

    /// <summary>Pins the differential states, so the next solve is one step's algebraic problem (<c>D-139</c>).</summary>
    /// <param name="states">
    /// J/kg, one per entry of <see cref="SystemLayout.Differential"/> in that order: pipe cells by node,
    /// then each tank's layers bottom to top.
    /// </param>
    /// <remarks>
    /// <para>
    /// A pinned node keeps its column and loses its energy balance, which becomes the identity
    /// <c>ṁ_nominal · (h − h_pinned) = 0</c> on the row's own watt scale — exactly what removing the
    /// unknown and substituting would solve, without re-indexing anything. Its balance is the ODE the
    /// integrator owns, not an algebraic equation.
    /// </para>
    /// <para>
    /// A tank's layers have no column. Each port reads the layer its level maps to, so the node the port
    /// feeds sees that layer's enthalpy arriving rather than the inflow-weighted mix a steady junction
    /// delivers, and the tank's own mixed enthalpy is pinned to the layers' mean so that it enters no
    /// equation the integrator does not already own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="states"/> is the wrong length.</exception>
    public void Pin(ReadOnlySpan<double> states)
    {
        var differential = Unknowns.Differential;

        if (states.Length != differential.Length)
        {
            throw new ArgumentException($"Expected {differential.Length} differential states, got {states.Length}.", nameof(states));
        }

        for (var index = 0; index < differential.Length; index++)
        {
            var state = differential[index];

            if (state.Column >= 0)
            {
                _nodePin[state.Column - Unknowns.NodeEnthalpyOffset] = states[index];
                continue;
            }

            if (_graph.Components[state.Element] is not Tank tank)
            {
                continue;
            }

            _layerPin[state.Element][state.Layer - 1] = states[index];

            for (var port = 0; port < tank.Ports.Length; port++)
            {
                if (tank.LayerForPort(port) == state.Layer)
                {
                    _portPin[state.Element][port] = states[index];
                }
            }

            // Equal-volume layers of one incompressible liquid: the mean is the mixed enthalpy.
            _ownPin[state.Element] = double.IsNaN(_ownPin[state.Element])
                ? states[index] / tank.Layers
                : _ownPin[state.Element] + (states[index] / tank.Layers);
        }

        Pinned = true;
    }

    /// <summary>Freezes a promoted parameter at a value, releasing the constraint that promoted it (<c>D-140</c>).</summary>
    /// <param name="promotion">The promotion's index in <c>CountingTable.Promotions</c>.</param>
    /// <param name="value">The parameter's value in its own SI unit: the design solve's, or what a controller or a schedule moved it to.</param>
    /// <remarks>
    /// The constraint row becomes <c>x − value</c> on the parameter's own scale. A stated <c>out.t</c>
    /// on an exchanger is a design point: it chose a pump head at t = 0 and is released after it; the
    /// head holds, and the outlet temperature follows the flow. A controller writes here once per
    /// accepted step; a schedule on a promoted parameter writes here at its time.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="promotion"/> names no promotion.</exception>
    public void Freeze(int promotion, double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(promotion);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(promotion, _frozen.Length);

        _frozen[promotion] = value;
        Pinned = true;
    }

    /// <summary>Returns the system to its equilibrium form: every pin and every freeze cleared.</summary>
    public void Release()
    {
        Array.Fill(_nodePin, double.NaN);
        Array.Fill(_ownPin, double.NaN);
        Array.Fill(_frozen, double.NaN);

        foreach (var pins in _portPin)
        {
            Array.Fill(pins, double.NaN);
        }

        Pinned = false;
    }

    /// <summary>Evaluates the pinned system at a state and reads each differential state's energy balance (<c>33</c>).</summary>
    /// <param name="x">The algebraic solution at the pinned state, <see cref="Columns"/> long.</param>
    /// <param name="balances">
    /// Destination, one per entry of <see cref="SystemLayout.Differential"/> in that order. W, positive
    /// when the state gains energy: <c>Σ ṁ_in (h_in − h) + Q̇</c> for a pipe cell, the upwind layer
    /// balance with the interface flows for a tank layer. Divided by the state's reference mass it is
    /// <c>dh/dt</c>.
    /// </param>
    /// <param name="inflows">
    /// Destination, same length and order: kg/s arriving at each state — a cell's incoming port flows, a
    /// layer's external inflows plus the interface flow entering it. The reference mass over this is
    /// the residence time the CFL limit is taken from (<c>33</c> §The step-size limit).
    /// </param>
    /// <param name="injected">W added to the circuit by every injecting component at this state.</param>
    /// <param name="boundary">W carried into the circuit by its boundary streams at this state, net of what leaves.</param>
    /// <returns><see langword="false"/> when a node's state left the property domain.</returns>
    /// <exception cref="InvalidOperationException">The system is not pinned.</exception>
    /// <remarks>
    /// One residual evaluation, allocation-free. The tank's layer balance is <c>33</c> §Stratified tank
    /// with a hard switch on the interface flows: <c>u_k = Σ_(j≤k) s_j</c> positive upward, layer
    /// <c>k</c> gaining from below when <c>u_(k−1) &gt; 0</c> and from above when <c>u_k &lt; 0</c>, and an
    /// outflow leaving at the layer's own enthalpy, which is the fixed-mass form.
    /// </remarks>
    public bool TryEvaluateRates(ReadOnlySpan<double> x, Span<double> balances, Span<double> inflows, out double injected, out double boundary)
    {
        if (!Pinned)
        {
            throw new InvalidOperationException("The rates of an unpinned system are its residuals; pin it first.");
        }

        var differential = Unknowns.Differential;

        if (balances.Length != differential.Length || inflows.Length != differential.Length)
        {
            throw new ArgumentException($"Expected {differential.Length} balances and inflows.", nameof(balances));
        }

        injected = 0;
        boundary = 0;

        if (!TryEvaluateResiduals(x, _rateScratch))
        {
            return false;
        }

        injected = _injected;
        boundary = _boundary;

        for (var index = 0; index < differential.Length; index++)
        {
            var state = differential[index];

            if (state.Column >= 0)
            {
                var node = state.Column - Unknowns.NodeEnthalpyOffset;
                var arriving = 0.0;

                if (_elementOfNode[node] >= 0)
                {
                    var count = Fill(_elementOfNode[node], x);

                    for (var port = 0; port < count; port++)
                    {
                        arriving += Math.Max(_flowScratch[port], 0);
                    }
                }

                balances[index] = _balance[node];
                inflows[index] = arriving;
                continue;
            }

            if (_graph.Components[state.Element] is not Tank tank)
            {
                balances[index] = 0;
                inflows[index] = 0;
                continue;
            }

            var ports = Fill(state.Element, x);
            var layers = _layerPin[state.Element];
            var own = layers[state.Layer - 1];
            var balance = 0.0;
            var inflow = 0.0;

            // External inflows into this layer, at the enthalpy the node delivers to the port.
            for (var port = 0; port < ports; port++)
            {
                if (tank.LayerForPort(port) == state.Layer && _flowScratch[port] > 0)
                {
                    balance += _flowScratch[port] * (_portScratch[port].Enthalpy - own);
                    inflow += _flowScratch[port];
                }
            }

            // Interface flows: the cumulative imbalance below the interface, positive upward.
            var below = Interface(tank, state.Layer - 1, ports);
            var above = Interface(tank, state.Layer, ports);

            if (state.Layer > 1 && below > 0)
            {
                balance += below * (layers[state.Layer - 2] - own);
                inflow += below;
            }

            if (state.Layer < tank.Layers && above < 0)
            {
                balance += -above * (layers[state.Layer] - own);
                inflow += -above;
            }

            balances[index] = balance;
            inflows[index] = inflow;
        }

        return true;
    }

    /// <summary>The internal flow across the interface above a layer, positive upward, from the port flows <see cref="Fill"/> left.</summary>
    /// <param name="tank">The tank.</param>
    /// <param name="layer">The layer below the interface, 1-based; 0 or the top layer is a wall.</param>
    /// <param name="ports">How many ports the tank has.</param>
    /// <returns>kg/s.</returns>
    private double Interface(Tank tank, int layer, int ports)
    {
        if (layer <= 0 || layer >= tank.Layers)
        {
            return 0;
        }

        var cumulative = 0.0;

        for (var port = 0; port < ports; port++)
        {
            if (tank.LayerForPort(port) <= layer)
            {
                cumulative += _flowScratch[port];
            }
        }

        return cumulative;
    }

    /// <summary>Restores density order in every tank after an accepted step (<c>33</c>, invariant 12).</summary>
    /// <param name="states">
    /// The differential state vector, in <see cref="SystemLayout.Differential"/> order, rewritten in
    /// place where a tank overturned. Pipe cells are untouched: a cell has one enthalpy and no stack.
    /// </param>
    /// <param name="pooled">Receives how many layers ended up inside a pool, across every tank.</param>
    /// <returns>The tank whose layer left the property domain, or <see langword="null"/> on success.</returns>
    /// <remarks>
    /// Each tank's layers are contiguous in the layout, bottom to top, which is what lets one slice go
    /// to <see cref="Stratification.Remix"/>. The pressure is the tank's own, read from the node on its
    /// first port as the last evaluation left it; every layer shares it, because the tank's pressure
    /// equalities carry no hydrostatic term (<c>22</c>).
    /// </remarks>
    public string? Remix(Span<double> states, out int pooled)
    {
        var differential = Unknowns.Differential;
        var index = 0;

        pooled = 0;

        while (index < differential.Length)
        {
            if (differential[index].Column >= 0 || _graph.Components[differential[index].Element] is not Tank tank)
            {
                index++;
                continue;
            }

            var element = differential[index].Element;
            var attached = _attached[element][0];
            var pressure = attached.Component >= 0 && _nodeOf[attached.Component] >= 0
                ? _nodeStates[_nodeOf[attached.Component]].Pressure
                : 0;

            if (!Stratification.Remix(_layerMass[element], states.Slice(index, tank.Layers), _graph.Substance, pressure, out var turned))
            {
                return tank.Name;
            }

            pooled += turned;
            index += tank.Layers;
        }

        return null;
    }

    /// <summary>Records each tank's layer reference masses, so a remix needs no geometry of its own.</summary>
    /// <param name="masses">The reference mass of every differential state, in layout order. kg.</param>
    /// <remarks>
    /// Written once by the run before its first step, from <c>RunSnapshot.ReferenceMasses</c>. Until it
    /// is, every layer weighs 1, which gives the same pooled mean for the equal-volume layers of one
    /// liquid and differs only where a profile spans enough temperature for density to vary layer to
    /// layer. Supplying the masses makes the mean exact there and keeps the remix invisible to the
    /// drift accumulator.
    /// </remarks>
    public void SetLayerMasses(ReadOnlySpan<double> masses)
    {
        var differential = Unknowns.Differential;

        for (var index = 0; index < differential.Length; index++)
        {
            if (differential[index].Column < 0 && differential[index].Layer >= 1)
            {
                _layerMass[differential[index].Element][differential[index].Layer - 1] = masses[index];
            }
        }
    }

    /// <summary>The density of a node's state as the last evaluation left it.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <returns>kg/m³.</returns>
    /// <remarks>What a run fixes a pipe cell's reference mass from, at the design state (<c>33</c>).</remarks>
    public double NodeDensity(int node) => _nodeStates[node].Density;

    /// <summary>Moves a scheduled parameter to a value (<c>33</c> §Disturbances).</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The parameter's registry key: <c>power</c>, <c>position</c>, <c>kv</c>, <c>head</c>.</param>
    /// <param name="value">The value in the parameter's own SI unit.</param>
    /// <returns><see langword="false"/> when no component of that name resolves that parameter.</returns>
    /// <remarks>
    /// A stated or sized parameter is written into the table every residual reads; a promoted one goes
    /// through <see cref="Freeze"/>, because its column is the solver's and the frozen row is what holds
    /// it (<c>D-140</c>). Idempotent, so a schedule may apply it at every evaluation time.
    /// </remarks>
    public bool Schedule(string component, string parameter, double value)
    {
        for (var element = 0; element < _graph.Components.Length; element++)
        {
            if (!string.Equals(_graph.Components[element].Name, component, StringComparison.Ordinal))
            {
                continue;
            }

            var resolvable = _graph.Components[element].Resolvable;

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                if (!string.Equals(resolvable[slot].Name, parameter, StringComparison.Ordinal))
                {
                    continue;
                }

                for (var index = 0; index < _promoted.Length; index++)
                {
                    if (_promoted[index].Element == element && _promoted[index].Slot == slot)
                    {
                        Freeze(index, value);
                        return true;
                    }
                }

                _parameters[element][slot] = value;
                return true;
            }

            return false;
        }

        return false;
    }

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
        var unknownScales = FluidScript.Core.Solvers.Equations.UnknownScales.Build(unknowns, seed);
        var residualScales = FluidScript.Core.Solvers.Equations.ResidualScales.Build(
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

        // D-70: a bare connection spans two heights like a pipe does, so its row carries the rise.
        var links = posedness.Counting.IdealLinks
            .Select(link => (
                byComponent[link.From.Component],
                byComponent[link.To.Component],
                Height(link.To.Component) - Height(link.From.Component)))
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
                boundary.Component.Boundary is BoundaryRole.Outlet ? -(given ?? 0) : given ?? 0,
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

        var promoted = new List<(int Element, int Slot, int Column, string? Holds)>(posedness.Counting.Promotions.Length);
        var promotedRow = new List<int>(posedness.Counting.Promotions.Length);

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

            // A head promoted to hold a switched-off coil's branch at zero flow is the shut check valve
            // the plant does not have, and a check valve's drop has no lower bound (`S-56`).
            var holds = promotion.Constraint.Kind is ConstraintKind.FixedFlow
                && string.Equals(promotion.Parameter, "head", StringComparison.Ordinal)
                && graph.Components.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, promotion.Constraint.Component, StringComparison.Ordinal))
                    is { } pinned
                && WellPosedness.ZeroDuty(pinned)
                    ? pinned.Name
                    : null;

            var resolvable = graph.Components[element].Resolvable;

            for (var slot = 0; slot < resolvable.Length; slot++)
            {
                if (string.Equals(resolvable[slot].Name, promotion.Parameter, StringComparison.Ordinal))
                {
                    promoted.Add((element, slot, unknowns.PromotionOffset + index, holds));

                    var constraintIndex = posedness.Counting.Constraints.IndexOf(promotion.Constraint);

                    promotedRow.Add(constraintIndex < 0 ? -1 : equations.ConstraintOffset + constraintIndex);

                    break;
                }
            }
        }

        var constraints = Constraints(graph, posedness, equations, ports, byComponent, unknowns, seed);

        return new EquationSystem(
            graph, unknowns, equations, ports, unknownScales, residualScales,
            nodeOf, energyRow, arriving, [.. stated], [.. datums], links, owned, [.. fluxes],
            parameters, [.. promoted], constraints, Stagnant(graph, ports, byComponent, constraints), [.. promotedRow]);
    }

    /// <summary>Resolves each stated constraint to the state its residual reads.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="posedness">The counting table, whose constraint order the rows follow.</param>
    /// <param name="equations">The row layout, for the offset the constraint block starts at.</param>
    /// <param name="ports">Which node and branch each component port attaches to.</param>
    /// <param name="byComponent">Each node component's index among the graph's nodes.</param>
    /// <param name="unknowns">The unknown layout, for a volume-flow row's node columns.</param>
    /// <param name="seed">The starting iterate, whose density at the node scales a volume-flow row.</param>
    /// <returns>One entry per constraint, in row order.</returns>
    /// <remarks>
    /// A fixed-flow outlet with known duty and inlet is evaluated in its derived flow form (<c>D-92</c>).
    /// This is algebraically equivalent to the outlet-temperature form away from zero, but it does not
    /// admit the artificial near-zero-flow root created when duty upwinding blends a finite duty across
    /// both ports. The target is the duty ratio from the three stated constants —
    /// <see cref="FluidScript.Core.Sizing.Flows.BranchFlows.RatedFlow"/>, the same arithmetic the seed uses, and not the seed's
    /// estimate for the branch (<c>S-57</c>). The flow residual is multiplied by ΔT/ṁ so the existing
    /// kelvin scale and diagnostics remain valid. Other absolute and difference constraints continue to
    /// read node temperatures directly.
    /// </remarks>
    private static Constraint[] Constraints(
        CircuitGraph graph,
        WellPosednessResult posedness,
        EquationLayout equations,
        PortMap ports,
        Dictionary<object, int> byComponent,
        SystemLayout unknowns,
        StateVector seed)
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

            // A stated flow pins its branch outright (P5.13b, `S-72`): `flow` at the number, `vflow` at
            // the number times the density of the side's inlet node *as solved* -- `ṁ − ρ(p, h)·V̇ = 0`,
            // the identity V̇ = ṁ/ρ written where it holds rather than converted once at bind time with
            // a density the solve then contradicts (water is 2.5 % lighter at 80 °C than at 20 °C). The
            // row keeps the constraint block's kelvin scale: a 100 % flow error reads as the
            // temperature scale, so convergence on the row is convergence on the flow.
            if (constraint.Kind is ConstraintKind.FixedFlow
                && WellPosedness.IsStatedFlow(constraint.Parameter)
                && graph.Components[element] is HeatExchanger or Pump)
            {
                var side = ports[element, constraint.Parameter.EndsWith('2') ? 2 : 0];

                if (!side.CarriesFlow || stated <= 0)
                {
                    continue;
                }

                if (constraint.Parameter.StartsWith("vflow", StringComparison.Ordinal))
                {
                    var density = side.Node >= 0 ? SeedDensity(graph, unknowns, seed, side.Node) : double.NaN;

                    if (side.Node < 0 || !double.IsFinite(density) || density <= 0)
                    {
                        continue;
                    }

                    resolved[index] = new Constraint(
                        row, -1, -1, 0, side.Sign, side.Branch, 0, Tolerances.TemperatureScale / (density * stated), side.Node, stated);
                    continue;
                }

                resolved[index] = new Constraint(
                    row, -1, -1, 0, side.Sign, side.Branch, stated, Tolerances.TemperatureScale / stated);
                continue;
            }

            // A switched-off coil's flow pin is a pin at zero: `power=0` with its terminals stated is
            // m = 0/(h_out - h_in), and the temperature form it used to fall back to -- the outlet node
            // *at* 30 °C -- is a statement about a node nothing flows through, which the header may or
            // may not happen to satisfy (`S-56`). The residual keeps the row's kelvin scale through the
            // nominal seed flow: 0.1 kg/s of leakage reads as the coil's whole design span.
            if (constraint.Kind is ConstraintKind.FixedFlow
                && !WellPosedness.IsStatedFlow(constraint.Parameter)
                && graph.Components[element] is HeatExchanger stopped
                && WellPosedness.ZeroDuty(stopped))
            {
                var side = ports[element, constraint.Parameter.EndsWith('2') ? 2 : 0];
                var span = SideSpan(stopped, constraint.Parameter);

                if (side.CarriesFlow && span > 0)
                {
                    resolved[index] = new Constraint(
                        row, -1, -1, 0, side.Sign, side.Branch, 0, span / FluidScript.Core.Sizing.Flows.BranchFlows.Nominal);
                    continue;
                }
            }

            // Either side: `out` with `in` pins side 1's branch through port 0, `out2` with `in2` pins side
            // 2's through port 2 (`D-97`).
            if (constraint.Kind is ConstraintKind.FixedFlow
                && constraint.Parameter is "out" or "out2"
                && graph.Components[element] is HeatExchanger exchanger
                && exchanger.StatedParameters.TryGetValue(constraint.Parameter is "out" ? "in" : "in2", out var inlet)
                && exchanger.StatedParameters.TryGetValue(constraint.Parameter, out var outlet))
            {
                var binding = ports[element, constraint.Parameter is "out" ? 0 : 2];
                var rated = binding.CarriesFlow
                    ? FluidScript.Core.Sizing.Flows.BranchFlows.RatedFlow(graph.Substance, exchanger.Power, inlet, outlet)
                    : null;
                var temperatureSpan = Math.Abs(outlet.SiValue - inlet.SiValue);

                if (rated is { } magnitude && magnitude > Tolerances.FlowZero && temperatureSpan > 0)
                {
                    resolved[index] = new Constraint(
                        row,
                        -1,
                        -1,
                        0,
                        binding.Sign,
                        binding.Branch,
                        magnitude,
                        temperatureSpan / magnitude);
                    continue;
                }
            }

            if (graph.Components[element] is CircuitNode node)
            {
                resolved[index] = new Constraint(row, byComponent[node], -1, target, 1);
                continue;
            }

            var difference = constraint.Parameter is "dt" or "dt2";
            var suffix = constraint.Parameter.EndsWith('2') ? "2" : string.Empty;
            var outletNode = Attached(graph, ports, element, "out" + suffix);

            if (!difference)
            {
                resolved[index] = new Constraint(
                    row, Attached(graph, ports, element, constraint.Parameter), -1, target, 1);
                continue;
            }

            // The kind's sign, not the script's: `load power=20 dt=20` is stated positive and carried
            // negative (`ComponentFactory`), and reading the statement made the row demand that the load
            // heat its stream by 20 K. Only `power=-150` on a bare `heat_exchanger` had ever met this row,
            // which is why it held (`S-73`).
            var duty = (graph.Components[element] as HeatExchanger)?.Power ?? 0;

            resolved[index] = new Constraint(
                row,
                outletNode,
                Attached(graph, ports, element, "in" + suffix),
                target,
                duty < 0 ? -1 : 1);
        }

        return resolved;
    }

    /// <summary>A node's density at the seed, for scaling a volume-flow row.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="unknowns">The unknown layout.</param>
    /// <param name="seed">The starting iterate.</param>
    /// <param name="node">The node.</param>
    /// <returns>kg/m³, or NaN when the seed state cannot be read.</returns>
    private static double SeedDensity(CircuitGraph graph, SystemLayout unknowns, StateVector seed, int node)
    {
        var pressure = unknowns.NodePressure(node);
        var enthalpy = unknowns.NodeEnthalpy(node);

        if (pressure >= seed.Values.Length || enthalpy >= seed.Values.Length)
        {
            return double.NaN;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            FluidScript.Core.Physics.Units.Quantity.FromSi(seed.Values[pressure], FluidScript.Core.Physics.Units.Dimension.Pressure),
            FluidScript.Core.Physics.Units.Quantity.FromSi(seed.Values[enthalpy], FluidScript.Core.Physics.Units.Dimension.Enthalpy));

        return state.IsSuccess ? state.Value.Density.SiValue : double.NaN;
    }

    /// <summary>The temperature span a flow pin was written with: <c>out</c> less <c>in</c>, or <c>dt</c> itself.</summary>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="parameter">The pinning parameter: <c>out</c>, <c>out2</c>, <c>dt</c> or <c>dt2</c>.</param>
    /// <returns>K, a magnitude; zero when the pair is not both stated.</returns>
    private static double SideSpan(HeatExchanger exchanger, string parameter)
    {
        if (parameter is "dt" or "dt2")
        {
            return Math.Abs(HydraulicPartition.Stated(exchanger, parameter) ?? 0);
        }

        var suffix = parameter.EndsWith('2') ? "2" : string.Empty;

        return HydraulicPartition.Stated(exchanger, "in" + suffix) is { } inlet
            && HydraulicPartition.Stated(exchanger, "out" + suffix) is { } outlet
                ? Math.Abs(outlet - inlet)
                : 0;
    }

    /// <summary>The nodes inside every branch pinned at zero flow, each with the node it takes its temperature from.</summary>
    /// <param name="graph">The lowered graph.</param>
    /// <param name="ports">Which node each component port attaches to.</param>
    /// <param name="byComponent">Each node component's index among the nodes.</param>
    /// <param name="constraints">The resolved constraints; the flow pins at zero name the branches.</param>
    /// <returns>Node and anchor pairs, both indices among the graph's nodes.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Stagnant water has no steady-state temperature</strong> (<c>S-56</c>). A node's energy
    /// balance is Σ ṁ·h over its ports, and on a branch held at exactly zero flow every term is zero
    /// whatever the enthalpy: the row is satisfied by anything, its column is empty, and the Jacobian
    /// is singular by one for each node inside the stopped branch -- which is what the rad-off header
    /// reported, naming the pump head and the coil's outlet enthalpy as the pair nothing separates.
    /// </para>
    /// <para>
    /// The rule that replaces those rows: <em>the water in a stopped branch sits at the temperature of
    /// the header node it hangs from</em> -- the branch's <c>To</c> end when that is a node, else its
    /// <c>From</c> end. It is a modelling choice and a mild one: in a plant the stopped coil cools to the
    /// room, and nothing steady says what it holds. What the rule buys is a row with a slope, and a
    /// reported temperature on the stopped branch that is the header's rather than an artefact of the
    /// seed. A branch whose ends are both junction elements has no anchor and keeps its rows; the
    /// singularity is then reported as before.
    /// </para>
    /// </remarks>
    private static (int Node, int Anchor)[] Stagnant(
        CircuitGraph graph,
        PortMap ports,
        Dictionary<object, int> byComponent,
        Constraint[] constraints)
    {
        var stagnant = new List<(int Node, int Anchor)>();
        var index = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);

        for (var element = 0; element < graph.Components.Length; element++)
        {
            index[graph.Components[element]] = element;
        }

        foreach (var constraint in constraints)
        {
            // A pin at zero, and only that: a volume-flow row carries its target on the volume side and
            // a mass target of zero, which is not a stopped branch.
            if (constraint.FlowBranch < 0 || constraint.VolumeNode >= 0 || constraint.FlowTarget != 0)
            {
                continue;
            }

            var branch = graph.Branches[constraint.FlowBranch];
            var ends = new[] { branch.To.Element, branch.From.Element }
                .Where(end => end is CircuitNode)
                .Select(end => byComponent[end])
                .ToArray();

            if (ends.Length == 0)
            {
                continue;
            }

            var anchor = ends[0];
            var inside = new SortedSet<int>();

            foreach (var component in branch.Path)
            {
                var element = index[component];

                for (var port = 0; port < component.Ports.Length; port++)
                {
                    var binding = ports[element, port];

                    if (binding.Branch == branch.Index && binding.Node >= 0 && !ends.Contains(binding.Node))
                    {
                        inside.Add(binding.Node);
                    }
                }
            }

            foreach (var node in inside)
            {
                stagnant.Add((node, anchor));
            }
        }

        // A dead leg is the same closure with no constraint behind it (S-23): a branch ending at a
        // terminal node the script gave no role has its flow forced to zero by that node's own mass
        // balance, and then the node's energy balance is ṁ·h with ṁ = 0 -- a zero row on a zero
        // column, singular for a reason that has nothing to do with the circuit. The dead-end node and
        // everything inside its branch take the temperature of the node at the live end. A leg whose
        // live end is a junction element rather than a node has no anchor and keeps its rows.
        foreach (var branch in graph.Branches)
        {
            var deadEnd = DeadEnd(branch.To) ? branch.To : DeadEnd(branch.From) ? branch.From : null;

            if (deadEnd is null)
            {
                continue;
            }

            var live = ReferenceEquals(deadEnd, branch.To) ? branch.From : branch.To;

            if (live.Element is not CircuitNode || DeadEnd(live))
            {
                continue;
            }

            var anchor = byComponent[live.Element];
            var claimed = stagnant.Select(static pair => pair.Node).ToHashSet();
            var nodes = new SortedSet<int> { byComponent[deadEnd.Element] };

            foreach (var component in branch.Path)
            {
                var element = index[component];

                for (var port = 0; port < component.Ports.Length; port++)
                {
                    var binding = ports[element, port];

                    if (binding.Branch == branch.Index && binding.Node >= 0 && binding.Node != anchor)
                    {
                        nodes.Add(binding.Node);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (!claimed.Contains(node))
                {
                    stagnant.Add((node, anchor));
                }
            }
        }

        return [.. stagnant];

        // A terminal node with nothing stated: one connection, no boundary role. A supply or a return
        // with one connection is a boundary whose flux is an unknown, not a dead end (D-64).
        static bool DeadEnd(BranchEnd end) =>
            end.Element is CircuitNode { Boundary: BoundaryRole.Interior, Ports.Length: 1 };
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

    /// <summary>The energy each port of one component delivers into the node it touches, at one iterate, in watts (<c>C-103</c>).</summary>
    /// <param name="x">The iterate, <see cref="Columns"/> long.</param>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="injection">Destination, one entry per port in the component's port order, positive into the node.</param>
    /// <returns><see langword="false"/> when a node's state left the property domain, naming it in <see cref="OutOfDomainNode"/>.</returns>
    /// <remarks>
    /// What <see cref="Assemble"/> computes on its way to the node balances, exposed so that a solved
    /// outlet port can be given the component's own outlet state -- its inlet's enthalpy plus this
    /// injection over the flow -- rather than the state of the node it discharges into, which is the
    /// mixed state where two streams meet. Promoted parameters are read from the iterate first, as
    /// the assembly does.
    /// </remarks>
    /// <exception cref="ArgumentException">A span is the wrong length.</exception>
    public bool TryEvaluateInjection(ReadOnlySpan<double> x, int element, Span<double> injection)
    {
        if (x.Length != Columns)
        {
            throw new ArgumentException($"Expected {Columns} unknowns, got {x.Length}.", nameof(x));
        }

        var component = _graph.Components[element];

        if (injection.Length != component.Ports.Length)
        {
            throw new ArgumentException($"Expected {component.Ports.Length} ports, got {injection.Length}.", nameof(injection));
        }

        OutOfDomainNode = -1;

        for (var node = 0; node < _graph.Nodes.Length; node++)
        {
            if (!Refresh(node, x))
            {
                return false;
            }
        }

        foreach (var (owner, slot, column, _) in _promoted)
        {
            _parameters[owner][slot] = x[column];
        }

        injection.Clear();

        if (component.InjectsEnergy)
        {
            var ports = Fill(element, x);
            component.EvaluateEnergyInjection(Context(element, ports, x), injection);
        }

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

    /// <summary>Clamps every promoted column into the range its component declared.</summary>
    /// <param name="x">The iterate, updated in place.</param>
    /// <param name="pinned">The last column a bound actually bit on, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when any bound bit.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The bounds are the component's, not the solver's</strong> (<c>D-30</c>). A
    /// <c>position</c> is a fraction and a <c>kv</c> is positive; nothing in Newton knows that, and
    /// <c>_promoted</c> already carries the element and the slot, so the range comes off
    /// <see cref="Components.Declarations.ResolvedParameter"/> with no second table to keep in step.
    /// </para>
    /// <para>
    /// <strong>This is only safe because the residual is differentiable at a bound</strong>
    /// (<c>S-26a</c>). While <c>ValveLaw.Opening</c> clamped, projecting an iterate <em>onto</em> a
    /// bound put it exactly where the Jacobian column was identically zero — the fix and the defect
    /// would have been the same operation. The clamp is gone, so a pinned column still has a slope and
    /// the next step can leave the bound if the residual wants it to.
    /// </para>
    /// </remarks>
    public bool Project(Span<double> x, out int pinned)
    {
        pinned = -1;

        foreach (var (element, slot, column, holds) in _promoted)
        {
            var parameter = _graph.Components[element].Resolvable[slot];

            if (holds is null && parameter.Minimum is { } low && x[column] < low)
            {
                x[column] = low;
                pinned = column;
            }
            else if (parameter.Maximum is { } high && x[column] > high)
            {
                x[column] = high;
                pinned = column;
            }
        }

        return pinned >= 0;
    }

    /// <summary>The promoted columns that hold a switched-off branch shut, with the component each holds (<c>S-56</c>).</summary>
    /// <value>Column and the stopped exchanger's name. Such a column is exempt from its parameter's lower bound: a negative head is the check valve's drop.</value>
    public IEnumerable<(int Column, string Holds)> Closing
    {
        get
        {
            foreach (var (_, _, column, holds) in _promoted)
            {
                if (holds is not null)
                {
                    yield return (column, holds);
                }
            }
        }
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
        foreach (var (element, slot, column, _) in _promoted)
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

        // The integrator's right-hand side is exactly the balance the pin is about to overwrite:
        // `Σ ṁ_in (h_in − h) + Q̇` with the same upwind blend, injection and boundary stream the
        // algebraic rows use, so the derivative and the constraints are one evaluation (33). The two
        // totals feed the drift accumulator, which must be built from the injections and the boundary
        // streams and never from the balances it checks.
        if (Pinned)
        {
            _injected = 0;
            _boundary = 0;

            for (var node = 0; node < _graph.Nodes.Length; node++)
            {
                _injected += _nodeInjection[node];

                if (!double.IsNaN(_nodePin[node]))
                {
                    _balance[node] = _energyRow[node] >= 0 ? residuals[_energyRow[node]] : 0;
                }
            }

            foreach (var (node, _, column, magnitude, boundary, known) in _fluxes)
            {
                var flux = column >= 0 ? x[column] : magnitude;
                var own = x[Unknowns.NodeEnthalpy(node)];

                _boundary += flux * Smoothing.Upwind(flux, known ? boundary : own, own);
            }
        }

        // A node inside a branch held at zero flow takes the temperature of the node its branch hangs
        // from; its own balance is 0 = 0 there (`S-56`). Written in watts through the nominal flow so
        // the row keeps the scale its balance had.
        foreach (var (node, anchor) in _stagnant)
        {
            if (_energyRow[node] >= 0)
            {
                residuals[_energyRow[node]] = FluidScript.Core.Sizing.Flows.BranchFlows.Nominal
                    * (x[Unknowns.NodeEnthalpy(node)] - x[Unknowns.NodeEnthalpy(anchor)]);
            }
        }

        if (Pinned)
        {
            // A differential state's balance is the integrator's; here it is the identity on the pin,
            // in watts through the nominal flow like a stagnant node's (D-139).
            for (var node = 0; node < _nodePin.Length; node++)
            {
                if (!double.IsNaN(_nodePin[node]) && _energyRow[node] >= 0)
                {
                    residuals[_energyRow[node]] = FluidScript.Core.Sizing.Flows.BranchFlows.Nominal
                        * (x[Unknowns.NodeEnthalpy(node)] - _nodePin[node]);
                }
            }

            // A tank's own mixed enthalpy, pinned to its layers' mean: its energy row is local 0.
            for (var element = 0; element < _ownPin.Length; element++)
            {
                var row = double.IsNaN(_ownPin[element]) ? -1 : Equations.Row(element, 0);

                if (row >= 0)
                {
                    var column = Unknowns.ComponentUnknownOffset + _owned[element].Offset + Tank.EnthalpyIndex;

                    residuals[row] = FluidScript.Core.Sizing.Flows.BranchFlows.Nominal * (x[column] - _ownPin[element]);
                }
            }
        }

            // A fixed-flow statement is the derived form of power + inlet + outlet. Other constraints
            // retain their direct absolute- or difference-temperature residual.
            foreach (var constraint in _constraints)
            {
                if (constraint.FlowBranch >= 0)
                {
                    // A volume flow's mass target is the stated volume at the inlet node's density as it
                    // stands this iterate; a mass flow's is the number itself.
                    var target = constraint.VolumeNode >= 0
                        ? _nodeStates[constraint.VolumeNode].Density * constraint.VolumeTarget
                        : constraint.FlowTarget;

                    residuals[constraint.Row] =
                        ((constraint.Sign * x[Unknowns.BranchFlow(constraint.FlowBranch)]) - target)
                        * constraint.FlowScale;
                    continue;
                }

                if (constraint.Node < 0)
                {
                    continue;
                }

                residuals[constraint.Row] = constraint.Reference < 0
                    ? _nodeStates[constraint.Node].Temperature - constraint.Target
                    : (constraint.Sign
                        * (_nodeStates[constraint.Node].Temperature
                            - _nodeStates[constraint.Reference].Temperature))
                        - constraint.Target;
            }

        if (Pinned)
        {
            // A frozen promotion replaces the row of the constraint that promoted it (D-140): the
            // actuator holds, and the temperature the constraint asked for follows the flow. Written on
            // the parameter's own scale, carried to the row's, so the scaled residual is the relative
            // miss on the parameter and not a position read in kelvin.
            for (var index = 0; index < _frozen.Length; index++)
            {
                var row = double.IsNaN(_frozen[index]) ? -1 : _promotedRow[index];

                if (row >= 0)
                {
                    var column = _promoted[index].Column;

                    residuals[row] = (x[column] - _frozen[index]) / UnknownScales[column] * ResidualScales[row];
                }
            }
        }

        var assembly = Equations.LinkOffset;

        // D-25's zero-drop connection: two nodes with nothing between them are one pressure, and no
        // component is there to say so. Nothing between them but height, that is: a link that climbs
        // carries ρgΔz like a frictionless pipe would (D-70), at the mean density of its two ends.
        foreach (var (from, to, rise) in _links)
        {
            var density = (_nodeStates[from].Density + _nodeStates[to].Density) / 2;

            residuals[assembly++] = x[Unknowns.NodePressure(from)] - x[Unknowns.NodePressure(to)]
                - Hydrostatic.Pressure(density, rise);
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
    /// of the nodes at its other ports, which is what a mixing tee physically does:
    /// <c>Σ ṁᵢ hᵢ / Σ ṁᵢ</c> over the ports that flow in.
    /// </para>
    /// <para>
    /// <strong>The weight is the inflow itself, smoothed to zero across a reversal</strong> —
    /// <c>ṁ · ForwardShare(ṁ)</c>, which is <c>max(0, ṁ)</c> away from zero and C¹ through it as
    /// <c>36</c> requires. It was <see cref="Smoothing.ForwardShare"/> alone until <c>S-58</c>, and
    /// that is a 0-to-1 step that reads 1 for every inflow above one gram per second: a mixing valve
    /// passing 0.167 kg/s of 80 °C water and 0.063 kg/s of 30 °C water then delivered <em>55 °C</em>,
    /// the plain average, where the mass-weighted mix is 66 °C. Its position moved the split and the
    /// split moved nothing, so every constraint on a mixed temperature drove the valve to a stop. A
    /// small floor keeps the quotient defined when every other port is an outflow — a state the
    /// junction's own mass balance forbids at the solution but not on the path to it.
    /// </para>
    /// </remarks>
    private double Arriving(int element, int port, ReadOnlySpan<double> x, int node)
    {
        // Pinned view: a port of a tank delivers its layer, whatever flows into the tank elsewhere.
        var (attachedComponent, attachedPort) = _attached[element][port];

        if (attachedComponent >= 0 && !double.IsNaN(_portPin[attachedComponent][attachedPort]))
        {
            return _portPin[attachedComponent][attachedPort];
        }

        var sources = _arriving[element][port];

        if (sources.Length == 0)
        {
            return x[Unknowns.NodeEnthalpy(node)];
        }

        if (sources.Length == 1)
        {
            return x[Unknowns.NodeEnthalpy(sources[0].Node)] - sources[0].Lift;
        }

        var numerator = MixingFloor * x[Unknowns.NodeEnthalpy(node)];
        var denominator = MixingFloor;

        foreach (var source in sources)
        {
            var binding = _ports[source.Component, source.Port];
            var inflow = binding.CarriesFlow
                ? binding.Sign * x[Unknowns.BranchFlow(binding.Branch)]
                : 0;

            var weight = inflow * Smoothing.ForwardShare(inflow);

            numerator += weight * (x[Unknowns.NodeEnthalpy(source.Node)] - source.Lift);
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
            // A node wired straight to a node: the ideal link, which carries gravity's share of the
            // enthalpy itself because there is no component between them to inject it (D-70).
            var lift = graph.Components[element] is CircuitNode here && attached is CircuitNode there
                ? Hydrostatic.Lift(here.Elevation - there.Elevation)
                : 0;

            return [new ArrivingSource(direct, peer.Component, peer.Port, lift)];
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

    /// <summary>A node's height above the project datum, for the links between nodes.</summary>
    /// <param name="component">The node's component.</param>
    /// <returns>m; 0 for anything that is not a <see cref="CircuitNode"/>.</returns>
    private static double Height(IFlowComponent component) =>
        component is CircuitNode node ? node.Elevation : 0;
}
