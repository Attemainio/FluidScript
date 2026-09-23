using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- the scope an expression is evaluated against -------------------------------------------

    public ScopeLookup Lookup(ReferenceSyntax reference)
    {
        var head = reference.Head.Token.Text;

        if (reference.Parts.IsEmpty)
        {
            if (!_bindingsByName.TryGetValue(head, out var binding))
            {
                // A curve reference is an ordinary value source, which is the whole reason the feature
                // costs so little here: it resolves exactly as a `let` does and yields a *bare*
                // number, so `D-14`'s rule reinterprets it in the target parameter's canonical unit at
                // assignment. That is what lets one curve drive a power, a percentage and a
                // temperature without being told which.
                if (_curvesByName.ContainsKey(head))
                {
                    var curve = new ValueId.Curve(head);

                    return CurveValueFor(head) is { } y
                        ? new ScopeLookup.Value(
                            Quantity.FromSi(y, Dimension.Dimensionless), IsBare: true, curve)
                        : new ScopeLookup.Deferred(curve);
                }

                return new ScopeLookup.UnknownName(ClosestName(head));
            }

            return _pending.TryGetValue(binding.Id, out var pending) && pending.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, binding.Id)
                : new ScopeLookup.Deferred(binding.Id);
        }

        var written = reference.PropertyPath();

        if (!_componentsByName.TryGetValue(head, out var slot))
        {
            return new ScopeLookup.UnknownName(ClosestName(head));
        }

        var component = _components[slot.Index];
        var kind = component.Kind;
        if (kind is null)
        {
            return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, written));
        }

        // Through the kind rather than against `Properties` directly, so an indexed family member --
        // a tank's `layer[3].t`, `in[2].t` -- resolves like the fixed names beside it. The old
        // spellings resolve too; `ReviewLegacyReferences` is what says so, once per site.
        if (kind.ResolveProperty(written) is not { } property)
        {
            return new ScopeLookup.UnknownProperty(kind.Keyword, [.. kind.ReadableNames]);
        }

        // A parameter the user stated is readable at once, whatever the property's availability says:
        // `14`'s table reads "declared parameters: always, immediately", and availability describes
        // where the value comes from when nobody stated it. Reading the pending value rather than the
        // symbol's is what makes it work during evaluation, before anything has been published. The
        // stated value is keyed by the *parameter's* key, which `D-120` lets differ from the
        // property's: `in[2].t` is the parameter `in2` when stated and the property `t_in2` when solved.
        if (StatedParameterKey(kind, written) is { } key && component.Parameters.ContainsKey(key))
        {
            var parameterId = new ValueId.ComponentParameter(head, key);

            return _pending.TryGetValue(parameterId, out var stated) && stated.Value is { } value
                ? new ScopeLookup.Value(value, IsBare: false, parameterId)
                : new ScopeLookup.Deferred(parameterId);
        }

        // Sized or solved, and nobody stated it: this is the deferral `14`'s two-phase evaluation
        // exists for, not an error.
        return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, property.Key));
    }

    /// <summary>
    /// The dimension an expression has, without evaluating it (<c>U-5</c>): a deferred <c>let</c> has no value
    /// until the solve, but <c>1.2*HE1.dp</c> is a pressure difference as soon as <c>HE1</c>'s kind is known,
    /// and completion after <c>dp=</c> should offer it while completion after <c>power=</c> should not.
    /// </summary>
    /// <param name="expression">The expression to type.</param>
    /// <param name="visiting">The bindings on the path here, so a cycle types as unknown rather than recursing.</param>
    /// <returns>
    /// The dimension, or <see langword="null"/> when the expression does not say: a bare number alone, a curve,
    /// a call, or a reference nothing resolves. A bare number beside a dimensioned operand is dimensionless,
    /// so <c>2*HE1.dp</c> types; a curve is unknown, so <c>heating*2</c> does not -- the two kinds of "no
    /// dimension" are kept apart, and only the first is treated as a number.
    /// </returns>
    private Dimension? DimensionOf(ExpressionSyntax expression, HashSet<string> visiting)
    {
        var (known, dimension) = Type(expression, visiting);
        return known && dimension.IsNamed && dimension.Name != "Dimensionless" ? dimension : known && !dimension.IsNamed ? dimension : null;
    }

    /// <summary>The typing behind <see cref="DimensionOf(ExpressionSyntax, HashSet{string})"/>: whether the dimension is known, and what it is when it is.</summary>
    private (bool Known, Dimension Dimension) Type(ExpressionSyntax expression, HashSet<string> visiting)
    {
        switch (expression)
        {
            case NumberLiteralSyntax:
                return (true, Dimension.Dimensionless);

            case QuantityLiteralSyntax literal:
                return UnitTable.Resolve(literal.Unit, null) is { } unit ? (true, unit.Dimension) : (false, default);

            case QuantityReferenceSyntax quantity:
            {
                // `HE1.dp kPa`: the reference's own dimension picks between a shared spelling's readings.
                var inner = TypeReference(quantity.Reference, visiting);
                return UnitTable.Resolve(quantity.Unit, inner.Known ? inner.Dimension : null) is { } stated ? (true, stated.Dimension) : (false, default);
            }

            case ParenthesizedExpressionSyntax parenthesized:
                return Type(parenthesized.Inner, visiting);

            case UnaryExpressionSyntax unary:
                return Type(unary.Operand, visiting);

            case BinaryExpressionSyntax binary:
            {
                var left = Type(binary.Left, visiting);
                var right = Type(binary.Right, visiting);

                if (!left.Known || !right.Known)
                {
                    return (false, default);
                }

                switch (binary.Operator)
                {
                    case BinaryOperator.Multiply:
                        return (true, Dimension.FromVector(left.Dimension.Vector + right.Dimension.Vector));

                    case BinaryOperator.Divide:
                        return (true, Dimension.FromVector(left.Dimension.Vector - right.Dimension.Vector));

                    default:
                        // A sum keeps the dimensioned side: `HE1.dp + 5` reads the 5 in the other's unit (D-14).
                        if (left.Dimension.IsNamed && left.Dimension.Name == "Dimensionless")
                        {
                            return right;
                        }

                        if (left.Dimension == right.Dimension || (right.Dimension.IsNamed && right.Dimension.Name == "Dimensionless"))
                        {
                            return left;
                        }

                        // `HE1.dp + 5 kPa`: the literal's spelling is shared by a reading and a difference, and
                        // with no destination to consult it typed as the reading; the evaluator reads it against
                        // the other operand, and a sum of like vectors is the difference dimension (FromVector).
                        return left.Dimension.Vector == right.Dimension.Vector ? (true, Dimension.FromVector(left.Dimension.Vector)) : (false, default);
                }
            }

            case ReferenceSyntax reference:
                return TypeReference(reference, visiting);

            default:
                // A call: the closed set has functions of every shape, and typing them is a second evaluator.
                return (false, default);
        }
    }

    private (bool Known, Dimension Dimension) TypeReference(ReferenceSyntax reference, HashSet<string> visiting)
    {
        var head = reference.Head.Token.Text;

        if (reference.Parts.IsEmpty)
        {
            if (Constants.TryGet(head, out var constant))
            {
                return (true, constant.Dimension);
            }

            if (!_bindingsByName.TryGetValue(head, out var binding))
            {
                // A curve, or nothing: a curve's numbers are bare until something reads them (D-57).
                return (false, default);
            }

            if (_pending.TryGetValue(binding.Id, out var pending) && pending.Value is { } value)
            {
                return (true, value.Dimension);
            }

            return visiting.Add(head) ? Type(binding.Declaration.Value, visiting) : (false, default);
        }

        if (!_componentsByName.TryGetValue(head, out var slot) || _components[slot.Index].Kind is not { } kind)
        {
            return (false, default);
        }

        var written = reference.PropertyPath();

        if (StatedParameterKey(kind, written) is { } key
            && _components[slot.Index].Parameters.ContainsKey(key)
            && kind.Parameters.GetValueOrDefault(key) is { ValueKind: ParameterValueKind.Quantity } parameter)
        {
            return (true, parameter.Dimension);
        }

        return kind.ResolveProperty(written) is { } property ? (true, property.Dimension) : (false, default);
    }

    /// <summary>Says, once per site, which property references were written in a spelling <c>D-120</c> retired.</summary>
    /// <remarks>
    /// Not in <see cref="Lookup"/>: a reference is evaluated as often as the fixed point needs, and a
    /// diagnostic raised there would repeat with it. One walk over the statements after evaluation is
    /// one message per written name, at the reference's own span, with the current spelling of the
    /// whole reference as the quick fix -- <c>HX1.t_in2</c> becomes <c>HX1.in[2].t</c>.
    /// </remarks>
    private void ReviewLegacyReferences()
    {
        foreach (var statement in parse.Root.Statements)
        {
            foreach (var expression in Expressions(statement))
            {
                foreach (var reference in References(expression))
                {
                    if (reference.Parts.IsEmpty
                        || !_componentsByName.TryGetValue(reference.Head.Token.Text, out var slot)
                        || _components[slot.Index].Kind is not { } kind)
                    {
                        continue;
                    }

                    var written = reference.PropertyPath();
                    kind.ResolveProperty(written, out var suggestion);

                    if (suggestion is not null)
                    {
                        var span = TextSpan.FromBounds(reference.Parts[0].Name.Span.Start, reference.Span.End);
                        ReportLegacySpelling(span, written, suggestion);
                    }
                }
            }
        }
    }

    /// <summary>Every expression a statement carries: its parameters' values and a <c>let</c>'s right-hand side.</summary>
    private static IEnumerable<ExpressionSyntax> Expressions(StatementSyntax statement)
    {
        var parameters = statement switch
        {
            ComponentDeclarationSyntax declaration => declaration.Parameters.Concat(declaration.SizingPoint),
            ConnectionSyntax connection => connection.Parameters,
            ControlBindingSyntax control => control.Arguments,
            DesignDirectiveSyntax design => design.Arguments,
            CurveHeaderSyntax curve => curve.Arguments,
            _ => [],
        };

        foreach (var parameter in parameters)
        {
            // A list is not an expression that evaluates; its elements are (`D-143`). Yielding the
            // list itself would make the graph depend on a node nothing ever produces a value for.
            if (parameter.Value is ScenarioListSyntax list)
            {
                foreach (var element in list.Elements)
                {
                    yield return element.Value;
                }

                continue;
            }

            yield return parameter.Value;
        }

        if (statement is LetBindingSyntax let)
        {
            yield return let.Value;
        }
    }

    /// <summary>Every reference inside an expression, in source order.</summary>
    private static IEnumerable<ReferenceSyntax> References(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ReferenceSyntax reference:
                yield return reference;
                break;

            case QuantityReferenceSyntax quantity:
                yield return quantity.Reference;
                break;

            case BinaryExpressionSyntax binary:
                foreach (var inner in References(binary.Left).Concat(References(binary.Right)))
                {
                    yield return inner;
                }

                break;

            case UnaryExpressionSyntax unary:
                foreach (var inner in References(unary.Operand))
                {
                    yield return inner;
                }

                break;

            case ParenthesizedExpressionSyntax parenthesized:
                foreach (var inner in References(parenthesized.Inner))
                {
                    yield return inner;
                }

                break;

            case CallSyntax call:
                foreach (var inner in call.Arguments.SelectMany(static argument => References(argument.Value)))
                {
                    yield return inner;
                }

                break;
        }
    }
}
