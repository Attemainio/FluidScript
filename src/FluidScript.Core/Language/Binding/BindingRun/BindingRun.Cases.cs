using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <content>
/// Step 5 once per case: a driver — a <c>let</c> with one value per case — and everything that reads it
/// (<c>D-167</c>, <c>19</c> §Drivers and cases).
/// </content>
/// <remarks>
/// <para>
/// The ordinary evaluation is the design case's: a driver's pending value is its design element, so every
/// curve, <c>let</c> and parameter downstream of it settles exactly as a file without cases would. Each
/// other case is then a second walk of the same topological order over only the values that differ between
/// cases, with the driver's pending value set to that case's element. The walk reuses every lookup the
/// first one used — a curve reads its driver's pending value, a parameter reads the curve's — so nothing
/// beside this file learns that a value can have several.
/// </para>
/// <para>
/// What the walks find is published where <c>D-143</c>'s lists already put a case's value: in
/// <see cref="ParameterValue.Scenarios"/>. A parameter pinned to a curve of a driver is therefore, to
/// sizing and the solvers, a parameter written as a list, and <see cref="ScenarioProjection"/> projects it
/// the same way.
/// </para>
/// </remarks>
internal sealed partial class BindingRun
{
    /// <summary>Every case's expression of a <c>let</c> written as a list, by the <c>let</c>'s id; element <c>i</c> is case <c>i</c>.</summary>
    private readonly Dictionary<ValueId, ImmutableArray<ExpressionSyntax>> _caseExpressions = [];

    /// <summary>The values that differ between cases: the drivers, and everything that reads one.</summary>
    private readonly HashSet<ValueId> _varying = [];

    /// <summary>Each varying value in each case but the design case, which keeps its value on the pending value.</summary>
    private readonly Dictionary<(ValueId Id, int Case), Quantity> _caseValues = [];

    /// <summary>Each varying curve's value in each case but the design case.</summary>
    private readonly Dictionary<(string Curve, int Case), double?> _caseCurveValues = [];

    /// <summary>The case being evaluated by a case walk, or <see langword="null"/> during the design case's.</summary>
    private int? _case;

    /// <summary>The design case's position, with the first standing in when it is not settled (<c>FS1542</c>, <c>FS1543</c>).</summary>
    private int DesignCase => _designScenario is { } named ? Math.Max(0, _scenarios.IndexOf(named.Name)) : 0;

    /// <summary>Declares a <c>let</c> written as a list, one value per case (<c>D-167</c>).</summary>
    /// <param name="name">The binding's name.</param>
    /// <param name="id">The binding's id.</param>
    /// <param name="cases">The list.</param>
    /// <param name="span">The whole <c>let</c>, where a list of the wrong length is reported.</param>
    /// <returns>The expression the design case evaluates.</returns>
    /// <remarks>
    /// A list of the wrong length, or one in a file with no cases, is reported and its design element (or its
    /// last, when the list is short) stands for every case. That differs from a parameter's list, which binds
    /// nothing: a driver bound to nothing would make every curve of it <c>FS1528</c> as well, which is one
    /// mistake reported at every place it reaches.
    /// </remarks>
    private ExpressionSyntax DeclareCases(string name, ValueId id, ScenarioListSyntax cases, TextSpan span)
    {
        var elements = cases.Elements.Select(static element => element.Value).ToImmutableArray();
        var design = DesignCase;

        if (_scenarios.Count == 0)
        {
            Report(BinderDiagnostics.ScenarioListWithoutScenarios, span, ("written", name));
            return elements[0];
        }

        if (elements.Length != _scenarios.Count)
        {
            Report(
                BinderDiagnostics.ScenarioCountMismatch,
                span,
                ("written", name),
                ("given", elements.Length.ToString(CultureInfo.InvariantCulture)),
                ("count", _scenarios.Count.ToString(CultureInfo.InvariantCulture)),
                ("names", string.Join(", ", _scenarios)));
            return elements[Math.Min(design, elements.Length - 1)];
        }

        _caseExpressions[id] = elements;
        return elements[design];
    }

    /// <summary>Settles a language 2 curve's driver: a <c>let</c>, or <c>FS1811</c>.</summary>
    /// <remarks>The clock is settled before this is reached.</remarks>
    private CurveSymbol DriverLet(CurveSymbol curve, string driver)
    {
        if (!parse.Root.Statements.OfType<LetBindingSyntax>().Any(let => string.Equals(let.Name.Text, driver, StringComparison.Ordinal)))
        {
            Report(Language2Diagnostics.CurveDriverNotALet, curve.DeclarationSpan, ("curve", curve.Name), ("driver", driver));
            return curve;
        }

        _graph.AddDependency(new ValueId.Curve(curve.Name), new ValueId.Let(driver));
        return curve with { DriverKind = CurveDriverKind.Let };
    }

    /// <summary>Reads a <c>let</c> as the bare number a curve's first column is written in (<c>19</c> §Drivers and cases).</summary>
    /// <param name="name">The <c>let</c>'s name.</param>
    /// <returns>The value in the unit the <c>let</c> is written in, or <see langword="null"/> when it has none yet.</returns>
    /// <remarks>
    /// The unit written on the <c>let</c> — <c>[-26, 5] C</c> puts the rows in °C, <c>[2, 3] m3/h</c> in m³/h —
    /// because the rows sit beside it and are read against it. A <c>let</c> that writes no single unit (an
    /// expression) is read in its dimension's canonical unit, as a <c>design</c> value is; a bare one as it
    /// stands. This project's reasoning: <c>19</c> says "the driver's unit" and gives only °C as the example.
    /// </remarks>
    private double? LetNumber(string name)
    {
        if (!_bindingsByName.TryGetValue(name, out var slot)
            || !_pending.TryGetValue(slot.Id, out var pending)
            || pending.Value is not { } quantity)
        {
            return null;
        }

        var unit = WrittenUnit(pending.Expression, quantity.Dimension) ?? UnitTable.CanonicalUnitFor(quantity.Dimension);
        return unit is null ? quantity.SiValue : quantity.ValueIn(unit);
    }

    /// <summary>The unit a literal, possibly negated or parenthesised, is written in.</summary>
    private static UnitSymbol? WrittenUnit(ExpressionSyntax expression, Dimension dimension) => expression switch
    {
        QuantityLiteralSyntax literal => UnitTable.Resolve(literal.Unit, dimension),
        UnaryExpressionSyntax unary => WrittenUnit(unary.Operand, dimension),
        ParenthesizedExpressionSyntax parenthesized => WrittenUnit(parenthesized.Inner, dimension),
        _ => null,
    };

    /// <summary>Evaluates every value that differs between cases once in each case but the design case.</summary>
    /// <param name="order">The order the design case was evaluated in.</param>
    /// <remarks>
    /// The design case's values are put back afterwards, so what the model publishes as a parameter's value,
    /// a <c>let</c>'s and a curve's is the design case's, as it is in a file without drivers.
    /// </remarks>
    private void EvaluateCases(ImmutableArray<ValueId> order)
    {
        if (_caseExpressions.Count == 0)
        {
            return;
        }

        MarkVarying(order);

        var design = DesignCase;
        var values = _varying.Where(_pending.ContainsKey).ToDictionary(static id => id, id => _pending[id].Value);
        var curves = _varying.OfType<ValueId.Curve>().ToDictionary(static id => id.Name, id => _curveValues.GetValueOrDefault(id.Name));

        for (var index = 0; index < _scenarios.Count; index++)
        {
            if (index == design)
            {
                continue;
            }

            _case = index;

            foreach (var id in order.Where(_varying.Contains))
            {
                EvaluateInCase(id, index);
            }

            _case = null;

            foreach (var (id, value) in values)
            {
                _pending[id].Value = value;
            }

            foreach (var (name, value) in curves)
            {
                _curveValues[name] = value;
            }
        }
    }

    /// <summary>Finds the values that differ between cases, in the order that lets one pass see every reader after what it reads.</summary>
    private void MarkVarying(ImmutableArray<ValueId> order)
    {
        foreach (var id in order)
        {
            var varies = id switch
            {
                _ when _caseExpressions.ContainsKey(id) => true,
                ValueId.Curve curve => _curvesByName.TryGetValue(curve.Name, out var index) && _curves[index] is { DriverName: { } driver } symbol
                    && symbol.DriverKind switch
                    {
                        CurveDriverKind.Let => _varying.Contains(new ValueId.Let(driver)),
                        CurveDriverKind.Curve => _varying.Contains(new ValueId.Curve(driver)),
                        _ => false,
                    },
                _ => _pending.TryGetValue(id, out var pending) && pending.Dependencies.Overlaps(_varying),
            };

            if (varies)
            {
                _varying.Add(id);
            }
        }
    }

    /// <summary>Evaluates one varying value in one case and keeps what it came to.</summary>
    private void EvaluateInCase(ValueId id, int index)
    {
        if (id is ValueId.Curve curve)
        {
            EvaluateCurve(curve);
            _caseCurveValues[(curve.Name, index)] = _curveValues.GetValueOrDefault(curve.Name);
            return;
        }

        // An element of a parameter's list is the value of its own case and of no other.
        if (!_pending.TryGetValue(id, out var pending) || id is ValueId.ScenarioParameter { Scenario: var slot } && slot != index)
        {
            return;
        }

        var expression = _caseExpressions.TryGetValue(id, out var cases) ? cases[index] : pending.Expression;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var evaluator = new ExpressionEvaluator(
            this,
            parse.Source,
            diagnostics,
            pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);

        pending.Value = null;
        _evaluating = id;
        var result = evaluator.Evaluate(expression);
        _evaluating = null;

        _diagnostics.AddRange(diagnostics.Where(diagnostic => !Reported(diagnostic)));

        if (result is EvaluationResult.Value value)
        {
            Store(pending, value);
        }

        if (pending.Value is { } quantity)
        {
            _caseValues[(id, index)] = quantity;
        }
    }

    /// <summary>An expression bound after evaluation — a setpoint — in every case, when it reads a driver.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="dimension">What a bare number in it means.</param>
    /// <param name="design">Its value in the design case, already evaluated.</param>
    /// <returns>One value per case, or empty when the expression reads nothing that varies.</returns>
    private ImmutableArray<Quantity?> PerCase(ExpressionSyntax expression, Dimension? dimension, Quantity? design)
    {
        if (_varying.Count == 0)
        {
            return [];
        }

        var probe = new ExpressionEvaluator(this, parse.Source, ImmutableArray.CreateBuilder<Diagnostic>());
        probe.Evaluate(expression);

        if (!probe.Dependencies.Overlaps(_varying))
        {
            return [];
        }

        var values = new Quantity?[_scenarios.Count];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = index == DesignCase ? design : InCase(index, () => Value(expression, dimension));
        }

        return [.. values];
    }

    /// <summary>Runs an evaluation with every varying value, curves included, at one case's value.</summary>
    private T InCase<T>(int index, Func<T> evaluate)
    {
        var values = _varying.Where(_pending.ContainsKey).Select(id => (Pending: _pending[id], Value: _pending[id].Value)).ToList();
        var curves = _varying.OfType<ValueId.Curve>().Select(id => (id.Name, Value: _curveValues.GetValueOrDefault(id.Name))).ToList();

        foreach (var (pending, _) in values)
        {
            pending.Value = _caseValues.TryGetValue((pending.Id, index), out var inCase) ? inCase : null;
        }

        foreach (var (name, _) in curves)
        {
            _curveValues[name] = _caseCurveValues.GetValueOrDefault((name, index));
        }

        _case = index;

        try
        {
            return evaluate();
        }
        finally
        {
            _case = null;

            foreach (var (pending, value) in values)
            {
                pending.Value = value;
            }

            foreach (var (name, value) in curves)
            {
                _curveValues[name] = value;
            }
        }
    }

    /// <summary>Whether the same code has already been said at the same place in the same words.</summary>
    private bool Reported(Diagnostic diagnostic) =>
        _diagnostics.Any(existing =>
            existing.Code == diagnostic.Code
            && existing.Span == diagnostic.Span
            && string.Equals(existing.Message, diagnostic.Message, StringComparison.Ordinal));

    /// <summary>A parameter that follows a driver, with its value in every case beside the design case's.</summary>
    /// <param name="component">The component.</param>
    /// <param name="canonical">The parameter's key.</param>
    /// <param name="bound">The parameter as the design case bound it.</param>
    /// <param name="id">The parameter's id.</param>
    /// <returns>The parameter, carrying one element per case as a list written on it would.</returns>
    private ParameterValue WithCases(ComponentSymbol component, string canonical, ParameterValue bound, ValueId id)
    {
        var design = DesignCase;
        var elements = ImmutableArray.CreateBuilder<ParameterValue>(_scenarios.Count);

        for (var index = 0; index < _scenarios.Count; index++)
        {
            if (index == design)
            {
                elements.Add(bound);
                continue;
            }

            var value = _caseValues.TryGetValue((id, index), out var inCase) ? inCase : (Quantity?)null;
            elements.Add(bound with { Value = value });

            if (value is { } settled && _pending.TryGetValue(id, out var pending))
            {
                CheckRoleSign(component, canonical, settled, pending.Span);
            }
        }

        return bound with { Scenarios = elements.MoveToImmutable() };
    }
}
