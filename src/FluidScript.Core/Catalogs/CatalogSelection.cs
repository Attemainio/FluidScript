namespace FluidScript.Core.Catalogs;

/// <summary>A chosen catalogue entry and how well it fitted.</summary>
/// <typeparam name="TSpec">The dimensional record the catalogue holds.</typeparam>
/// <param name="Entry">The entry selected. Never absent — a non-empty catalogue always answers.</param>
/// <param name="Fit">Whether the answer was clamped at either end of the series.</param>
public readonly record struct CatalogSelection<TSpec>(CatalogEntry<TSpec> Entry, CatalogFit Fit);
