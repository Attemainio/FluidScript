using System.Collections.Immutable;

namespace FluidScript.Core.Language.Binding;

/// <summary>Resolves evaluation order and detects circularity among script-level values.</summary>
/// <remarks>
/// <para>
/// Nodes are <c>let</c> bindings, component parameters and component properties; edges point from a
/// dependency to its dependent. Order comes from the graph and never from source position, because
/// canvas write-back inserts lines and must not have to reason about where.
/// </para>
/// <para>
/// <strong>A static cycle is an error; a cycle through a solved value is not.</strong>
/// <c>let a = b + 1</c> with <c>let b = a + 1</c> has no solution and is <c>FS1402</c>.
/// <c>PU1.head → HE1.dp → (solve) → PU1.head</c> is a fixed point, and the graph classifies it as
/// deferred instead. Rejecting all cycles rejects the useful case; accepting all cycles hangs.
/// </para>
/// </remarks>
public sealed class DependencyGraph
{
    private readonly Dictionary<ValueId, HashSet<ValueId>> _dependencies = [];
    private readonly List<ValueId> _order = [];

    /// <summary>Records that a value exists, whether or not anything depends on it.</summary>
    /// <param name="value">The value's identity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public void Add(ValueId value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_dependencies.TryAdd(value, []))
        {
            _order.Add(value);
        }
    }

    /// <summary>Records that one value reads another.</summary>
    /// <param name="dependent">The value doing the reading.</param>
    /// <param name="dependency">The value being read.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public void AddDependency(ValueId dependent, ValueId dependency)
    {
        ArgumentNullException.ThrowIfNull(dependent);
        ArgumentNullException.ThrowIfNull(dependency);

        Add(dependent);
        Add(dependency);

        _dependencies[dependent].Add(dependency);
    }

    /// <summary>Gets what a value reads directly.</summary>
    /// <param name="value">The value's identity.</param>
    /// <returns>Its direct dependencies, empty when it has none or is unknown.</returns>
    public ImmutableHashSet<ValueId> DependenciesOf(ValueId value) =>
        _dependencies.TryGetValue(value, out var found) ? [.. found] : [];

    /// <summary>Orders every value so each follows what it depends on.</summary>
    /// <returns>The evaluation order, or the first cycle found.</returns>
    /// <remarks>
    /// <para>
    /// Depth-first rather than Kahn's algorithm for one reason: when it fails it has the cycle on its
    /// own path, and reporting the whole cycle is what <c>FS1402</c> needs. Kahn's leaves a set of
    /// unordered nodes, from which recovering the cycle is a second traversal.
    /// </para>
    /// <para>
    /// The path lives on an explicit stack, not the call stack. Its depth is the longest dependency
    /// chain in the script, which nothing bounds -- a generated script can chain thousands of
    /// well-formed references -- and a stack overflow cannot be caught.
    /// </para>
    /// </remarks>
    public OrderResult TopologicalOrder()
    {
        var ordered = ImmutableArray.CreateBuilder<ValueId>(_order.Count);
        var finished = new HashSet<ValueId>();
        var path = new List<ValueId>();
        var onPath = new HashSet<ValueId>();

        // One frame per value on the path: the value, its dependencies in a fixed order, and how many
        // of them the walk has already entered.
        var frames = new Stack<(ValueId Value, ValueId[] Dependencies, int Next)>();

        foreach (var start in _order)
        {
            if (finished.Contains(start))
            {
                continue;
            }

            Enter(start);

            while (frames.Count > 0)
            {
                var (value, dependencies, next) = frames.Pop();

                if (next < dependencies.Length)
                {
                    frames.Push((value, dependencies, next + 1));
                    var dependency = dependencies[next];

                    if (finished.Contains(dependency))
                    {
                        continue;
                    }

                    if (onPath.Contains(dependency))
                    {
                        // The cycle is the path from where this value first appears, closed by repeating it.
                        var from = path.IndexOf(dependency);
                        return new OrderResult.Cyclic([.. path[from..], dependency]);
                    }

                    Enter(dependency);
                    continue;
                }

                path.RemoveAt(path.Count - 1);
                onPath.Remove(value);
                finished.Add(value);
                ordered.Add(value);
            }
        }

        return new OrderResult.Ordered(ordered.ToImmutable());

        void Enter(ValueId value)
        {
            path.Add(value);
            onPath.Add(value);
            frames.Push((value, [.. _dependencies[value].OrderBy(static id => id.ToString(), StringComparer.Ordinal)], 0));
        }
    }
}
