using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Diagnostics.Explanations;

public static partial class SolveExplanation
{
    private static void State(
        StringBuilder report,
        CircuitGraph graph,
        SystemLayout layout,
        StateVector seed,
        SolveResult? solve)
    {
        // Every node in °C and kPa and every branch in kg/s against its written direction (`S-71`). The
        // unknowns table is the solver's view -- enthalpies and pascals -- and an engineer cannot argue
        // with an enthalpy.
        report.AppendLine();
        report.AppendLine("--- state, in engineering units");

        var at = solve?.Solution ?? seed;
        var label = solve is null ? "seed" : "solved";

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    nodes ({label})      t °C      p kPa");

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            var pressure = layout.NodePressure(node);
            var enthalpy = layout.NodeEnthalpy(node);

            if (pressure >= at.Values.Length || enthalpy >= at.Values.Length)
            {
                continue;
            }

            var state = graph.Substance.FromPressureEnthalpy(
                FluidScript.Core.Physics.Units.Quantity.FromSi(at.Values[pressure], FluidScript.Core.Physics.Units.Dimension.Pressure),
                FluidScript.Core.Physics.Units.Quantity.FromSi(at.Values[enthalpy], FluidScript.Core.Physics.Units.Dimension.Enthalpy));
            var temperature = state.IsSuccess
                ? (state.Value.Temperature.SiValue - 273.15).ToString("0.00", CultureInfo.InvariantCulture)
                : "(out of range)";

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {graph.Nodes[node].Name,-18} {temperature,9} {at.Values[pressure] / 1000,10:0.00}");
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"    branches ({label})   kg/s      direction, against the script");

        // "Written order" is what the port map knows, not the branch's sign: a ring's path may start
        // at whichever element the walk reached first, and on the two-ring substation both branches
        // were labelled reversed while every pump pushed the way it was written (S-70). The sign of
        // the flow entering the first ported element's `in` is the direction the script would call
        // forward, which is what `OuterLoop.Reversals` reads for FS3013.
        var ports = PortMap.Build(graph);

        foreach (var branch in graph.Branches)
        {
            var column = layout.BranchFlow(branch.Index);

            if (column >= at.Values.Length)
            {
                continue;
            }

            var flow = at.Values[column];
            var direction = Direction(graph, ports, branch, flow);
            var path = branch.Path.Length == 0
                ? "(direct)"
                : string.Join(" - ", branch.Path.Select(static element => element.Name));

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    {Spelled(branch.From)} -> {Spelled(branch.To),-14} {Math.Abs(flow),9:0.0000}  {direction,-9} {path}");
        }
    }

    private static void HeatBalance(
        StringBuilder report,
        CircuitGraph graph,
        WellPosednessResult posedness,
        SystemLayout layout,
        SolveResult? solve)
    {
        // The first line an engineer checks (`S-71`): what goes in, what comes out, per hydraulic. The
        // duties are the lowered ones -- stated, or what the closure chose -- and a boundary stream
        // carries the enthalpy of the node it crosses at, so an open circuit's sum is its net enthalpy
        // flux. `FS2203` checks this and says nothing about the numbers. A duty the solve was asked to find
        // is read from the solution and marked `solved` (`S-84`): summing the lowered one reported a machine
        // whose duty was promoted as a hole of exactly that duty in a circuit balanced to 1e-11 W.
        report.AppendLine();
        report.AppendLine("--- heat balance");

        var at = solve?.Solution;

        foreach (var hydraulic in posedness.Hydraulics)
        {
            var sources = new List<string>();
            var loads = new List<string>();
            var sourceTotal = 0.0;
            var loadTotal = 0.0;

            foreach (var element in hydraulic.Elements)
            {
                if (element is not HeatExchangerComponent exchanger)
                {
                    continue;
                }

                var promoted = at is null ? null : SolvedStates.Parameter(layout, at, exchanger.Name, "power");
                var power = promoted ?? exchanger.Power;

                if (power == 0)
                {
                    continue;
                }

                // `Power` is side 1's gain. A coupled exchanger sits in two hydraulics and gives the one
                // holding its side 2 the same duty with the opposite sign; which side is here is read
                // off the branch that carries it.
                var side = hydraulic.Branches
                    .Where(branch => branch.Path.Contains(exchanger))
                    .Select(branch => BranchFlows.Side(graph, branch, exchanger))
                    .DefaultIfEmpty(1)
                    .First();
                var duty = side == 2 ? -power : power;
                var entry = $"{exchanger.Name} {duty / 1000:+0.###;-0.###}{(promoted is null ? string.Empty : " solved")}";

                if (duty > 0)
                {
                    sources.Add(entry);
                    sourceTotal += duty;
                }
                else
                {
                    loads.Add(entry);
                    loadTotal += duty;
                }
            }

            var crossing = 0.0;
            var streams = new List<string>();

            if (at is not null)
            {
                for (var flux = 0; flux < layout.FluxNodes.Length; flux++)
                {
                    var node = layout.FluxNodes[flux];

                    if (!hydraulic.Nodes.Contains(node))
                    {
                        continue;
                    }

                    var index = graph.Nodes.IndexOf(node);
                    var column = layout.ExternalFluxOffset + flux;

                    if (index < 0 || column >= at.Values.Length)
                    {
                        continue;
                    }

                    var mass = at.Values[column];
                    var energy = mass * at.Values[layout.NodeEnthalpy(index)];
                    crossing += energy;
                    streams.Add($"{node.Name} {mass:+0.####;-0.####} kg/s, {energy / 1000:+0.###;-0.###} kW");
                }
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    [{hydraulic.Index}] sources {sourceTotal / 1000:+0.###;-0.###} kW"
                + $"{(sources.Count > 0 ? $" ({string.Join(", ", sources)})" : string.Empty)}, "
                + $"loads {loadTotal / 1000:+0.###;-0.###} kW"
                + $"{(loads.Count > 0 ? $" ({string.Join(", ", loads)})" : string.Empty)}, "
                + $"boundary streams {crossing / 1000:+0.###;-0.###} kW"
                + $"{(streams.Count > 0 ? $" ({string.Join(", ", streams)})" : string.Empty)}"
                + $" — net {(sourceTotal + loadTotal + crossing) / 1000:+0.###;-0.###} kW");
        }
    }

    private static void OperatingPoints(
        StringBuilder report,
        CircuitGraph graph,
        SystemLayout layout,
        SolveResult? solve)
    {
        if (solve is null)
        {
            return;
        }

        // Where each pump sits on its curve and what each valve is doing (`S-71`): the ratings section
        // covers exchangers, and pumps and valves appeared only as promoted unknowns and sizing lines.
        // A promoted parameter's solved value is read off the solution; a sized or stated one is the
        // component's own.
        var ports = PortMap.Build(graph);
        var values = solve.Solution.Values;
        var written = false;
        ImmutableArray<ImmutableArray<SolvedPort?>>? solvedPorts = null;
        double Resolved(string owner, string name, double own) => SolvedStates.Resolved(layout, solve.Solution, owner, name, own);

        for (var index = 0; index < graph.Components.Length; index++)
        {
            var component = graph.Components[index];

            if (component is PumpComponent pump)
            {
                if (!ports[index, 0].CarriesFlow)
                {
                    continue;
                }

                solvedPorts ??= SolvedStates.Ports(graph, layout, solve.Solution);

                if (SolvedStates.Pump(layout, solve.Solution, pump, solvedPorts.Value[index]) is not { } at)
                {
                    continue;
                }

                var origin = at.Basis switch
                {
                    PumpHeadBasis.Promoted => "(solved for)",
                    PumpHeadBasis.StatedRise => string.Create(CultureInfo.InvariantCulture, $"(rise stated, {pump.StatedRise!.Value / 1000:0.00} kPa at speed {pump.Speed:0.##})"),
                    _ => string.Create(CultureInfo.InvariantCulture, $"(on its curve, shut-off {pump.ShutOffHead:0.00} m)"),
                };

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {pump.Name,-12} pump   {at.Flow,9:0.0000} kg/s  head {at.Head,7:0.00} m  rise {at.Rise / 1000,8:0.00} kPa  {origin}");
            }
            else if (component is ValveComponent valve)
            {
                var inlet = ports[index, 0];
                var outlet = ports[index, 1];

                if (!inlet.CarriesFlow)
                {
                    continue;
                }

                var flow = inlet.Sign * values[layout.BranchFlow(inlet.Branch)];
                var drop = outlet.Node >= 0 && inlet.Node >= 0
                    ? values[layout.NodePressure(inlet.Node)] - values[layout.NodePressure(outlet.Node)]
                    : double.NaN;

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {valve.Name,-12} valve  {flow,9:0.0000} kg/s  Kv {Resolved(valve.Name, "kv", valve.Kv),7:0.##}"
                    + $"  position {Resolved(valve.Name, "position", valve.Position),5:0.###}  drop {drop / 1000,8:0.00} kPa");
            }
            else if (component is ThreeWayValveComponent three)
            {
                var common = ports[index, 0];

                if (!common.CarriesFlow)
                {
                    continue;
                }

                var legs = new List<string>();

                for (var port = 0; port < three.Ports.Length; port++)
                {
                    var binding = ports[index, port];

                    if (!binding.CarriesFlow)
                    {
                        continue;
                    }

                    var into = binding.Sign * values[layout.BranchFlow(binding.Branch)];
                    var pressure = binding.Node >= 0 && common.Node >= 0
                        ? values[layout.NodePressure(binding.Node)] - values[layout.NodePressure(common.Node)]
                        : double.NaN;
                    var drop = port == 0 ? string.Empty : $", {pressure / 1000:0.00} kPa to {three.Ports[0].Name}";

                    var signed = (into < 0 ? "-" : "+") + Math.Abs(into).ToString("0.0000", CultureInfo.InvariantCulture);

                    legs.Add(string.Create(CultureInfo.InvariantCulture, $"{three.Ports[port].Name} {signed}{drop}"));
                }

                Header(report, ref written);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"    {three.Name,-12} 3-way  Kv {Resolved(three.Name, "kv", three.Kv):0.##}"
                    + $"  position {Resolved(three.Name, "position", three.Position):0.###}  "
                    + $"kg/s into it: {string.Join("; ", legs)}");
            }
        }

        static void Header(StringBuilder report, ref bool written)
        {
            if (!written)
            {
                report.AppendLine();
                report.AppendLine("--- operating points");
                written = true;
            }
        }
    }
}
