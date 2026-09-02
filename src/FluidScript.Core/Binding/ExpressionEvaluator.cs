using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>What evaluating an expression produced.</summary>
public abstract record EvaluationResult
{
    private EvaluationResult()
    {
    }

    /// <summary>A value.</summary>
    /// <param name="Quantity">The value, in SI.</param>
    /// <param name="IsBare">
    /// Whether no unit symbol took part anywhere in the expression. A bare result is reinterpreted in
    /// the target's canonical unit when it is assigned, which is what makes <c>power=30</c> mean 30 kW
    /// and <c>length=45</c> mean 45 m (<c>D-14</c>).
    /// </param>
    public sealed record Value(Quantity Quantity, bool IsBare) : EvaluationResult;

    /// <summary>The expression reads something no stage has computed yet.</summary>
    /// <param name="Dependencies">Every value it reads, so the outer loop knows what to wait for.</param>
    public sealed record Deferred(ImmutableHashSet<ValueId> Dependencies) : EvaluationResult;

    /// <summary>The expression could not be evaluated, and the reason has been reported.</summary>
    public sealed record Failed : EvaluationResult;
}

/// <summary>What a name in an expression turned out to be.</summary>
public abstract record ScopeLookup
{
    private ScopeLookup()
    {
    }

    /// <summary>A value that is already known.</summary>
    /// <param name="Quantity">The value.</param>
    /// <param name="IsBare">Whether it came from a bare number.</param>
    /// <param name="Id">Its identity in the dependency graph.</param>
    public sealed record Value(Quantity Quantity, bool IsBare, ValueId Id) : ScopeLookup;

    /// <summary>A value that will not exist until sizing or the solve has run.</summary>
    /// <param name="Id">What to wait for.</param>
    public sealed record Deferred(ValueId Id) : ScopeLookup;

    /// <summary>Nothing of that name exists.</summary>
    /// <param name="Suggestion">The closest name, or <see langword="null"/> when nothing is close.</param>
    public sealed record UnknownName(string? Suggestion) : ScopeLookup;

    /// <summary>The component exists; the property does not.</summary>
    /// <param name="Kind">The component's kind, for the message.</param>
    /// <param name="Available">What it does have.</param>
    public sealed record UnknownProperty(string Kind, ImmutableArray<string> Available) : ScopeLookup;
}

/// <summary>Where an expression's names are looked up.</summary>
public interface IValueScope
{
    /// <summary>Resolves one reference — a binding's name, or <c>Component.property</c>.</summary>
    /// <param name="reference">The reference as written.</param>
    /// <returns>What the name turned out to be.</returns>
    ScopeLookup Lookup(ReferenceSyntax reference);
}

/// <summary>Evaluates a parsed expression against a scope.</summary>
/// <remarks>
/// <para>
/// Never throws (<c>14</c>'s invariant 6): a division by zero is <c>FS1403</c>, a dimension mismatch
/// is <c>FS1305</c>, and an unknown name is <c>FS1404</c>. Every failure is a diagnostic and a
/// <see cref="EvaluationResult.Failed"/>.
/// </para>
/// <para>
/// Dimensional correctness is checked as part of evaluating, not after it, so no arithmetic is
/// performed on mismatched dimensions even inside an expression that would have been deferred.
/// </para>
/// </remarks>
public sealed class ExpressionEvaluator
{
    /// <summary>The functions a script may call. Fixed and closed (<c>D-01</c>).</summary>
    /// <value>
    /// <c>min</c>, <c>max</c>, <c>abs</c>, <c>round</c>, <c>pow</c> and <c>sqrt</c>. Not extensible:
    /// there is nothing to branch on and nothing to loop over, so the set that would grow does not.
    /// </value>
    public static ImmutableArray<string> Functions { get; } = ["abs", "max", "min", "pow", "round", "sqrt"];

    private readonly IValueScope _scope;
    private readonly SourceText _source;
    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics;
    private readonly HashSet<ValueId> _dependencies = [];

    /// <summary>Creates an evaluator over one scope.</summary>
    /// <param name="scope">Where names are looked up.</param>
    /// <param name="source">The text, for quoting an expression in a message.</param>
    /// <param name="diagnostics">Where failures are reported.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ExpressionEvaluator(
        IValueScope scope,
        SourceText source,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _scope = scope;
        _source = source;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets every value the last evaluation read.</summary>
    /// <value>
    /// Includes values that were already known, not only deferred ones, so cycle formatting and
    /// invalidation are deterministic.
    /// </value>
    public ImmutableHashSet<ValueId> Dependencies => [.. _dependencies];

    /// <summary>Evaluates one expression.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>A value, a deferral, or a failure that has already been reported.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public EvaluationResult Evaluate(ExpressionSyntax expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        _dependencies.Clear();

        return Visit(expression);
    }

    private EvaluationResult Visit(ExpressionSyntax expression) => expression switch
    {
        NumberLiteralSyntax number => Number(number),
        QuantityLiteralSyntax quantity => Literal(quantity),
        ReferenceSyntax reference => Reference(reference),
        ParenthesizedExpressionSyntax parenthesized => Visit(parenthesized.Inner),
        UnaryExpressionSyntax unary => Unary(unary),
        BinaryExpressionSyntax binary => Binary(binary),
        CallSyntax call => Call(call),

        // A string is a style token, not a value. Nothing in the grammar puts one where a number
        // belongs, so reaching here means the parser produced a shape this stage does not model.
        _ => Fail(BinderDiagnostics.UnknownName, expression.Span, new DiagnosticArgument("name", Text(expression))),
    };

    private static EvaluationResult.Value Number(NumberLiteralSyntax number) =>
        new EvaluationResult.Value(
            Quantity.FromSi(number.Token.Value ?? 0, Dimension.Dimensionless),
            IsBare: true);

    private static EvaluationResult.Value Literal(QuantityLiteralSyntax quantity)
    {
        var token = quantity.Token;

        if (token.Unit is null || !UnitTable.TryResolve(token.Unit, out var unit))
        {
            // The lexer only produces a quantity token when the unit matched the table, so an
            // unresolvable one here would be a lexer defect rather than a user error.
            return new EvaluationResult.Value(
                Quantity.FromSi(token.Value ?? 0, Dimension.Dimensionless),
                IsBare: true);
        }

        return new EvaluationResult.Value(Quantity.FromUnit(token.Value ?? 0, unit), IsBare: false);
    }

    private EvaluationResult Reference(ReferenceSyntax reference)
    {
        switch (_scope.Lookup(reference))
        {
            case ScopeLookup.Value value:
                _dependencies.Add(value.Id);
                return new EvaluationResult.Value(value.Quantity, value.IsBare);

            case ScopeLookup.Deferred deferred:
                _dependencies.Add(deferred.Id);
                return new EvaluationResult.Deferred([deferred.Id]);

            case ScopeLookup.UnknownProperty unknown:
                return Fail(
                    BinderDiagnostics.UnknownProperty,
                    reference.Span,
                    new DiagnosticArgument("kind", unknown.Kind),
                    new DiagnosticArgument("property", reference.Parts[^1].Name.Token.Text),
                    new DiagnosticArgument("available", string.Join(", ", unknown.Available)));

            case ScopeLookup.UnknownName { Suggestion: { } suggestion }:
                _diagnostics.Add(
                    Diagnostic.Create(
                        BinderDiagnostics.UnknownName,
                        reference.Span,
                        new DiagnosticArgument("name", Text(reference)))
                    with
                    {
                        Suggestion = new Suggestion($"Change it to '{suggestion}'", reference.Span, suggestion),
                    });
                return new EvaluationResult.Failed();

            default:
                return Fail(
                    BinderDiagnostics.UnknownName,
                    reference.Span,
                    new DiagnosticArgument("name", Text(reference)));
        }
    }

    private EvaluationResult Unary(UnaryExpressionSyntax unary)
    {
        if (Visit(unary.Operand) is not EvaluationResult.Value operand)
        {
            return Visit(unary.Operand);
        }

        if (!Quantity.TryNegate(operand.Quantity, out var negated, out var error))
        {
            return FailArithmetic(error, "negate", operand.Quantity, operand.Quantity, unary.Span);
        }

        return new EvaluationResult.Value(negated, operand.IsBare);
    }

    private EvaluationResult Binary(BinaryExpressionSyntax binary)
    {
        var left = Visit(binary.Left);
        var right = Visit(binary.Right);

        // Both sides are visited before either result is inspected, so a mistake on the right is
        // reported even when the left has already deferred. A user editing one line wants every
        // problem on it, not the first.
        if (left is EvaluationResult.Failed || right is EvaluationResult.Failed)
        {
            return new EvaluationResult.Failed();
        }

        if (left is EvaluationResult.Deferred or EvaluationResult.Value && right is EvaluationResult.Deferred
            || left is EvaluationResult.Deferred)
        {
            return new EvaluationResult.Deferred([.. _dependencies]);
        }

        var a = ((EvaluationResult.Value)left).Quantity;
        var b = ((EvaluationResult.Value)right).Quantity;
        var bare = ((EvaluationResult.Value)left).IsBare && ((EvaluationResult.Value)right).IsBare;

        var (succeeded, result, error) = binary.Operator switch
        {
            BinaryOperator.Add => Try(Quantity.TryAdd, a, b),
            BinaryOperator.Subtract => Try(Quantity.TrySubtract, a, b),
            BinaryOperator.Multiply => Try(Quantity.TryMultiply, a, b),
            _ => Try(Quantity.TryDivide, a, b),
        };

        if (!succeeded)
        {
            return FailArithmetic(error, NameOf(binary.Operator), a, b, binary.Span, binary.Right);
        }

        return new EvaluationResult.Value(result, bare);
    }

    private EvaluationResult Call(CallSyntax call)
    {
        var name = call.Name.Token.Text;
        if (!Functions.Contains(name, StringComparer.Ordinal))
        {
            return Fail(
                BinderDiagnostics.UnknownFunction,
                call.Span,
                new DiagnosticArgument("name", name),
                new DiagnosticArgument("available", string.Join(", ", Functions)));
        }

        var arguments = new List<EvaluationResult.Value>();
        var deferred = false;

        foreach (var argument in call.Arguments)
        {
            switch (Visit(argument.Value))
            {
                case EvaluationResult.Value value:
                    arguments.Add(value);
                    break;
                case EvaluationResult.Deferred:
                    deferred = true;
                    break;
                default:
                    return new EvaluationResult.Failed();
            }
        }

        if (!HasValidArity(name, call.Arguments.Length, out var expected))
        {
            return Fail(
                BinderDiagnostics.WrongArgumentCount,
                call.Span,
                new DiagnosticArgument("function", name),
                new DiagnosticArgument("expected", expected));
        }

        if (deferred)
        {
            return new EvaluationResult.Deferred([.. _dependencies]);
        }

        return Apply(name, arguments, call);
    }

    private EvaluationResult Apply(string name, List<EvaluationResult.Value> arguments, CallSyntax call)
    {
        var bare = arguments.All(static argument => argument.IsBare);
        var first = arguments[0].Quantity;

        switch (name)
        {
            case "min" or "max":
                foreach (var argument in arguments)
                {
                    if (argument.Quantity.Dimension != first.Dimension)
                    {
                        return FailArithmetic(
                            QuantityError.DimensionMismatch, "compare", first, argument.Quantity, call.Span);
                    }
                }

                var chosen = name == "min"
                    ? arguments.MinBy(static argument => argument.Quantity.SiValue)
                    : arguments.MaxBy(static argument => argument.Quantity.SiValue);

                return new EvaluationResult.Value(chosen!.Quantity, bare);

            case "abs":
                return new EvaluationResult.Value(
                    Quantity.FromSi(Math.Abs(first.SiValue), first.Dimension), bare);

            case "round":
                var decimals = arguments.Count == 2 ? (int)arguments[1].Quantity.SiValue : 0;
                return new EvaluationResult.Value(
                    Quantity.FromSi(Math.Round(first.SiValue, Math.Clamp(decimals, 0, 15)), first.Dimension),
                    bare);

            // `pow` and `sqrt` take dimensionless arguments only. A dimensioned base with a runtime
            // exponent gives an exponent vector unknown until evaluation, and a square root halves
            // every exponent — legal arithmetic, unnameable dimensions, and no useful FS1304. Those
            // equations live in Core, where the bookkeeping is done once and tested.
            case "pow" when first.Dimension != Dimension.Dimensionless:
            case "sqrt" when first.Dimension != Dimension.Dimensionless:
                return Fail(
                    BinderDiagnostics.OperandDimensionMismatch,
                    call.Span,
                    new DiagnosticArgument("operation", name),
                    new DiagnosticArgument("left", first.Dimension.Name),
                    new DiagnosticArgument("right", "a dimensionless number"));

            case "pow":
                return new EvaluationResult.Value(
                    Quantity.FromSi(Math.Pow(first.SiValue, arguments[1].Quantity.SiValue), Dimension.Dimensionless),
                    bare);

            default:
                return new EvaluationResult.Value(
                    Quantity.FromSi(Math.Sqrt(first.SiValue), Dimension.Dimensionless), bare);
        }
    }

    private static bool HasValidArity(string name, int count, out string expected)
    {
        switch (name)
        {
            case "min" or "max":
                expected = "at least two";
                return count >= 2;
            case "abs" or "sqrt":
                expected = "one";
                return count == 1;
            case "pow":
                expected = "two";
                return count == 2;
            default:
                expected = "one or two";
                return count is 1 or 2;
        }
    }

    private static (bool Succeeded, Quantity Result, QuantityError Error) Try(
        Operation operation, Quantity left, Quantity right)
    {
        var succeeded = operation(left, right, out var result, out var error);
        return (succeeded, result, error);
    }

    private static string NameOf(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "add",
        BinaryOperator.Subtract => "subtract",
        BinaryOperator.Multiply => "multiply",
        _ => "divide",
    };

    private EvaluationResult.Failed FailArithmetic(
        QuantityError error,
        string operation,
        Quantity left,
        Quantity right,
        TextSpan span,
        ExpressionSyntax? divisor = null)
    {
        if (error == QuantityError.DivisionByZero)
        {
            return Fail(
                BinderDiagnostics.DivisionByZero,
                span,
                new DiagnosticArgument("expression", divisor is null ? "the divisor" : Text(divisor)));
        }

        if (error == QuantityError.AbsoluteAddition)
        {
            // The invariant the type system exists for. The message says what to write instead,
            // because "cannot add two temperatures" without the alternative is a dead end.
            return Fail(
                BinderDiagnostics.CannotAddAbsolutes,
                span,
                new DiagnosticArgument("dimension", Describe(left.Dimension)),
                new DiagnosticArgument("example", Example(left, right)));
        }

        return Fail(
            BinderDiagnostics.OperandDimensionMismatch,
            span,
            new DiagnosticArgument("operation", operation),
            new DiagnosticArgument("left", Describe(left.Dimension)),
            new DiagnosticArgument("right", Describe(right.Dimension)));
    }

    private static string Describe(Dimension dimension) => dimension.Name.ToLowerInvariant();

    private static string Example(Quantity left, Quantity right) =>
        left.Dimension == Dimension.Temperature
            ? $"{left.ValueIn(UnitTable.CanonicalUnitFor(Dimension.Temperature)!):0.##} °C + " +
              $"{right.SiValue:0.##} dK"
            : "a difference rather than a second absolute value";

    private EvaluationResult.Failed Fail(DiagnosticDescriptor descriptor, TextSpan span, params DiagnosticArgument[] arguments)
    {
        _diagnostics.Add(Diagnostic.Create(descriptor, span, arguments));
        return new EvaluationResult.Failed();
    }

    private string Text(SyntaxNode node) => _source.ToString(node.Span).Trim();

    private delegate bool Operation(Quantity left, Quantity right, out Quantity result, out QuantityError error);
}
