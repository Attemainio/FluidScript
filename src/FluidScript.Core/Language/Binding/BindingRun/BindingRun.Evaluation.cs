using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- steps 4-5: the dependency graph, then evaluation ---------------------------------------

    private void Evaluate()
    {
        // Dependencies are discovered by evaluating, and evaluation needs the order dependencies give.
        // The way out is that a first pass records edges without using any value: an expression that
        // reads an unevaluated binding sees no value and reports nothing, because nothing is asked of
        // it yet.
        foreach (var pending in _pending.Values)
        {
            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                ImmutableArray.CreateBuilder<Diagnostic>(),
                pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);
            _evaluating = pending.Id;
            evaluator.Evaluate(pending.Expression);
            var dependencies = evaluator.Dependencies;

            // A `let` written per case reads what every one of its cases reads (`D-167`).
            foreach (var other in _caseExpressions.GetValueOrDefault(pending.Id, []))
            {
                evaluator.Evaluate(other);
                dependencies = dependencies.Union(evaluator.Dependencies);
            }

            _evaluating = null;

            // Kept on the pending value as well, so a later pass can ask what an expression read
            // without evaluating it a third time. That is how a curve reference is found again at the
            // site that made it, which is where reading one has to be reported.
            pending.Dependencies = dependencies;

            foreach (var dependency in dependencies)
            {
                _graph.AddDependency(pending.Id, dependency);
            }
        }

        switch (_graph.TopologicalOrder())
        {
            case OrderResult.Cyclic cyclic:
                ReportCycle(cyclic);
                return;

            case OrderResult.Ordered ordered:
                EvaluateInOrder(ordered.Order);
                return;
        }
    }

    /// <summary>Step 5: evaluates every value in the order the graph gave, curves included.</summary>
    private void EvaluateInOrder(ImmutableArray<ValueId> order)
    {
        _order = order;

        foreach (var id in order)
        {
            // A curve is evaluated here rather than in step 0b, because the order it needs is the one
            // the graph gives: a curve driving another has already been read by the time this reaches
            // the second (`D-57`).
            if (id is ValueId.Curve curve)
            {
                EvaluateCurve(curve);
                continue;
            }

            if (!_pending.TryGetValue(id, out var pending))
            {
                continue;
            }

            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                _diagnostics,
                pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);

            // Named while it reads, so a curve reference can tell whose parameter is asking and read
            // the curve at that component's own sizing point (`D-94`).
            _evaluating = pending.Id;
            var result = evaluator.Evaluate(pending.Expression);
            _evaluating = null;

            switch (result)
            {
                case EvaluationResult.Value value:
                    Store(pending, value);
                    break;

                case EvaluationResult.Deferred deferred:
                    _deferred.Add(new DeferredExpression(pending.Expression, id, null, deferred.Dependencies) { Source = parse.Source });
                    _deferredTargets.Add(id);
                    break;

                default:
                    break;
            }
        }

        EvaluateCases(order);
        Publish();
    }

    private void Store(PendingValue pending, EvaluationResult.Value value)
    {
        var quantity = value.Quantity;

        // A design value is not a parameter, and its driver's role is what checks it (`D-59`). The
        // role's dimension makes `design tout=-26` and `design tout=-26 C` the same point, and
        // `design tout=3 bar` a mismatch rather than a silent reinterpretation; a role with no
        // dimension takes its value bare and checks nothing.
        if (pending.IsDesign)
        {
            if (pending.DesignRole?.Dimension is { } dimension)
            {
                if (value.IsBare)
                {
                    quantity = Quantity.FromBareNumber(quantity.SiValue, dimension);
                }
                else if (quantity.Dimension != dimension)
                {
                    Report(
                        BinderDiagnostics.ParameterDimensionMismatch,
                        pending.Span,
                        ("parameter", pending.DesignRole.CanonicalName),
                        ("expected", dimension.Name.ToLowerInvariant()),
                        ("value", parse.Source.ToString(pending.Expression.Span).Trim()),
                        ("actual", quantity.Dimension.Name.ToLowerInvariant()));
                    return;
                }
            }

            pending.Value = quantity;
            return;
        }

        if (pending.Target is { } target)
        {
            // A bare number takes the parameter's canonical unit (`D-14`), which is what makes
            // `power=30` thirty kilowatts and `power=30 kW` the same quantity. Reinterpreting here
            // rather than in the evaluator keeps the evaluator ignorant of what it is being assigned
            // to, so the same expression means the same thing wherever it is written.
            if (value.IsBare && target.Info.ValueKind == ParameterValueKind.Quantity)
            {
                quantity = Quantity.FromBareNumber(quantity.SiValue, target.Info.Dimension);
            }
            else if (!Quantity.TryAssign(quantity, target.Info.Dimension, out quantity))
            {
                Report(
                    BinderDiagnostics.ParameterDimensionMismatch,
                    pending.Span,
                    ("parameter", target.Info.Name),
                    ("expected", BinderDiagnostics.Expected(target.Info.Dimension)),
                    ("value", parse.Source.ToString(pending.Expression.Span).Trim()),
                    ("actual", quantity.Dimension.Name.ToLowerInvariant()));
                return;
            }

            quantity = HeldToCapacity(pending, quantity);
            CheckRange(target, quantity, pending.Span);
        }

        pending.Value = quantity;
    }

    private void CheckRange(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        // Order matters, and it is the order of severity. A value outside a hard bound or with the
        // wrong sign is already reported as an error, and adding "and it is outside the usual range"
        // underneath would be two diagnostics for one mistake, the second of them redundant.
        if (CheckValidity(target, quantity, span) || CheckSign(target, quantity, span))
        {
            return;
        }

        if (target.Info.UsualRange is not { } range || range.Contains(quantity.SiValue))
        {
            return;
        }

        var unit = UnitTable.CanonicalUnitFor(target.Info.Dimension);
        var shown = unit is null ? quantity.SiValue : quantity.ValueIn(unit);
        var low = unit is null ? range.Min : Quantity.FromSi(range.Min, target.Info.Dimension).ValueIn(unit);
        var high = unit is null ? range.Max : Quantity.FromSi(range.Max, target.Info.Dimension).ValueIn(unit);

        Report(
            BinderDiagnostics.ValueOutsideUsualRange,
            span,
            ("parameter", target.Info.Name),
            ("value", Format(shown, unit?.Text)),
            ("low", Format(low, null)),
            ("high", Format(high, unit?.Text)));
    }

    /// <summary>Reports a value outside the range in which its parameter means anything.</summary>
    /// <param name="target">The component and parameter the value was written for.</param>
    /// <param name="quantity">The evaluated value.</param>
    /// <param name="span">Where the assignment sits in the source.</param>
    /// <returns><see langword="true"/> when a diagnostic was reported.</returns>
    /// <remarks>
    /// Which code is raised comes from the registry rather than from a branch here, so <c>FS2105</c>,
    /// <c>FS2108</c>, <c>FS2114</c> and <c>FS2115</c> are one check site and four rows.
    /// </remarks>
    private bool CheckValidity(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        if (target.Info.Validity is not { } validity)
        {
            return false;
        }

        var value = quantity.SiValue;

        if (validity.Range.Contains(value) && (!validity.RequiresWholeNumber || double.IsInteger(value)))
        {
            return false;
        }

        Report(
            validity.Descriptor,
            span,
            ("name", target.Owner),
            ("parameter", target.Info.Name),
            ("value", Format(value, null)),
            ("low", Format(validity.Range.Min, null)),
            ("high", Format(validity.Range.Max, null)));

        return true;
    }

    /// <summary>Reports a negative value for a parameter whose declared range starts at or above zero.</summary>
    /// <param name="target">The component and parameter the value was written for.</param>
    /// <param name="quantity">The evaluated value.</param>
    /// <param name="span">Where the assignment sits in the source.</param>
    /// <returns><see langword="true"/> when a diagnostic was reported.</returns>
    /// <remarks>
    /// <para>
    /// The usual range doubles as the declaration of sign, which is why <c>power</c> (-100 to 100 kW)
    /// takes a negative and <c>dt</c> (0.1 to 200 K) does not. A duty's direction is <c>power</c>'s
    /// sign and nothing else's, so <c>power=-70 dt=20</c> is a cooler and <c>dt=-20</c> is an error.
    /// </para>
    /// <para>
    /// <strong>Absolute temperatures are exempt, and the exemption is not a convenience.</strong> A
    /// temperature parameter's range is stated in °C and held in K, so its lower bound is 223.15 and
    /// every ordinary value is positive; a value that did reach below zero would be below absolute
    /// zero, and "t cannot be negative" is the wrong sentence for it when <c>t=-50</c> is legal.
    /// <c>FS1306</c> reports that case as the out-of-range value it is.
    /// </para>
    /// </remarks>
    private bool CheckSign(ParameterTarget target, Quantity quantity, TextSpan span)
    {
        if (quantity.SiValue >= 0
            || target.Info.Dimension == Dimension.Temperature
            || target.Info.UsualRange is not { Min: >= 0 })
        {
            return false;
        }

        Report(BinderDiagnostics.NegativeValue, span, ("parameter", target.Info.Name));

        return true;
    }

    private void ReportCycle(OrderResult.Cyclic cyclic)
    {
        var first = cyclic.Cycle[0];
        var span = _pending.TryGetValue(first, out var pending) ? pending.Span : new TextSpan(0, 0);

        Report(
            BinderDiagnostics.CyclicDependency,
            span,
            ("name", first.ToString()!),
            ("cycle", string.Join(" → ", cyclic.Cycle)));
    }

    private void Publish()
    {
        foreach (var (name, slot) in _bindingsByName)
        {
            _pending.TryGetValue(slot.Id, out var pending);
            _bindings.Add(new BindingSymbol(
                name,
                slot.Declaration.Value,
                slot.Id,
                pending?.Value,
                slot.Declaration.Span,
                pending?.Value?.Dimension ?? DimensionOf(slot.Declaration.Value, [name])));
        }

        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];
            var parameters = component.Parameters.ToBuilder();

            foreach (var (canonical, value) in component.Parameters)
            {
                var bound = value;
                var id = new ValueId.ComponentParameter(component.Name, canonical);

                if (_pending.TryGetValue(id, out var pending) && pending.Value is { } quantity)
                {
                    bound = bound with
                    {
                        Value = quantity,
                        Basis = SizingBasis(component.Name, pending),
                    };
                    CheckRoleSign(component, canonical, quantity, pending.Span);
                }

                // The other cases of a list (`D-143`). The design case's element shares the id above,
                // so it is filled from that same evaluation rather than a second one -- one expression
                // with two homes, and a projection that found the design slot empty would read the
                // case the file operates at as unstated.
                if (!bound.Scenarios.IsEmpty)
                {
                    var design = Math.Max(0, _scenarios.IndexOf(_designScenario?.Name ?? string.Empty));
                    var elements = bound.Scenarios.ToBuilder();

                    for (var slot = 0; slot < elements.Count; slot++)
                    {
                        if (slot == design)
                        {
                            if (bound.Value is { } designed)
                            {
                                elements[slot] = elements[slot] with
                                {
                                    Value = designed,
                                    Basis = pending is null ? elements[slot].Basis : SizingBasis(component.Name, pending),
                                };
                            }

                            continue;
                        }

                        var element = new ValueId.ScenarioParameter(component.Name, canonical, slot);

                        // An element that reads a driver was evaluated again in its own case (`D-167`).
                        if (_pending.TryGetValue(element, out var each)
                            && (_caseValues.TryGetValue((element, slot), out var inCase) ? inCase : each.Value) is { } settled)
                        {
                            elements[slot] = elements[slot] with { Value = settled };

                            // The design element was checked above under the parameter's own id; every
                            // other case is checked here, on its own element's span (`C-120`).
                            CheckRoleSign(component, canonical, settled, each.Span);
                        }
                    }

                    bound = bound with { Scenarios = elements.ToImmutable() };
                }
                else if (_varying.Contains(id))
                {
                    bound = WithCases(component, canonical, bound, id);
                }

                if (!ReferenceEquals(bound, value))
                {
                    parameters[canonical] = bound;
                }
            }

            _components[i] = component with
            {
                Parameters = parameters.ToImmutable(),
                SizingPoint = PublishSizingPoint(component.Name),
                Capacities = PublishCapacities(component.Name),
            };
        }
    }

    /// <summary>Reports a negative <c>power</c> on a role spelling, where the word already carries the sign.</summary>
    /// <remarks>
    /// Checked at publication rather than in <see cref="CheckRange"/> because the range check knows the
    /// parameter and not the word the component was declared with; the lowering that applies the
    /// magnitude (<c>D-91</c>) reads <see cref="ComponentSymbol.WrittenKind"/> the same way.
    /// </remarks>
    private void CheckRoleSign(ComponentSymbol component, string canonical, Quantity quantity, TextSpan span)
    {
        if (!string.Equals(canonical, "power", StringComparison.Ordinal) || quantity.SiValue >= 0)
        {
            return;
        }

        var written = NameResolution.Normalize(component.WrittenKind);

        if (written is not ("load" or "cooler" or "radiator" or "chiller" or "heater" or "boiler"))
        {
            return;
        }

        var unit = UnitTable.CanonicalUnitFor(quantity.Dimension);
        var shown = unit is null ? quantity.SiValue : quantity.ValueIn(unit);

        Report(
            BinderDiagnostics.SignedRoleCapacity,
            span,
            ("component", component.Name),
            ("kind", written),
            ("value", Format(shown, unit?.Text)),
            ("magnitude", Format(Math.Abs(shown), unit?.Text)));
    }
}
