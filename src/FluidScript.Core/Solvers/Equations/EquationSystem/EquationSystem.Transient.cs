using FluidScript.Core.Components;
using FluidScript.Core.Solvers.Transient;

namespace FluidScript.Core.Solvers.Equations;

public sealed partial class EquationSystem
{
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
}
