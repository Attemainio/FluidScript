using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;

namespace FluidScript.Core.Catalogs;

/// <summary>How well the chosen entry matched what was asked for.</summary>
/// <remarks>
/// The catalogue reports the fit and stops there. <c>FS2601</c> and <c>FS2602</c> name the
/// <em>component</em> that got clamped, and a catalogue that knew the caller's name would be a
/// catalogue that had to be told it — so the sizing loop builds those diagnostics (<c>P3.7</c>).
/// </remarks>
public enum CatalogFit
{
    /// <summary>An entry satisfied the request with something smaller available below it.</summary>
    Exact,

    /// <summary>Nothing satisfied the request; the largest entry was taken instead.</summary>
    ClampedToLargest,

    /// <summary>The smallest entry already satisfied the request, so the ideal size is below the series.</summary>
    ClampedToSmallest,
}

/// <summary>A chosen catalogue entry and how well it fitted.</summary>
/// <typeparam name="TSpec">The dimensional record the catalogue holds.</typeparam>
/// <param name="Entry">The entry selected. Never absent — a non-empty catalogue always answers.</param>
/// <param name="Fit">Whether the answer was clamped at either end of the series.</param>
public readonly record struct CatalogSelection<TSpec>(CatalogEntry<TSpec> Entry, CatalogFit Fit);

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
