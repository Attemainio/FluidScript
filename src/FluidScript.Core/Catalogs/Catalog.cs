using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Catalogs;

/// <summary>A catalogue held as an ordered, immutable table.</summary>
/// <typeparam name="TSpec">The dimensional record this catalogue holds.</typeparam>
/// <remarks>
/// <para>
/// The rows are C# rather than a data file, which is <c>D-66</c>. <c>D-47</c> forbids any file in Core
/// from reaching a serializer, and a hand-rolled reader for one internal format buys nothing a
/// compiled table does not already give: it is versioned in git, it is human-readable, no network is
/// reachable from it, and a malformed row is a build error rather than an <c>FS2604</c> somebody sees
/// at run time.
/// </para>
/// <para>
/// <strong>That rule is checked by scanning source text</strong>, so naming the forbidden namespace
/// here — even to explain the rule — is itself a violation. The check is a floor and reads like one.
/// </para>
/// <para>
/// Nothing here throws. Ordering, duplication and plausibility are reported by <see cref="Validate"/>
/// and enforced by whoever resolves the catalogue, because a table checked in a static constructor
/// fails as a <c>TypeInitializationException</c> raised from an unrelated line.
/// </para>
/// </remarks>
public sealed class Catalog<TSpec> : ICatalog<TSpec>
{
    private readonly ImmutableArray<CatalogEntry<TSpec>> _entries;
    private readonly Func<TSpec, string?>? _plausibility;
    private readonly Func<TSpec, double>? _order;

    /// <summary>Builds a catalogue from an ordered set of rows.</summary>
    /// <param name="name">The id a script pins.</param>
    /// <param name="version">The version a script pins after <c>@</c>.</param>
    /// <param name="standard">The standard cited by number, or <see langword="null"/>.</param>
    /// <param name="entries">The rows, ascending by designation.</param>
    /// <param name="plausibility">
    /// Returns what is wrong with one spec, or <see langword="null"/> when it is plausible. Supplied by
    /// the catalogue's owner because the checks are dimensional and this class is generic.
    /// </param>
    /// <param name="order">
    /// Reads the value the rows are meant to ascend by. Supplied for the same reason as
    /// <paramref name="plausibility"/>, and checked because <see cref="SmallestSatisfying"/> takes the
    /// first match as the smallest -- on a table that stops ascending, that is quietly the wrong row.
    /// </param>
    public Catalog(
        string name,
        string version,
        string? standard,
        IEnumerable<CatalogEntry<TSpec>> entries,
        Func<TSpec, string?>? plausibility = null,
        Func<TSpec, double>? order = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Name = name;
        Version = version;
        Standard = standard;
        _entries = [.. entries];
        _plausibility = plausibility;
        _order = order;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Version { get; }

    /// <inheritdoc/>
    public string? Standard { get; }

    /// <inheritdoc/>
    public IReadOnlyList<CatalogEntry<TSpec>> Entries => _entries;

    /// <inheritdoc/>
    public CatalogSelection<TSpec> SmallestSatisfying(Func<TSpec, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (var i = 0; i < _entries.Length; i++)
        {
            if (!predicate(_entries[i].Spec))
            {
                continue;
            }

            // The first row satisfying it is also the smallest, so a hit at index 0 means the size the
            // caller actually wanted sits below the series rather than inside it.
            return new CatalogSelection<TSpec>(
                _entries[i], i == 0 ? CatalogFit.ClampedToSmallest : CatalogFit.Exact);
        }

        return new CatalogSelection<TSpec>(_entries[^1], CatalogFit.ClampedToLargest);
    }

    /// <inheritdoc/>
    public CatalogSelection<TSpec> NearestBelow(double target, Func<TSpec, double> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var chosen = -1;

        for (var i = 0; i < _entries.Length; i++)
        {
            if (selector(_entries[i].Spec) <= target)
            {
                chosen = i;
            }
        }

        return chosen < 0
            ? new CatalogSelection<TSpec>(_entries[0], CatalogFit.ClampedToSmallest)
            : new CatalogSelection<TSpec>(
                _entries[chosen], chosen == _entries.Length - 1 ? CatalogFit.ClampedToLargest : CatalogFit.Exact);
    }

    /// <inheritdoc/>
    public ImmutableArray<ResultError> Validate()
    {
        var found = ImmutableArray.CreateBuilder<ResultError>();

        if (_entries.Length == 0)
        {
            found.Add(Invalid("it has no entries"));
            return found.ToImmutable();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in _entries)
        {
            if (!seen.Add(entry.Designation))
            {
                found.Add(Invalid($"'{entry.Designation}' appears more than once"));
            }

            if (_plausibility?.Invoke(entry.Spec) is { } wrong)
            {
                found.Add(Invalid($"'{entry.Designation}' {wrong}"));
            }
        }

        for (var i = 1; _order is not null && i < _entries.Length; i++)
        {
            if (_order(_entries[i].Spec) <= _order(_entries[i - 1].Spec))
            {
                found.Add(Invalid(
                    $"'{_entries[i].Designation}' does not sit above '{_entries[i - 1].Designation}'"));
            }
        }

        var unusable = _entries.Where(static entry => !entry.Provenance.IsUsable).ToArray();

        if (unusable.Length > 0)
        {
            found.Add(ResultError.From(
                CatalogDiagnostics.UnverifiedCatalog,
                ("name", Name),
                ("count", unusable.Length.ToString(CultureInfo.InvariantCulture)),
                ("first", unusable[0].Designation)));
        }

        return found.ToImmutable();
    }

    private ResultError Invalid(string reason) =>
        ResultError.From(CatalogDiagnostics.InvalidCatalog, ("name", Name), ("reason", reason));
}
