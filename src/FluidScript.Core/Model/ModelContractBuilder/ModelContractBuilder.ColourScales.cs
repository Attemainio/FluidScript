using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Model;

public static partial class ModelContractBuilder
{
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
                    // Settled to the legend's precision first, so a value a nanounit past a tick does
                    // not open an empty band and a plant sitting on its datum is degenerate at zero (C-112).
                    (min, max) = ScaleDomain.Settle(low, high, Resolution(property, unit), diverging);
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
                else if (PropertyTable.Find(property) is { Of: { } of } change)
                {
                    // A change across the component (`D-123`): its base quantity at the outlet less the
                    // inlet, or the reverse for a drop. A node has no change and read its state above.
                    var baseName = PropertyTable.Find(of)!.Name;
                    var entering = inlet is { } pIn ? Read(baseName, pIn) : null;
                    var leaving = outlet is { } pOut ? Read(baseName, pOut) : null;

                    at = entering is { } a && leaving is { } b ? (change.Drop ? a - b : b - a) : null;
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
            "specific_heat" => port.SpecificHeat,
            _ => null,
        };

        private static double? InUnit(double? si, UnitSymbol? unit) =>
            si is { } value && double.IsFinite(value) ? (unit is null ? value : Quantity.FromSi(value, unit.Dimension).ValueIn(unit)) : null;

        /// <summary>
        /// The magnitude below which a property's value is zero on a legend, in the scale's unit: the
        /// resolution the solve claims for it (<c>newton.residual_tol</c> times its scale), for the
        /// properties that have a physical zero. A temperature in °C and a reference-relative enthalpy
        /// have none.
        /// </summary>
        private static double Resolution(string property, UnitSymbol? unit) => property switch
        {
            "pressure" => InUnit(Tolerances.NewtonResidual * Tolerances.PressureScale, unit) ?? 0,
            "flow" => InUnit(Tolerances.NewtonResidual * Tolerances.FlowScaleFloor, unit) ?? 0,
            _ => 0,
        };
    }
}
