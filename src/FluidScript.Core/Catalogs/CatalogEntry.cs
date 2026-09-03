using System.Collections.Immutable;

namespace FluidScript.Core.Catalogs;

/// <summary>One public document a catalogue row was read from.</summary>
/// <param name="Publisher">Who published it — a manufacturer, not a standards body's paywall.</param>
/// <param name="Url">The public URL it was read from.</param>
/// <param name="Retrieved">The date it was read, because a manufacturer's catalogue is revised.</param>
public sealed record SourceReference(string Publisher, string Url, DateOnly Retrieved);

/// <summary>Where a catalogue value came from, and whether a person checked it.</summary>
/// <remarks>
/// <para>
/// <strong>Two sources, and a human.</strong> A single manufacturer's typo becomes a silent sizing
/// error affecting every circuit that lands on that diameter: a wrong bore propagates into velocity,
/// Reynolds number, friction factor and pump head, and the result looks entirely reasonable at every
/// step. Two independent sources agreeing is the cheapest check that catches it.
/// </para>
/// <para>
/// <see cref="Standard"/> cites the standard by <em>number only</em>. The dimensions themselves are
/// facts about physical objects and are not copyrightable; a standard's table layout, selection,
/// arrangement and notes are, and none of them appears in this repository.
/// </para>
/// </remarks>
public sealed record Provenance
{
    /// <summary>The standard this row conforms to, cited by number.</summary>
    /// <value><c>"EN 10255"</c>, or <see langword="null"/> for a manufacturer-specific value.</value>
    public string? Standard { get; init; }

    /// <summary>The public sources consulted.</summary>
    /// <value>At least two for a verified row; may be empty while a row awaits sourcing.</value>
    public ImmutableArray<SourceReference> Sources { get; init; } = [];

    /// <summary>Whether a person checked this row against the sources above.</summary>
    /// <remarks>
    /// An attestation, not a computation, and nothing may set it from the data. A row that reaches a
    /// user unverified is a design defended by nobody.
    /// </remarks>
    public required bool Verified { get; init; }

    /// <summary>Whether this row may be used to size anything.</summary>
    /// <value><see langword="true"/> when a person verified it against two or more sources.</value>
    public bool IsUsable => Verified && Sources.Length >= 2;
}

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
