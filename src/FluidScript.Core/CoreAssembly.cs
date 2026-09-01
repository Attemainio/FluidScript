using System.Reflection;

namespace FluidScript.Core;

/// <summary>
/// Provides a stable handle on the assembly that contains FluidScript's language and physics
/// implementation.
/// </summary>
/// <remarks>
/// The architecture tests need to reach this assembly without naming a type that will later move
/// as the tier 10-30 namespaces are filled in, and reaching it through any real type would make
/// those tests fail for the wrong reason the first time that type is renamed. It carries no
/// behaviour. The invariants it exists to guard are stated in
/// <c>plan/00-foundation/03-repository-layout.md</c>.
/// </remarks>
public static class CoreAssembly
{
    /// <summary>Gets the assembly containing FluidScript's language and physics implementation.</summary>
    /// <value>Never <see langword="null"/>.</value>
    public static Assembly Reference => typeof(CoreAssembly).Assembly;
}
