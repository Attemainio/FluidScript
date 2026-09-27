using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Topology.Hydraulics;

/// <summary>Which fluid a file is solved with, and what is said about a circuit that states none (<c>D-181</c>).</summary>
/// <remarks>
/// <para>
/// A circuit that writes no <c>fluid</c> carries water: the decided default of a hydronic tool, and one the file
/// should be able to see rather than infer (<c>D-32</c>, <c>L-87</c>). The note is written once per circuit that
/// is on its own, never for a branch hung off a circuit that did state its fluid: a radiator group joined to a
/// water header carries that header's water whatever it writes, and saying "carries water" on each of them would
/// be the same sentence about the same fluid. A coupled exchanger does not join the two sides it separates, so the
/// secondary of a plate exchanger is on its own and is told.
/// </para>
/// <para>
/// One fluid serves the whole file. <c>D-77</c> accepted a substance per hydraulic partition and it is not built
/// (<c>C-146</c>), so a file whose circuits state different fluids is solved as the first one, and a warning says
/// so on every circuit that is not.
/// </para>
/// </remarks>
public static class CircuitFluids
{
    /// <summary>Chooses the fluid the file is solved with.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="substances">The registry the names are resolved in.</param>
    /// <param name="diagnostics">Receives <c>FS2001</c> for a name no substance answers to, and <c>FS2009</c> for a circuit whose fluid is not the chosen one.</param>
    /// <returns>The first stated fluid that resolves, in declaration order; water when none does.</returns>
    public static ISubstance Choose(
        SemanticModel model,
        ISubstanceRegistry substances,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substances);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ISubstance? chosen = null;
        var stated = new List<(CircuitSymbol Circuit, ISubstance Substance)>();

        foreach (var circuit in model.Circuits)
        {
            if (circuit.Substance is not { } name)
            {
                continue;
            }

            var resolved = substances.Resolve(name);
            if (resolved.TryGetValue(out var substance))
            {
                chosen ??= substance;
                stated.Add((circuit, substance));
            }
            else
            {
                diagnostics.Add(resolved.Error!.At(circuit.DeclarationSpan));
            }
        }

        chosen ??= Water.Instance;

        foreach (var (circuit, substance) in stated)
        {
            if (!ReferenceEquals(substance, chosen))
            {
                diagnostics.Add(Diagnostic.Create(
                    FluidDiagnostics.OneFluidPerFile,
                    circuit.DeclarationSpan,
                    new DiagnosticArgument("circuit", circuit.Name),
                    new DiagnosticArgument("fluid", substance.Name),
                    new DiagnosticArgument("chosen", chosen.Name)));
            }
        }

        return chosen;
    }

    /// <summary>Notes each circuit that states no fluid and is not joined to one that does.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="graph">The lowered graph, whose hydraulic partitions decide what "joined" means.</param>
    /// <param name="substance">The fluid the file is solved with, which the note names.</param>
    /// <param name="diagnostics">Receives one <c>FS2008</c> per such circuit, on its header.</param>
    public static void ReportUnstated(
        SemanticModel model,
        CircuitGraph graph,
        ISubstance substance,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var unstated = model.Circuits.Where(static circuit => circuit.Substance is null).ToList();
        if (unstated.Count == 0)
        {
            return;
        }

        var statedNames = model.Circuits
            .Where(static circuit => circuit.Substance is not null)
            .Select(static circuit => circuit.Name)
            .ToHashSet(StringComparer.Ordinal);

        var partitions = HydraulicPartition.Of(graph);

        // A coupled exchanger is listed in both partitions it separates; it carries neither side's fluid into the
        // other, so it joins nothing here.
        var seen = new Dictionary<IFlowComponent, int>(ReferenceEqualityComparer.Instance);
        foreach (var element in partitions.SelectMany(static partition => partition.Elements))
        {
            seen[element] = seen.GetValueOrDefault(element) + 1;
        }

        var joined = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);

        foreach (var partition in partitions)
        {
            var circuits = partition.Elements
                .Where(element => seen[element] == 1)
                .Select(element => graph.CircuitOf.GetValueOrDefault(element.Name))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            present.UnionWith(circuits);
            if (circuits.Overlaps(statedNames))
            {
                joined.UnionWith(circuits);
            }
        }

        foreach (var circuit in unstated)
        {
            if (present.Contains(circuit.Name) && !joined.Contains(circuit.Name))
            {
                diagnostics.Add(Diagnostic.Create(
                    FluidDiagnostics.FluidNotStated,
                    circuit.DeclarationSpan,
                    new DiagnosticArgument("circuit", circuit.Name),
                    new DiagnosticArgument("fluid", substance.Name)));
            }
        }
    }
}
