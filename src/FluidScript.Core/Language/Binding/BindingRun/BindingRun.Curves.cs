using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

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
    private readonly List<CurveSymbol> _curves = [];
    private readonly Dictionary<string, int> _curvesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DesignValue> _design = new(StringComparer.Ordinal);

    /// <summary>The scenario names in the order written, which is what an array binds to (<c>D-143</c>).</summary>
    private readonly List<string> _scenarios = [];

    /// <summary>The scenario <c>design</c> named, with the span to report an unknown one against.</summary>
    private (string Name, TextSpan Span)? _designScenario;

    /// <summary>The <c>scenarios</c> line's span, which is where a missing <c>design</c> is reported.</summary>
    private TextSpan? _scenarioSpan;
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

                case ScenariosDirectiveSyntax scenarios:
                    DeclareScenarios(scenarios);
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
        var formatIsBad = false;

        foreach (var argument in header.Arguments)
        {
            if (!string.Equals(argument.Name.Text, "format", StringComparison.Ordinal))
            {
                ReportUnknownCurveWord(argument.Name.Text, argument.Span);
                continue;
            }

            // `D-60`: validated here, on the header, so one mistake is one diagnostic and not one per
            // row. A bad format leaves the rows unread and unreported: they are not the fault.
            format = (argument.Value as StringLiteralSyntax)?.Value;
            var reason = format is null
                ? "it is not a quoted string"
                : !format.Contains('d')
                    ? "it has no day (d)"
                    : !format.Contains('M')
                        ? "it has no month (M)"
                        : null;

            if (reason is not null)
            {
                formatIsBad = true;
                Report(BinderDiagnostics.CurveFormatInvalid, argument.Span, ("curve", name), ("reason", reason));
            }
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
            Points = formatIsBad && isTime ? [] : ReadRows(draft, name, isTime, format),
            DeclarationSpan = header.Span,
        };

        if (symbol.Points.Length < 2 && !(formatIsBad && isTime))
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

    /// <summary>How many unreadable rows of one curve are marked where they are before the rest are counted (<c>FS1535</c>).</summary>
    private const int UnreadableRowsShown = 5;

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

        // Language 2 has no roles and no `design`: a driver is a `let` or the clock (`D-167`).
        if (parse.Language == 2)
        {
            return DriverLet(curve, driver);
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

        if (curve.DriverKind == CurveDriverKind.Let)
        {
            return LetNumber(driver);
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

            // Language 2 has no dynamic circuit outside a run (`D-169`), and any run may hand a driver to a curve
            // of time; so every reader of a curve that has its value is held for the clock too, as a dynamic
            // circuit's is in language 1. One with no value is FS1528 below, as in a static circuit.
            var followable = parse.Language == 2
                && curves.All(curve => CurveValueSeenBy(pending.Id, curve.Name) is not null);

            if (ModeOf(pending.Id) == FluidMode.Dynamic || followable)
            {
                if (_deferredTargets.Add(pending.Id))
                {
                    _deferred.Add(new DeferredExpression(
                        pending.Expression, pending.Id, pending.Value, pending.Dependencies) { Source = parse.Source });
                }

                // `D-149`: a curve that runs on the clock is read at start + t, so a run needs the start.
                // A warning and not an error, because the design solve does not read the clock.
                if (parse.Language != 2
                    && _project.Start is null
                    && curves.Select(curve => Clocked(curve.Name)).FirstOrDefault(static clocked => clocked is not null) is { } clockedCurve)
                {
                    Report(BinderDiagnostics.ClockWithoutStart, pending.Span, ("curve", clockedCurve));
                }

                continue;
            }

            foreach (var curve in curves.OrderBy(static curve => curve.Name, StringComparer.Ordinal))
            {
                // As this reader saw it: a component's own `sized_at` positions the curve for its
                // parameters even when the file states no `design` at all (`D-94`).
                if (CurveValueSeenBy(pending.Id, curve.Name) is not null || _unletCurves.Contains(curve.Name))
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

    /// <summary>Reports a <c>start=</c> that no clock reads (<c>FS1547</c>).</summary>
    private void ReviewStart()
    {
        if (_startSpan is { } span && _project.Start is not null
            && !_circuits.Any(static circuit => circuit.Mode == FluidMode.Dynamic))
        {
            Report(BinderDiagnostics.StartWithoutClock, span);
        }
    }

    /// <summary>The curve at the root of a chain when that root is the clock.</summary>
    /// <param name="name">A curve's name.</param>
    /// <returns>The name of the curve driven by <c>time</c> that the chain reaches, or <see langword="null"/> when it ends at a role or a design value.</returns>
    private string? Clocked(string name)
    {
        for (var depth = 0; depth <= _curves.Count && _curvesByName.TryGetValue(name, out var index); depth++)
        {
            var curve = _curves[index];

            switch (curve.DriverKind)
            {
                case CurveDriverKind.Time:
                    return curve.Name;
                case CurveDriverKind.Curve when curve.DriverName is { } driver:
                    name = driver;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private sealed record CurveDraft(CurveHeaderSyntax Header, List<CurveRowSyntax> Rows);
}
