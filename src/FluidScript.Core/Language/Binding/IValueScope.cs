using FluidScript.Core.Language.Syntax.Ast.Expressions;

namespace FluidScript.Core.Language.Binding;

/// <summary>Where an expression's names are looked up.</summary>
public interface IValueScope
{
    /// <summary>Resolves one reference — a binding's name, or <c>Component.property</c>.</summary>
    /// <param name="reference">The reference as written.</param>
    /// <returns>What the name turned out to be.</returns>
    ScopeLookup Lookup(ReferenceSyntax reference);
}
