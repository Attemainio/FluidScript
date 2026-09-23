using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>A stable identity for any value that may participate in evaluation.</summary>
public abstract record ValueId
{
    private ValueId()
    {
    }

    /// <summary>A <c>let</c> binding.</summary>
    /// <param name="Name">The bound name.</param>
    public sealed record Let(string Name) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>A parameter written on a component.</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Parameter">The canonical parameter name.</param>
    public sealed record ComponentParameter(string Component, string Parameter) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Parameter}";
    }

    /// <summary>One element of a parameter's per-scenario value list (<c>D-143</c>).</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Parameter">The canonical parameter name.</param>
    /// <param name="Scenario">The element's position, which is the scenario's position.</param>
    /// <remarks>
    /// Separate from <see cref="ComponentParameter"/> because each element is evaluated on its own:
    /// an element may read a curve or a <c>let</c> the others do not, so they are different nodes in
    /// the dependency graph even though they are one line in the file.
    /// </remarks>
    public sealed record ScenarioParameter(string Component, string Parameter, int Scenario) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Parameter}[{Scenario}]";
    }

    /// <summary>A property read off a component.</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Property">The canonical property name.</param>
    public sealed record ComponentProperty(string Component, string Property) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Property}";
    }

    /// <summary>A curve, read at its driver's design value (<c>D-57</c>).</summary>
    /// <param name="Name">The curve's name.</param>
    /// <remarks>
    /// A node like any other, which is what makes a cycle among curves the same <c>FS1402</c> a cycle
    /// among <c>let</c> bindings already was, reported by the same depth-first sort.
    /// </remarks>
    public sealed record Curve(string Name) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>One driver's value at the design condition (<c>D-58</c>).</summary>
    /// <param name="Driver">The canonical driver name the <c>design</c> line was keyed under.</param>
    public sealed record Design(string Driver) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"design {Driver}";
    }

    /// <summary>One driver's value at a component's own sizing point (<c>D-94</c>).</summary>
    /// <param name="Component">The component that wrote the <c>sized_at</c> clause.</param>
    /// <param name="Driver">The canonical driver name the clause's argument was keyed under.</param>
    /// <remarks>
    /// A node of its own so the component's parameters are ordered after it: a curve they read is
    /// evaluated at this value rather than at the file's <see cref="Design"/>.
    /// </remarks>
    public sealed record SizingPoint(string Component, string Driver) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component} sized_at {Driver}";
    }
}

/// <summary>An expression held until sizing or solving supplies all of its inputs.</summary>
/// <param name="Expression">The expression, unevaluated.</param>
/// <param name="Target">What the expression sets.</param>
/// <param name="CurrentEstimate">
/// The value the last outer pass produced, or <see langword="null"/> when the first pass must obtain
/// the target from normal sizing before re-evaluating.
/// </param>
/// <param name="Dependencies">
/// Every <see cref="ValueId"/> the expression reads directly, including ones already static, so cycle
/// formatting and invalidation are deterministic.
/// </param>
public sealed record DeferredExpression(
    ExpressionSyntax Expression,
    ValueId Target,
    Quantity? CurrentEstimate,
    ImmutableHashSet<ValueId> Dependencies)
{
    /// <summary>Gets the text the expression was parsed from, so the outer loop can evaluate it and quote it (<c>L-59</c>).</summary>
    /// <value><see langword="null"/> only for a deferral built without one, which nothing in Core does.</value>
    public FluidScript.Core.Language.Syntax.Text.SourceText? Source { get; init; }
}

/// <summary>The order values may be evaluated in, or the cycle that prevents one.</summary>
public abstract record OrderResult
{
    private OrderResult()
    {
    }

    /// <summary>Every value, in an order where each follows what it depends on.</summary>
    /// <param name="Order">The evaluation order.</param>
    public sealed record Ordered(ImmutableArray<ValueId> Order) : OrderResult;

    /// <summary>A value depends on itself.</summary>
    /// <param name="Cycle">The participating ids in cycle order, the first repeated at the end.</param>
    public sealed record Cyclic(ImmutableArray<ValueId> Cycle) : OrderResult;
}

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
