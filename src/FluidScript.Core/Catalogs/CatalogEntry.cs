namespace FluidScript.Core.Catalogs;

/// <summary>One selectable size from a catalogue.</summary>
/// <typeparam name="TSpec">The dimensional record this catalogue holds.</typeparam>
public sealed record CatalogEntry<TSpec>
{
    /// <summary>The designation as an engineer would write it.</summary>
    /// <value><c>"DN25"</c>, <c>"Kv 1.6"</c>.</value>
    public required string Designation { get; init; }

    /// <summary>The dimensional data.</summary>
    public required TSpec Spec { get; init; }

    /// <summary>Where this row came from and when.</summary>
    /// <remarks>
    /// Never absent. A row without provenance cannot be defended when a user asks why their pipe is
    /// 27.3 mm, and cannot be audited when a value turns out to be wrong.
    /// </remarks>
    public required Provenance Provenance { get; init; }
}
