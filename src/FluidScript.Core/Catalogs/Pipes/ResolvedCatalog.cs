using System.Collections.Immutable;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Catalogs.Pipes;

/// <summary>A resolved catalogue and whatever the resolution had to say about it.</summary>
/// <typeparam name="TSpec">The dimensional record the catalogue holds.</typeparam>
/// <param name="Catalog">The catalogue to size against.</param>
/// <param name="Notes">
/// Informational results of the resolution itself -- today only <c>FS2606</c>, the unpinned default.
/// Carried rather than emitted because only the caller knows the span to hang them on.
/// </param>
public sealed record ResolvedCatalog<TSpec>(ICatalog<TSpec> Catalog, ImmutableArray<ResultError> Notes);
