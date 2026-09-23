using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Topology.Construction;

namespace FluidScript.Core.Catalogs.Pipes;

/// <summary>Turns a DN designation into a bore by reading a resolved pipe catalogue.</summary>
/// <param name="resolved">The catalogue, as <see cref="PipeCatalogs.Resolve(CatalogPin?)"/> returned it.</param>
/// <param name="available">Every shipped catalogue by id, for a pipe whose own <c>material</c> names one (<c>C-36</c>).</param>
/// <remarks>
/// <para>
/// The implementation <c>P3.4a</c> put the <see cref="IBoreLookup"/> seam in front of, closing
/// <c>C-24</c> and <c>F-18</c>. The seam stays rather than being inlined: <c>P3.7</c>'s outer loop
/// re-instantiates components as sizing chooses values, so lowering has to be re-runnable against
/// changing geometry, and a lookup it is handed is what makes that possible.
/// </para>
/// <para>
/// It is built from a <em>resolved</em> catalogue and nothing else (<c>C-39</c>): resolution is where
/// provenance is enforced (<c>FS2605</c>), and a lookup over an unverified catalogue would size a
/// pipe from a row nobody has checked. Reading a row for what a designation means is a different
/// act, and is done on the catalogue's entries directly.
/// </para>
/// </remarks>
public sealed class CatalogBoreLookup(
    ResolvedCatalog<PipeSpec> resolved,
    IReadOnlyDictionary<string, ICatalog<PipeSpec>>? available = null) : IBoreLookup
{
    private readonly ICatalog<PipeSpec> catalog = (resolved ?? throw new ArgumentNullException(nameof(resolved))).Catalog;

    /// <inheritdoc/>
    /// <remarks>
    /// An exact designation match, never a nearest one. <c>dn=27</c> is a script naming a size that
    /// does not exist, and quietly sizing it as DN25 would answer a question nobody asked. A pipe's own
    /// <c>material</c> names another shipped catalogue (<c>C-36</c>); one that fails its provenance
    /// check, or is not shipped, yields nothing, and the pipe is not built -- the script's catalogue
    /// went through <see cref="PipeCatalogs.Resolve(CatalogPin?)"/> and needs no check here.
    /// </remarks>
    public double? BoreFor(double nominalDiameter, string? material = null)
    {
        var series = catalog;

        if (material is not null && !string.Equals(material, catalog.Name, StringComparison.Ordinal))
        {
            if (available is null
                || !available.TryGetValue(material, out var other)
                || !other.Validate().IsEmpty)
            {
                return null;
            }

            series = other;
        }

        foreach (var entry in series.Entries)
        {
            if (entry.Spec.NominalDiameter == nominalDiameter)
            {
                return entry.Spec.InsideDiameter;
            }
        }

        return null;
    }
}
