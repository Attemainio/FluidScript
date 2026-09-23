using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>Resolves the name a script writes to the substance behind it.</summary>
public interface ISubstanceRegistry
{
    /// <summary>Gets every registered name, in order, for a diagnostic that lists them.</summary>
    System.Collections.Immutable.ImmutableArray<string> Names { get; }

    /// <summary>Resolves a script name.</summary>
    /// <param name="name">The name as written, such as <c>water</c>.</param>
    /// <returns>The substance, or <c>FS2001</c> listing what is available.</returns>
    Result<ISubstance> Resolve(string name);
}
