namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>The binary operators, tightest binding first.</summary>
/// <remarks>
/// There is no remainder operator: <c>%</c> is a unit symbol (<c>D-51</c>). There is no exponentiation
/// operator either — <c>pow(x, 2)</c> is a call, because <c>^</c> and <c>**</c> both have a
/// constituency and picking one violates P6 for no gain.
/// </remarks>
public enum BinaryOperator
{
    /// <summary>Multiplication.</summary>
    Multiply = 1,

    /// <summary>Division.</summary>
    Divide,

    /// <summary>Addition.</summary>
    Add,

    /// <summary>Subtraction.</summary>
    Subtract,
}
