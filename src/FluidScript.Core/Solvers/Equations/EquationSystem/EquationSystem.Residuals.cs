using FluidScript.Core.Components;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Solvers.Equations;

public sealed partial class EquationSystem
{
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
}
