using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    /// <summary>What a timestamp may look like when the curve states no <c>format=</c>.</summary>
    /// <remarks>
    /// ISO 8601 only, per <c>D-60</c>. Culture-inferred layouts are rejected outright: the proposal's
    /// own example ran <c>1.1</c>, <c>1.1</c>, <c>1.2</c> a minute apart, and whether the third point
    /// is a day or a month later cannot be recovered from the text.
    /// </remarks>
    private static readonly string[] IsoTimestamps =
    [
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
    ];

    private static readonly char[] RowSeparators = [' ', '\t'];

    /// <summary>Reads every row of one curve into a sorted table.</summary>
    /// <remarks>
    /// Rows written out of order are sorted here rather than reported: a weather file is not obliged
    /// to arrive monotonic, and <see cref="CurveSymbol.Evaluate"/> needs the order, not the user.
    /// </remarks>
    private ImmutableArray<CurvePoint> ReadRows(CurveDraft draft, string name, bool isTime, string? format)
    {
        var read = new List<CurvePoint>();
        var unreadable = 0;

        foreach (var row in draft.Rows)
        {
            if (ReadRow(row, isTime, format) is { } point)
            {
                read.Add(point);
                continue;
            }

            if (++unreadable <= UnreadableRowsShown)
            {
                Report(ParserDiagnostics.MalformedCurveRow, row.Span);
            }
        }

        if (unreadable > UnreadableRowsShown)
        {
            Report(
                BinderDiagnostics.CurveRowsUnreadable,
                draft.Header.Span,
                ("curve", name),
                ("count", (unreadable - UnreadableRowsShown).ToString(CultureInfo.InvariantCulture)),
                ("shown", UnreadableRowsShown.ToString(CultureInfo.InvariantCulture)));
        }

        // Stable, so two rows at one x stay in the order they were written and the later one is the
        // one kept below.
        var sorted = read.OrderBy(static point => point.X).ToArray();
        var table = ImmutableArray.CreateBuilder<CurvePoint>(sorted.Length);

        foreach (var point in sorted)
        {
            if (table.Count > 0 && table[^1].X == point.X)
            {
                // Information, not an error: a step is a legitimate thing to write, and the later row
                // is what a reader of the file would expect to win.
                Report(
                    BinderDiagnostics.DuplicateCurveRow,
                    draft.Header.Span,
                    ("curve", name),
                    ("x", point.X.ToString("0.###", CultureInfo.InvariantCulture)));

                table[^1] = point;
                continue;
            }

            table.Add(point);
        }

        return table.ToImmutable();
    }

    /// <summary>Splits one row into its two columns and reads both.</summary>
    /// <remarks>
    /// Split at the last run of whitespace, not on tokens: <c>-26</c> is two tokens and one value, and
    /// <c>01/01/2026 00:00:00</c> is many tokens and one timestamp. A timestamp cannot be lexed as a
    /// unit, because <c>2026-01-01</c> is also a perfectly good subtraction, so the split has to
    /// happen over the text and it has to happen here.
    /// </remarks>
    private CurvePoint? ReadRow(CurveRowSyntax row, bool isTime, string? format)
    {
        var text = parse.Source.ToString(row.Span).Trim();
        var cut = text.LastIndexOfAny(RowSeparators);

        if (cut < 0)
        {
            return null;
        }

        if (!double.TryParse(
                text[(cut + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return null;
        }

        var written = text[..cut].TrimEnd();

        return (isTime ? ReadTimestamp(written, format) : ReadNumber(written)) is { } x
            ? new CurvePoint(x, y)
            : null;
    }

    private static double? ReadNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Reads one <c>x</c> of a time-driven curve, in Unix seconds (<c>D-60</c>).</summary>
    /// <param name="text">The column as written.</param>
    /// <param name="format">The curve's <c>format=</c>, or <see langword="null"/> for the defaults.</param>
    /// <returns>Seconds since the Unix epoch, or <see langword="null"/> when nothing read it.</returns>
    /// <remarks>
    /// The format string is .NET's and its case matters: <c>MM</c> is the month and <c>mm</c> the
    /// minute, <c>HH</c> the 24-hour clock and <c>hh</c> the 12-hour. Read under the invariant culture
    /// in every branch, so the same file means the same thing on two machines.
    /// </remarks>
    private static double? ReadTimestamp(string text, string? format)
    {
        if (format is not null)
        {
            return DateTime.TryParseExact(
                text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stated)
                ? (stated - DateTime.UnixEpoch).TotalSeconds
                : null;
        }

        if (ReadNumber(text) is { } seconds)
        {
            return seconds;
        }

        return DateTime.TryParseExact(
            text, IsoTimestamps, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
            ? (iso - DateTime.UnixEpoch).TotalSeconds
            : null;
    }
}
