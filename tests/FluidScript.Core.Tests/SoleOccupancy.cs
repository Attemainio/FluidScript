namespace FluidScript.Core.Tests;

/// <summary>The tests that read the process's working set, run alone.</summary>
/// <remarks>
/// The working set is one number for the whole process, so a memory assertion made while other
/// classes run in parallel measures their heaps too: under the full suite the same read that grows it
/// by 5 MB alone read 72 MB, and a solve that keeps nothing read 494 MB. A collection with
/// parallelization disabled is run on its own, after the parallel ones.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SoleOccupancy
{
    /// <summary>The collection's name, for <c>[Collection]</c>.</summary>
    public const string Name = "memory";
}
