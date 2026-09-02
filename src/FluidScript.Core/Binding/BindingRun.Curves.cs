using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <content>
/// Binding step 0b: collect the curves and the design point (<c>D-57</c>, <c>D-58</c>, <c>D-59</c>,
/// <c>D-60</c>).
/// </content>
/// <remarks>
/// <para>
/// Curves are file-wide, which is the one place <c>D-52</c> does not apply — a curve is read by every
/// circuit that names it — so they are collected from the statement list directly rather than from a
/// circuit's block. The design point is file-wide for the same reason <c>project</c> is: an outdoor
/// temperature is a property of the site.
/// </para>
/// <para>
/// The evaluation itself is not here. A curve is a node of the dependency graph like any other value,
/// so it is ordered and evaluated by step 5, which is what makes a cycle among curves the same
/// <c>FS1402</c> a cycle among <c>let</c> bindings already was.
/// </para>
/// </remarks>
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

    private readonly List<CurveSymbol> _curves = [];
    private readonly Dictionary<string, int> _curvesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DesignValue> _design = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double?> _curveValues = new(StringComparer.Ordinal);

    // ---- step 0b: the curves, their rows, and the design point ------------------------------------

    private void CollectCurves()
    {
        var drafts = new List<CurveDraft>();
        CurveDraft? current = null;

        foreach (var statement in parse.Root.Statements)
        {
            switch (statement)
            {
                case CurveHeaderSyntax header:
                    current = new CurveDraft(header, []);
                    drafts.Add(current);
                    break;

                case CurveRowSyntax row:
                    // A row with no header above it is FS1115 from the parser, and there is nothing
                    // here to put it in.
                    current?.Rows.Add(row);
                    break;

                case DesignDirectiveSyntax design:
                    DeclareDesign(design);
                    break;

                // Nothing else closes a curve section: it is file-wide, so the first circuit is the
                // end of the region curves may be declared in.
                case CircuitHeaderSyntax:
                    current = null;
                    break;

                default:
                    break;
            }
        }

        foreach (var draft in drafts)
        {
            DeclareCurve(draft);
        }

        // A second pass, because a curve may name a curve declared below it and the design point may
        // be written after the curve that reads it.
        for (var i = 0; i < _curves.Count; i++)
        {
            _curves[i] = ResolveDriver(_curves[i]);
        }
    }

    private void DeclareDesign(DesignDirectiveSyntax design)
    {
        foreach (var argument in design.Arguments)
        {
            var written = argument.Name.Text;
            var role = ScheduleRoleRegistry.Resolve(written);

            // Keyed by the role rather than the spelling, which is the whole of `D-59`: `design
            // tout=-26` and `design outdoor=-26` are the same design point, and a curve driven by
            // either name finds it.
            var key = role?.CanonicalName ?? written;

            if (_design.TryGetValue(key, out var existing))
            {
                Report(
                    BinderDiagnostics.DuplicateBinding,
                    argument.Span,
                    ("name", written),
                    ("line", LineOf(existing.Span)));
                continue;
            }

            var id = new ValueId.Design(key);
            _graph.Add(id);
            _pending[id] = new PendingValue(argument.Value, id, argument.Span, null)
            {
                DesignRole = role,
                IsDesign = true,
            };

            _design[key] = new DesignValue(written, role, null, null, argument.Span);
        }
    }

    private void DeclareCurve(CurveDraft draft)
    {
        var header = draft.Header;
        var name = header.Name.Text;

        if (_curvesByName.TryGetValue(name, out var duplicate))
        {
            Report(
                BinderDiagnostics.DuplicateComponent,
                header.Span,
                ("name", name),
                ("line", LineOf(_curves[duplicate].DeclarationSpan)));
            return;
        }

        var extrapolated = false;

        foreach (var modifier in header.Modifiers)
        {
            if (string.Equals(modifier.Text, "extrapolated", StringComparison.Ordinal))
            {
                extrapolated = true;
                continue;
            }

            ReportUnknownCurveWord(modifier.Text, modifier.Span);
        }

        string? format = null;

        foreach (var argument in header.Arguments)
        {
            if (!string.Equals(argument.Name.Text, "format", StringComparison.Ordinal))
            {
                ReportUnknownCurveWord(argument.Name.Text, argument.Span);
                continue;
            }

            // A `format=` that is not a quoted string leaves the curve reading ISO 8601, and each row
            // that is not says so on its own line. One code per unreadable row is the honest report:
            // the rows are what failed.
            format = (argument.Value as StringLiteralSyntax)?.Value;
        }

        var driver = header.Driver?.Text;
        var isTime = string.Equals(driver, "time", StringComparison.Ordinal);

        var symbol = new CurveSymbol
        {
            Name = name,
            DriverName = driver,
            DriverKind = CurveDriverKind.Unresolved,
            IsExtrapolated = extrapolated,
            TimeFormat = format,
            Points = ReadRows(draft, name, isTime, format),
            DeclarationSpan = header.Span,
        };

        if (symbol.Points.Length < 2)
        {
            Report(BinderDiagnostics.CurveTooShort, header.Span, ("curve", name));
        }

        _curvesByName[name] = _curves.Count;
        _curves.Add(symbol);
        _graph.Add(new ValueId.Curve(name));
    }

    private void ReportUnknownCurveWord(string written, TextSpan span) =>
        Report(
            BinderDiagnostics.UnknownParameter,
            span,
            ("kind", "curve"),
            ("parameter", written),
            ("available", "extrapolated, format"));

    /// <summary>Reads every row of one curve into a sorted table.</summary>
    /// <remarks>
    /// Rows written out of order are sorted here rather than reported: a weather file is not obliged
    /// to arrive monotonic, and <see cref="CurveSymbol.Evaluate"/> needs the order, not the user.
    /// </remarks>
    private ImmutableArray<CurvePoint> ReadRows(CurveDraft draft, string name, bool isTime, string? format)
    {
        var read = new List<CurvePoint>();

        foreach (var row in draft.Rows)
        {
            if (ReadRow(row, isTime, format) is { } point)
            {
                read.Add(point);
                continue;
            }

            Report(ParserDiagnostics.MalformedCurveRow, row.Span);
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

    /// <summary>Settles what a curve's second position names, and wires the graph edge it implies.</summary>
    /// <remarks>
    /// The role is resolved whatever the driver turns out to be, because it is the design point's key
    /// rather than the driver's kind: <c>curve heating outdoor</c> is driven by the curve
    /// <c>outdoor</c>, and <c>design tout=-26</c> still short-circuits it, which is exactly
    /// <c>D-58</c>'s worked example.
    /// </remarks>
    private CurveSymbol ResolveDriver(CurveSymbol curve)
    {
        if (curve.DriverName is not { } driver)
        {
            return curve;
        }

        // Checked before the registry, so no similarity match can turn the clock into a driver.
        if (string.Equals(driver, "time", StringComparison.Ordinal))
        {
            return curve with { DriverKind = CurveDriverKind.Time };
        }

        var role = ScheduleRoleRegistry.Resolve(driver);
        var key = role?.CanonicalName ?? driver;

        var kind = _curvesByName.ContainsKey(driver) && !string.Equals(driver, curve.Name, StringComparison.Ordinal)
            ? CurveDriverKind.Curve
            : role is not null
                ? CurveDriverKind.Role
                : _design.ContainsKey(key)
                    ? CurveDriverKind.DesignOnly
                    : CurveDriverKind.Unresolved;

        if (kind == CurveDriverKind.Unresolved)
        {
            // `D-59` says an unregistered name is not an error, and `FS1527` says a driver naming
            // nothing is. Both hold, because a driver has to supply a number: an unregistered name
            // with a `design` value behind it works, and this is the name with nothing behind it.
            Report(
                BinderDiagnostics.UnknownCurveDriver,
                curve.DeclarationSpan,
                ("driver", driver),
                ("curve", curve.Name));

            return curve;
        }

        var id = new ValueId.Curve(curve.Name);

        if (kind == CurveDriverKind.Curve)
        {
            _graph.AddDependency(id, new ValueId.Curve(driver));
        }

        if (_design.ContainsKey(key))
        {
            _graph.AddDependency(id, new ValueId.Design(key));
        }

        return curve with { DriverKind = kind, DriverRole = role };
    }

    // ---- step 5, for a curve: read it at the design point ------------------------------------------

    /// <summary>Evaluates one curve at its driver's design value.</summary>
    /// <remarks>
    /// Called from step 5's topological walk, so a curve that drives another has already been read.
    /// A curve with no value here is not a failure: in a dynamic circuit it is a live function of
    /// time, and only a static circuit reading it is <c>FS1528</c>.
    /// </remarks>
    private void EvaluateCurve(ValueId.Curve id)
    {
        var curve = _curves[_curvesByName[id.Name]];

        _curveValues[id.Name] = curve.Points.IsEmpty || DriverValue(curve) is not { } x
            ? null
            : curve.Evaluate(x);
    }

    /// <summary>Finds the point a curve is read at (<c>D-58</c>).</summary>
    /// <returns>
    /// The driver's design value if it has one, otherwise the driving curve's own value, otherwise
    /// <see langword="null"/> — which is the clock, and the answer only a solve in time supplies.
    /// </returns>
    /// <remarks>
    /// The design value is tried <em>first</em>, and that order is the decision: with
    /// <c>design tout=-26</c> the chain <c>time → outdoor → heating</c> is not walked at all, which is
    /// what lets a file carrying a full year of weather data still solve statically.
    /// </remarks>
    private double? DriverValue(CurveSymbol curve)
    {
        if (curve.DriverName is not { } driver)
        {
            return null;
        }

        if (DesignNumber(curve.DriverRole?.CanonicalName ?? driver) is { } stated)
        {
            return stated;
        }

        return curve.DriverKind == CurveDriverKind.Curve
            ? _curveValues.GetValueOrDefault(driver)
            : null;
    }

    /// <summary>Reads one design value as the bare number a curve's table is written in.</summary>
    /// <remarks>
    /// In the role's canonical unit, never in SI, which is what makes <c>design tout=-26</c> and
    /// <c>design tout=-26 C</c> pick the same row of a table whose <c>x</c> column says −26.
    /// </remarks>
    private double? DesignNumber(string key)
    {
        if (!_design.TryGetValue(key, out var entry)
            || !_pending.TryGetValue(new ValueId.Design(key), out var pending)
            || pending.Value is not { } quantity)
        {
            return null;
        }

        return entry.Role?.Dimension is { } dimension && UnitTable.CanonicalUnitFor(dimension) is { } unit
            ? quantity.ValueIn(unit)
            : quantity.SiValue;
    }

    /// <summary>Reports what reading a curve cost, once every value has been evaluated.</summary>
    /// <remarks>
    /// <para>
    /// Two outcomes, and the circuit's mode picks between them (<c>D-58</c>). A <strong>static</strong>
    /// circuit reading a curve with no design value is <c>FS1528</c>: guessing zero, or the table's
    /// first row, would put a number in front of an engineer that nothing chose. A
    /// <strong>dynamic</strong> circuit reading one is not an error at all — the design value sized the
    /// component and the curve is a live function of time — so the reference is recorded as deferred
    /// for the transient stage to read again at each step.
    /// </para>
    /// <para>
    /// It runs after evaluation rather than inside it because the reporting site is the reference, not
    /// the curve: one unreadable curve read from four parameters is four places to fix.
    /// </para>
    /// </remarks>
    private void ReviewCurveReferences()
    {
        foreach (var pending in _pending.Values)
        {
            var curves = pending.Dependencies.OfType<ValueId.Curve>().ToArray();

            if (curves.Length == 0)
            {
                continue;
            }

            if (ModeOf(pending.Id) == FluidMode.Dynamic)
            {
                if (!_deferred.Any(deferred => deferred.Target == pending.Id))
                {
                    _deferred.Add(new DeferredExpression(
                        pending.Expression, pending.Id, pending.Value, pending.Dependencies));
                }

                continue;
            }

            foreach (var curve in curves.OrderBy(static curve => curve.Name, StringComparer.Ordinal))
            {
                if (_curveValues.GetValueOrDefault(curve.Name) is not null)
                {
                    continue;
                }

                Report(
                    BinderDiagnostics.CurveWithoutDesignPoint,
                    pending.Span,
                    ("curve", curve.Name),
                    ("driver", _curves[_curvesByName[curve.Name]].DriverName ?? curve.Name));
            }
        }
    }

    /// <summary>Finds the solve mode of the circuit a value was written in.</summary>
    /// <returns>Static when nothing places the value, which is the language's own default.</returns>
    private FluidMode ModeOf(ValueId id)
    {
        var circuit = id switch
        {
            ValueId.ComponentParameter parameter
                when _componentsByName.TryGetValue(parameter.Component, out var slot) =>
                _components[slot.Index].CircuitName,
            ValueId.Let let => _bindingCircuits.GetValueOrDefault(let.Name),
            _ => null,
        };

        return _circuits
            .FirstOrDefault(candidate => string.Equals(candidate.Name, circuit, StringComparison.Ordinal))
            ?.Mode ?? FluidMode.Static;
    }

    /// <summary>Writes each design value's evaluated result back, for the model to carry.</summary>
    private ImmutableDictionary<string, DesignValue> PublishDesign()
    {
        var published = ImmutableDictionary.CreateBuilder<string, DesignValue>(StringComparer.Ordinal);

        foreach (var (key, entry) in _design)
        {
            _pending.TryGetValue(new ValueId.Design(key), out var pending);

            published[key] = entry with { Value = pending?.Value, Number = DesignNumber(key) };
        }

        return published.ToImmutable();
    }

    private sealed record CurveDraft(CurveHeaderSyntax Header, List<CurveRowSyntax> Rows);
}
