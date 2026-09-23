using System.Collections.Immutable;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Catalogs;

/// <summary>The standard sizes available to the sizer.</summary>
/// <typeparam name="TSpec">The dimensional record this catalogue holds.</typeparam>
public interface ICatalog<TSpec>
{
    /// <summary>The catalogue's id, as a script pins it.</summary>
    /// <value><c>"steel_en10255"</c>.</value>
    string Name { get; }

    /// <summary>The catalogue's version, as a script pins it after <c>@</c>.</summary>
    string Version { get; }

    /// <summary>The standard this catalogue conforms to, cited by number.</summary>
    string? Standard { get; }

    /// <summary>Every entry, ascending by designation.</summary>
    IReadOnlyList<CatalogEntry<TSpec>> Entries { get; }

    /// <summary>The smallest entry satisfying a predicate.</summary>
    /// <param name="predicate">What the entry has to be big enough for.</param>
    /// <returns>The entry and its fit; the largest entry when nothing satisfies the predicate.</returns>
    CatalogSelection<TSpec> SmallestSatisfying(Func<TSpec, bool> predicate);

    /// <summary>The nearest entry at or below a target.</summary>
    /// <param name="target">The wanted value, SI.</param>
    /// <param name="selector">Reads the compared value off a spec, SI.</param>
    /// <returns>
    /// The entry and its fit. Below, not above, because an undersized valve authority is recoverable
    /// and an oversized one is a valve that controls nothing over most of its travel.
    /// </returns>
    CatalogSelection<TSpec> NearestBelow(double target, Func<TSpec, double> selector);

    /// <summary>Everything wrong with this catalogue, or empty when it is fit to size against.</summary>
    /// <returns>
    /// <c>FS2604</c> for a structural or plausibility failure and <c>FS2605</c> for unverified
    /// provenance, carried rather than thrown so that a bad table fails where it is resolved instead of
    /// wherever a static initializer happened to run.
    /// </returns>
    ImmutableArray<ResultError> Validate();
}
