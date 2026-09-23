using System.Collections.Immutable;

namespace FluidScript.Core.Language.Binding;

/// <summary>Where every component sits, once the script's heights have been propagated (<c>D-70</c>, <c>D-95</c>).</summary>
/// <remarks>
/// <para>
/// A height is a property of position, and only a pipe or a bare connection spans two of them. So the
/// map carries one height per single-height component and node, and two per pipe — one for each
/// port — from which the pipe's rise is <c>z(out) − z(in)</c>. Nothing here is a parameter the script
/// wrote: it is the result of walking the connections, and a component that wrote nothing sits where
/// what it is wired to says it does.
/// </para>
/// <para>
/// Metres above the project datum. A component the walk never reached, or a script with no height in
/// it at all, reads 0 everywhere, which is what makes every existing script mean what it did.
/// </para>
/// </remarks>
public sealed record HeightMap
{
    /// <summary>A map in which everything sits at 0 m.</summary>
    public static HeightMap Empty { get; } = new() { Heights = ImmutableDictionary<string, double>.Empty };

    /// <summary>Gets the heights, keyed by component name or by <c>component.port</c> for a pipe.</summary>
    /// <value>m above the project datum.</value>
    public required ImmutableDictionary<string, double> Heights { get; init; }

    /// <summary>The height of a single-height component or a node.</summary>
    /// <param name="component">The component's name.</param>
    /// <returns>m above the project datum; 0 where nothing placed it.</returns>
    public double Of(string component) => Heights.GetValueOrDefault(component);

    /// <summary>The height of one port.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="port">The port's name.</param>
    /// <returns>
    /// m above the project datum: the port's own height for a pipe, the component's for everything
    /// else, 0 where nothing placed it.
    /// </returns>
    public double Of(string component, string port) =>
        Heights.TryGetValue($"{component}.{port}", out var own) ? own : Of(component);

    /// <summary>A pipe's rise, outlet height minus inlet height.</summary>
    /// <param name="pipe">The pipe's name.</param>
    /// <returns>m, positive when the outlet is higher.</returns>
    public double Rise(string pipe) => Of(pipe, "out") - Of(pipe, "in");
}
