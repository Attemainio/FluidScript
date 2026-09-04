using FluidScript.Core.Components;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// The rows the components declare, held against the rows the counting table counts.
/// </summary>
/// <remarks>
/// <para>
/// <c>S-9</c> asks the assembler to consume the counting table rather than re-derive it, and this is
/// the check that makes the agreement mean something. It has already earned its place twice: it found
/// <c>S-11</c> (an exchanger and the node it discharges into both claiming the same enthalpy relation)
/// and <c>S-14a</c> (a three-way valve writing a Kv law for a port with no node), neither of which any
/// failing test would have shown, because both circuits counted as square.
/// </para>
/// <para>
/// <strong>The one allowance is computed, not named.</strong> A component two branches cross has two
/// pressure relations by <c>Relations()</c> and declares one, because a coupled exchanger's second
/// side has no momentum equation yet (<c>S-14b</c>, <c>C-4</c>, <c>P4.1</c>). Written as a count of
/// multiply-crossed components rather than a list of sample names, the allowance disappears by itself
/// the day that equation lands — and this test starts failing until it is removed, which is the point.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EquationRowReconciliationTests
{
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ThePressureRowsDeclaredMatchThePressureRelationsCounted(string sample)
    {
        var graph = Lower(sample);
        var counting = WellPosedness.Check(graph).Counting;

        var declared = graph.Components
            .SelectMany(static component => component.DeclareEquations())
            .Count(static row => row.Kind is EquationKind.Pressure or EquationKind.ComponentConstraint);

        // The links are the table's own now (`S-15`): an assembler has to write those rows, so it has
        // to be told between which nodes, and a walk here would be the second implementation that
        // naming them exists to prevent.
        Assert.Equal(
            counting.PressureRelations - RowAllowance.CoupledCrossings(graph),
            declared + counting.IdealLinks.Length);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void AnUnconnectedPortIsOneTheKindDeclaredOptional(string sample)
    {
        // The property S-14a violated, stated as an invariant rather than as the count that revealed
        // it. Rule I3 terminates every non-optional port with a boundary node, so one left with no peer
        // means something went missing. An *optional* port with no peer is ordinary -- a duty-mode
        // exchanger's second side is two of them -- and what a kind may not do is keep declaring
        // equations that read one.
        var lowered = GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)));
        var graph = lowered.Graph;

        // A component the factory could not build takes its connections with it, and lowering says so
        // in as many words: the node's port counter still advanced, so a peerless port is left behind.
        // That path is deliberate and tested elsewhere; this invariant is about a graph that resolved.
        Assert.SkipWhen(
            !lowered.Unresolved.IsEmpty,
            $"{sample} drops {string.Join(", ", lowered.Unresolved)}, so its ports are expected to be short.");

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var component = graph.Components[element];

            for (var port = 0; port < component.Ports.Length; port++)
            {
                if (graph.Adjacency.Peer(element, port).Exists || component.Ports[port].IsOptional)
                {
                    continue;
                }

                Assert.Fail(
                    $"{component.Name}.{component.Ports[port].Name} is not optional and is connected to "
                    + "nothing, so I3 did not reach it and its residual reads a state that does not exist.");
            }
        }
    }

    private static CircuitGraph Lower(string sample) =>
        GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;
}
