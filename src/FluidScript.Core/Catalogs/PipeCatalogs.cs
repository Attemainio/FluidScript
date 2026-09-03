using System.Collections.Immutable;

using FluidScript.Core.Compatibility;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Catalogs;

/// <summary>A resolved catalogue and whatever the resolution had to say about it.</summary>
/// <typeparam name="TSpec">The dimensional record the catalogue holds.</typeparam>
/// <param name="Catalog">The catalogue to size against.</param>
/// <param name="Notes">
/// Informational results of the resolution itself -- today only <c>FS2606</c>, the unpinned default.
/// Carried rather than emitted because only the caller knows the span to hang them on.
/// </param>
public sealed record ResolvedCatalog<TSpec>(ICatalog<TSpec> Catalog, ImmutableArray<ResultError> Notes);

/// <summary>Every pipe catalogue this build ships, and how a script's pin resolves against them.</summary>
/// <remarks>
/// <para>
/// Shipped and curated, never fetched. A runtime dependency on somebody else's website would make
/// sizing non-deterministic, break offline use, break the reproducibility of a saved design, and put a
/// third party in the path of every solve.
/// </para>
/// <para>
/// <strong>A misspelled pin is an error, never a fallback.</strong> Resolving <c>steel_en10225</c> to
/// the default would size a design against a series nobody chose, which is the whole failure this
/// document exists to prevent -- so <c>FS2603</c> names what is available and stops.
/// </para>
/// </remarks>
public static class PipeCatalogs
{
    /// <summary>The catalogue used when a script pins none.</summary>
    public static ICatalog<PipeSpec> Default => SteelEn10255.Instance;

    /// <summary>Every shipped pipe catalogue, by id.</summary>
    public static ImmutableDictionary<string, ICatalog<PipeSpec>> All { get; } =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new[] { SteelEn10255.Instance, CopperEn1057.Instance }.Select(
                static catalog => KeyValuePair.Create(catalog.Name, catalog)));

    /// <summary>Resolves the catalogue a script asked for.</summary>
    /// <param name="pin">The script's <c>catalog</c> directive, or <see langword="null"/> for none.</param>
    /// <returns>
    /// The catalogue and its notes, or a failure: <c>FS2603</c> when the id is unknown, and
    /// <c>FS2604</c>/<c>FS2605</c> when the catalogue itself does not hold up. Validation runs at
    /// resolution rather than at construction so that a bad table fails somewhere a message can be
    /// attached, instead of as a type-initializer exception from an unrelated line.
    /// </returns>
    /// <remarks>
    /// A pinned <em>version</em> is not matched here. <c>18-script-compatibility</c> owns what happens
    /// when a file asks for a version this build does not ship, and answering it twice, differently, is
    /// how the two documents would drift apart.
    /// </remarks>
    public static Result<ResolvedCatalog<PipeSpec>> Resolve(CatalogPin? pin) =>
        Resolve(pin, All, Default);

    /// <summary>Resolves a pin against a supplied set of catalogues.</summary>
    /// <param name="pin">The script's <c>catalog</c> directive, or <see langword="null"/> for none.</param>
    /// <param name="available">The catalogues to resolve against, by id.</param>
    /// <param name="fallback">The catalogue an absent pin selects.</param>
    /// <returns>As the single-argument overload.</returns>
    /// <remarks>
    /// The set is a parameter because the shipped one is unverified and therefore refused, which would
    /// otherwise make <c>FS2606</c> unobservable: the default is selected, the note is built, and then
    /// validation fails the whole resolution before anybody can see it. A code that cannot be watched
    /// fire is the shape of defect <c>S-8</c> was, so the seam is worth more than the argument costs.
    /// </remarks>
    public static Result<ResolvedCatalog<PipeSpec>> Resolve(
        CatalogPin? pin,
        IReadOnlyDictionary<string, ICatalog<PipeSpec>> available,
        ICatalog<PipeSpec> fallback)
    {
        ArgumentNullException.ThrowIfNull(available);

        var notes = ImmutableArray.CreateBuilder<ResultError>();
        ICatalog<PipeSpec> catalog;

        if (pin is null)
        {
            catalog = fallback;
            notes.Add(ResultError.From(CatalogDiagnostics.DefaultCatalogUsed, ("name", catalog.Name)));
        }
        else if (!available.TryGetValue(pin.Id, out var pinned))
        {
            return Result.Failure<ResolvedCatalog<PipeSpec>>(ResultError.From(
                CatalogDiagnostics.UnknownCatalog,
                ("name", pin.Id),
                ("list", string.Join(", ", available.Keys.Order(StringComparer.Ordinal)))));
        }
        else
        {
            catalog = pinned;
        }

        return catalog.Validate() is { Length: > 0 } faults
            ? Result.Failure<ResolvedCatalog<PipeSpec>>(faults[0])
            : Result.Success(new ResolvedCatalog<PipeSpec>(catalog, notes.ToImmutable()));
    }
}

/// <summary>Turns a DN designation into a bore by reading a pipe catalogue.</summary>
/// <param name="catalog">The catalogue to read.</param>
/// <remarks>
/// The implementation <c>P3.4a</c> put the <see cref="IBoreLookup"/> seam in front of, closing
/// <c>C-24</c> and <c>F-18</c>. The seam stays rather than being inlined: <c>P3.7</c>'s outer loop
/// re-instantiates components as sizing chooses values, so lowering has to be re-runnable against
/// changing geometry, and a lookup it is handed is what makes that possible.
/// </remarks>
public sealed class CatalogBoreLookup(ICatalog<PipeSpec> catalog) : IBoreLookup
{
    /// <inheritdoc/>
    /// <remarks>
    /// An exact designation match, never a nearest one. <c>dn=27</c> is a script naming a size that
    /// does not exist, and quietly sizing it as DN25 would answer a question nobody asked.
    /// </remarks>
    public double? BoreFor(double nominalDiameter)
    {
        foreach (var entry in catalog.Entries)
        {
            if (entry.Spec.NominalDiameter == nominalDiameter)
            {
                return entry.Spec.InsideDiameter;
            }
        }

        return null;
    }
}
