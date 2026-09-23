using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

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
public sealed partial class EquationSystem
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
            _layerPin[element] = graph.Components[element] is TankComponent tank ? new double[tank.Layers] : [];
            _layerMass[element] = graph.Components[element] is TankComponent vessel ? new double[vessel.Layers] : [];

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
}
