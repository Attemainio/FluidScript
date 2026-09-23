using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Passes;

public static partial class DeferredEvaluation
{
    /// <summary>The names an expression may read, resolved against one solved pass.</summary>
    private sealed class SolvedScope : IValueScope
    {
        private readonly SemanticModel _model;
        private readonly CircuitGraph _graph;
        private readonly SystemLayout _layout;
        private readonly StateVector _solution;
        private readonly Dictionary<ValueId, Quantity> _supplied = new();
        private readonly HashSet<ValueId> _unsupplied = [];
        private readonly bool _seeding;
        private ImmutableArray<ImmutableArray<SolvedPort?>>? _ports;

        public SolvedScope(SemanticModel model, CircuitGraph graph, SystemLayout layout, StateVector solution, bool seeding)
        {
            _model = model;
            _graph = graph;
            _layout = layout;
            _solution = solution;
            _seeding = seeding;
        }

        /// <summary>The values this pass could not supply, for the note.</summary>
        public HashSet<ValueId> Unsupplied => _unsupplied;

        /// <summary>Makes an evaluated target readable by the expressions after it.</summary>
        public void Supply(ValueId target, Quantity value) => _supplied[target] = value;

        public ScopeLookup Lookup(ReferenceSyntax reference)
        {
            ArgumentNullException.ThrowIfNull(reference);

            var head = reference.Head.Token.Text;

            if (reference.Parts.IsDefaultOrEmpty)
            {
                var let = new ValueId.Let(head);

                if (_supplied.TryGetValue(let, out var supplied))
                {
                    return new ScopeLookup.Value(supplied, IsBare: false, let);
                }

                var binding = _model.Bindings.FirstOrDefault(candidate => string.Equals(candidate.Name, head, StringComparison.Ordinal));

                if (binding is null)
                {
                    return new ScopeLookup.UnknownName(null);
                }

                if (binding.Value is { } value)
                {
                    return new ScopeLookup.Value(value, IsBare: false, let);
                }

                _unsupplied.Add(let);
                return new ScopeLookup.Deferred(let);
            }

            var symbol = _model.Components.FirstOrDefault(candidate => string.Equals(candidate.Name, head, StringComparison.Ordinal));

            if (symbol?.Kind is not { } kind)
            {
                return new ScopeLookup.UnknownName(null);
            }

            var written = reference.PropertyPath();

            if (kind.ResolveProperty(written) is not { } property)
            {
                return new ScopeLookup.UnknownProperty(kind.Keyword, [.. kind.ReadableNames]);
            }

            var id = new ValueId.ComponentProperty(head, property.Key);

            if (_supplied.TryGetValue(id, out var known))
            {
                return new ScopeLookup.Value(known, IsBare: false, id);
            }

            if (Read(head, kind, property) is { } read)
            {
                return new ScopeLookup.Value(Quantity.FromSi(read, property.Dimension), IsBare: false, id);
            }

            _unsupplied.Add(id);
            return new ScopeLookup.Deferred(id);
        }

        /// <summary>One property of one element, SI, from what the pass published.</summary>
        private double? Read(string name, ComponentKindInfo kind, PropertyInfo property)
        {
            var index = -1;

            for (var i = 0; i < _graph.Components.Length; i++)
            {
                if (string.Equals(_graph.Components[i].Name, name, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }

            var element = _graph.Components[index];
            var key = property.Key;

            // What the script stated is what a reference reads, whatever the solve did with it.
            if (element.StatedParameters.TryGetValue(key, out var stated))
            {
                return stated.SiValue;
            }

            // The seed is a guess at everything the script did not state: a node it knows nothing
            // about sits at the bootstrap's placeholder temperature. Written in as a stated value, that
            // guess became the truth the first solve was held to -- a 38 °C primary on a 150 kW
            // exchanger, which no solve survives. The seed supplies only what the script anchored,
            // which includes a kind's decided default (`D-32`): an exchanger's 20 kPa design drop is
            // the script's number whether or not the line spells it.
            if (_seeding)
            {
                return element.DefaultParameters.TryGetValue(key, out var decided) ? decided.SiValue : null;
            }

            if (SolvedStates.Parameter(_layout, _solution, name, key) is { } promoted)
            {
                return promoted;
            }

            _ports ??= SolvedStates.Ports(_graph, _layout, _solution);
            var ports = _ports.Value[index];

            if (kind.HasUnlimitedPorts)
            {
                return NodeProperty(element, ports, key);
            }

            // A rated exchanger's second side is not wired, so its leaving temperature, its duty and
            // its rating figures exist only through the rating -- the same arithmetic the report prints.
            if (element is HeatExchanger exchanger && ExchangerProperty(exchanger, index, key) is { } rated)
            {
                return rated;
            }

            if (PortProperty(element, ports, key) is { } fromPorts)
            {
                return fromPorts;
            }

            if (element.SizedParameters.TryGetValue(key, out var sized))
            {
                return sized.SiValue;
            }

            return element.DefaultParameters.TryGetValue(key, out var fallback) ? fallback.SiValue : null;
        }

        private double? ExchangerProperty(HeatExchanger exchanger, int index, string key)
        {
            if (key is not ("t_in2" or "t_out2" or "dt2" or "flow2" or "ua" or "ntu" or "effectiveness" or "lmtd" or "approach" or "power")
                || (exchanger.SecondarySideConnected && key is "t_in2" or "t_out2" or "dt2" or "flow2")
                || SolvedStates.Exchanger(_graph, _layout, _solution, index) is not { } at)
            {
                return null;
            }

            return key switch
            {
                "t_in2" => at.Inlet2,
                "t_out2" => at.Outlet2,
                "dt2" => at.Outlet2 - at.Inlet2,
                "flow2" => exchanger.SecondaryFlow > 0 ? exchanger.SecondaryFlow : null,
                "ua" => at.Rating.Conductance,
                "ntu" => at.Ntu,
                "effectiveness" => at.Effectiveness,
                "lmtd" => at.Lmtd,
                "approach" => at.Approach,
                "power" => at.Duty,
                _ => null,
            };
        }

        private double? NodeProperty(IComponent element, ImmutableArray<SolvedPort?> ports, string key)
        {
            var state = ports.FirstOrDefault(static port => port is not null);

            if (string.Equals(key, "flow", StringComparison.Ordinal))
            {
                foreach (var unknown in _layout.Unknowns)
                {
                    if (unknown.Kind == UnknownKind.ExternalMassFlux
                        && string.Equals(unknown.OwnerComponentId, element.Name, StringComparison.Ordinal))
                    {
                        return _solution.Values[unknown.Index];
                    }
                }

                return null;
            }

            return state is not { } port
                ? null
                : key switch
                {
                    "t" => port.Temperature,
                    "p" => port.Pressure,
                    "h" => port.Enthalpy,
                    "rho" => port.Density,
                    "cp" => port.SpecificHeat,
                    _ => null,
                };
        }

        /// <summary>A flow component's port-derived properties: side 1 on ports 0 and 1, side 2 on 2 and 3.</summary>
        private static double? PortProperty(IComponent element, ImmutableArray<SolvedPort?> ports, string key)
        {
            var second = key.EndsWith('2');
            var bare = second ? key[..^1] : key;
            var first = second ? 2 : 0;

            if (ports.Length < first + 2 || ports[first] is not { } a || ports[first + 1] is not { } b)
            {
                return null;
            }

            // The inlet is whichever of the pair the flow enters; at zero flow the declared inlet.
            var (inlet, outlet) = a.Flow > 0 || (a.Flow == 0 && b.Flow <= 0) ? (a, b) : (b, a);
            var flow = Math.Abs(inlet.Flow);

            return bare switch
            {
                "flow" => flow,
                "dp" => inlet.Pressure - outlet.Pressure,
                "dt" => outlet.Temperature - inlet.Temperature,
                "dh" => outlet.Enthalpy - inlet.Enthalpy,
                "t_in" => inlet.Temperature,
                "t_out" => outlet.Temperature,
                "p_in" => inlet.Pressure,
                "p_out" => outlet.Pressure,
                "power" when element is HeatExchanger => flow * (outlet.Enthalpy - inlet.Enthalpy),
                _ => null,
            };
        }
    }
}
