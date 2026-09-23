using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
    /// <summary>Reports every stated boundary state the substance cannot be in.</summary>
    private static void ReportStates(CircuitGraph graph, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var range = graph.Substance.ValidRange;

        foreach (var node in graph.Nodes)
        {
            var temperature = HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature);
            var pressure = HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure);

            if (temperature is null && pressure is null)
            {
                continue;
            }

            // Where a boundary states only one of the two, the other is checked at the middle of its
            // validated span rather than at an edge: the point is to catch a stated value that is out
            // of range, not to report a temperature because no one wrote a pressure beside it.
            var kelvin = temperature ?? ((range.MinimumTemperature + range.MaximumTemperature) / 2);
            var absolute = pressure is { } gauge
                ? gauge + UnitTable.StandardAtmosphere
                : (range.MinimumAbsolutePressure + range.MaximumAbsolutePressure) / 2;

            if (range.Contains(kelvin, absolute))
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.StateOutsideRange,
                span: null,
                new DiagnosticArgument("substance", graph.Substance.Name),
                new DiagnosticArgument("state", Describe(temperature, pressure)))
                with
            { ComponentName = node.Name });
        }
    }

    /// <summary>Reports the highest node of each hydraulic part whose static head takes it below the substance's floor (<c>S-60</c>).</summary>
    /// <remarks>
    /// <para>
    /// Water at the top of a 32 m riser is 313 kPa below the bottom, and a script that states no
    /// pressure has its datum picked at 0 gauge. The seed then puts the top of the building at
    /// −213 kPa, water has no state there, and the solve stopped with <c>FS3007</c> — an impossible
    /// fluid state after 0 steps — which reads as a solver failure when the plant as written simply has
    /// no fill pressure. Now that every node has a height (<c>D-70</c>) the check is arithmetic before
    /// the seed: <c>p_datum − ρg(z − z_datum)</c> against the substance's floor, at the density the
    /// plant is filled at. Since <c>D-121</c> water's floor is its triple-point pressure, so this fires
    /// only where the top would be at or below no pressure at all; a top under partial vacuum solves,
    /// and <see cref="FillPressure.ReportSolved"/> says so afterwards (<c>FS2221</c>).
    /// </para>
    /// <para>
    /// One diagnostic per hydraulic part, on its highest node, because every node above the floor line
    /// fails for the one reason and the fix is one number on the datum. The number suggested is what
    /// practice writes: the static head plus half a bar (<see cref="FillPressure.Margin"/>).
    /// </para>
    /// </remarks>
    private static void ReportStaticHead(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var floor = graph.Substance.ValidRange.MinimumAbsolutePressure;

        foreach (var hydraulic in hydraulics)
        {
            var datum = hydraulic.Nodes.FirstOrDefault(node => node.Name == hydraulic.Datum);

            if (datum?.Component is not CircuitNode anchor)
            {
                continue;
            }

            var gauge = HydraulicPartition.Stated(anchor, HydraulicPartition.Pressure) ?? 0;

            // Density where the plant is filled: at the datum, cold. A property call that fails here
            // is a state FS2215 has already reported, and there is nothing to add.
            if (!graph.Substance.FromPressureTemperature(
                    Quantity.FromSi(Math.Max(gauge, 0), Dimension.Pressure),
                    Quantity.FromSi(FillPressure.FillTemperature, Dimension.Temperature)).TryGetValue(out var filled))
            {
                continue;
            }

            var density = filled.Density.SiValue;
            GraphNode? highest = null;
            var rise = 0.0;

            foreach (var node in hydraulic.Nodes)
            {
                if (node.Component is CircuitNode placed && placed.Elevation - anchor.Elevation > rise)
                {
                    rise = placed.Elevation - anchor.Elevation;
                    highest = node;
                }
            }

            if (highest is null)
            {
                continue;
            }

            var head = Hydrostatic.Pressure(density, rise);
            var absolute = gauge + UnitTable.StandardAtmosphere - head;

            if (absolute >= floor)
            {
                continue;
            }

            // The datum pressure practice would state: static head plus the fill margin, in whole tens
            // of kPa.
            var needed = FillPressure.Suggest(head);

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.StaticHeadBelowFloor,
                span: null,
                new DiagnosticArgument("node", highest.Name),
                new DiagnosticArgument("rise", rise.ToString("0.#", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("datum", hydraulic.Datum),
                new DiagnosticArgument("short", ((floor - absolute) / 1000).ToString("0", CultureInfo.InvariantCulture)),
                new DiagnosticArgument("substance", graph.Substance.Name),
                new DiagnosticArgument("needed", needed.ToString("0", CultureInfo.InvariantCulture)))
                with
            { ComponentName = highest.Name });
        }
    }

    /// <summary>Renders a boundary state the way the script wrote it.</summary>
    private static string Describe(double? temperature, double? gaugePressure)
    {
        var parts = new List<string>(2);

        if (temperature is { } kelvin)
        {
            parts.Add((kelvin - 273.15).ToString("0.###", CultureInfo.InvariantCulture) + " °C");
        }

        if (gaugePressure is { } pascal)
        {
            parts.Add((pascal / 1000).ToString("0.###", CultureInfo.InvariantCulture) + " kPa");
        }

        return string.Join(" and ", parts);
    }

    /// <summary>A hydraulic component's reportable name: the circuit most of its own elements belong to.</summary>
    /// <remarks>
    /// "Its own" excludes an element that also sits in another hydraulic -- a coupled exchanger is in
    /// both the hydraulics it separates, and naming a hydraulic by its first element named the
    /// exchanger's circuit for both sides of a substation (<c>S-70</c>). Ties fall to graph order.
    /// </remarks>
    private static string Name(
        CircuitGraph graph, ImmutableArray<HydraulicComponent> hydraulics, HydraulicComponent hydraulic)
    {
        var fallback = hydraulic.Index.ToString(CultureInfo.InvariantCulture);
        string? best = null;
        var bestCount = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var element in hydraulic.Elements)
        {
            if (!graph.CircuitOf.TryGetValue(element.Name, out var circuit)
                || hydraulics.Any(other => other.Index != hydraulic.Index && other.Elements.Contains(element)))
            {
                continue;
            }

            var count = counts.GetValueOrDefault(circuit) + 1;
            counts[circuit] = count;

            if (count > bestCount)
            {
                (best, bestCount) = (circuit, count);
            }
        }

        return best
            ?? (hydraulic.Elements.Length > 0
                ? graph.CircuitOf.GetValueOrDefault(hydraulic.Elements[0].Name, fallback)
                : fallback);
    }

    /// <summary>Reports fluid that can enter a circuit and not leave it, or the reverse (<c>FS2204</c>).</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="diagnostics">Where to report.</param>
    /// <remarks>
    /// <para>
    /// The mass analogue of <see cref="ReportClosure"/>, and invisible to the count for the same reason:
    /// a stated <c>flow</c> is a known injection, so a circuit that injects mass with nowhere to put it
    /// is square and inconsistent.
    /// </para>
    /// <para>
    /// <strong>A stated pressure is a way in and a way out</strong> as surely as a boundary kind is —
    /// mass crosses there to hold the number — so an <c>inlet</c> paired with a pressure-driven outlet is
    /// not reported. What a pressure cannot do is stand in for the boundary it sits <em>on</em>: a
    /// <c>inlet p=300</c> is one node, and a circuit whose only flux is at that node passes none.
    /// </para>
    /// <para>
    /// <strong>A circuit with neither boundary kind is never reported here.</strong> A closed loop needs
    /// neither (<c>D-64</c>), and the cooling loop's two stated pressures are a complete pair of
    /// boundary conditions written the older way.
    /// </para>
    /// </remarks>
    private static void ReportBoundaries(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var hydraulic in hydraulics)
        {
            // D-115: a boundary is a terminal with one connection; the flow splits or merges at a node after it (FS2205).
            foreach (var node in hydraulic.Nodes)
            {
                if (node.Component.Boundary is not BoundaryRole.Interior && node.Component.Ports.Length > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        TopologyDiagnostics.BoundaryFanOut,
                        span: null,
                        new DiagnosticArgument("node", node.Component.Name),
                        new DiagnosticArgument("kind", node.Component.Boundary is BoundaryRole.Inlet ? "inlet" : "outlet"),
                        new DiagnosticArgument("count", node.Component.Ports.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        with { ComponentName = node.Component.Name });
                }
            }

            var supplied = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Inlet);
            var returned = hydraulic.Boundaries.Any(
                static node => node.Component.Boundary is BoundaryRole.Outlet);

            var exits = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Outlet
                || (node.Component.Boundary is not BoundaryRole.Inlet
                    && HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null));

            var entries = hydraulic.Nodes.Any(static node =>
                node.Component.Boundary is BoundaryRole.Inlet
                || (node.Component.Boundary is not BoundaryRole.Outlet
                    && (HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure) is not null
                        || HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is not null)));

            string present;
            string missing;

            if (supplied && !exits)
            {
                (present, missing) = ("inlet", "outlet");
            }
            else if (returned && !entries)
            {
                (present, missing) = ("outlet", "inlet");
            }
            else
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                TopologyDiagnostics.UnpairedBoundary,
                span: null,
                new DiagnosticArgument("circuit", Name(graph, hydraulics, hydraulic)),
                new DiagnosticArgument("present", present),
                new DiagnosticArgument("missing", missing)));
        }
    }

    /// <summary>A stated node pressure as the script wrote it: <c>N2.p</c>, or <c>PU1 out.p</c> when a port stated it (<c>D-124</c>).</summary>
    private static string PressureLabel(GraphNode node) =>
        node.Component.PressureStatedAs ?? $"{node.Name}.p";
}
