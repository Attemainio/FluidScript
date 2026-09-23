namespace FluidScript.Core.Components.Declarations;

/// <summary>One parameter a component reads from the solve, and the value it holds now.</summary>
/// <param name="Name">The canonical parameter name, spelled as the registry and a promotion spell it.</param>
/// <param name="Value">
/// What the component uses when nothing supplies one, in SI. The component's own stored value, so that
/// filling a parameter buffer needs no knowledge of the kind.
/// </param>
/// <param name="SiUnit">
/// The SI unit of the value, so a promoted column can say what its number means. A dimensionless
/// parameter carries <c>"1"</c>; <c>kv</c> is <c>"m3/h"</c>, which is not SI and says so by being the
/// one exception — a valve coefficient is defined by a standard's test rig and has no SI form.
/// </param>
/// <param name="Minimum">
/// The smallest physically meaningful value, or <see langword="null"/> where none exists. A promoted
/// column is projected into this range after every Newton step (<c>S-26b</c>).
/// </param>
/// <param name="Maximum">The largest physically meaningful value, or <see langword="null"/>.</param>
/// <remarks>
/// <strong>The component is the only thing that knows its own range, and the solver knows none of
/// them.</strong> A <c>position</c> is a fraction and a <c>kv</c> is positive; left unbounded, a
/// promoted <c>position</c> on the cooling loop walks to 5.5 chasing a mixed inlet temperature the
/// field cannot deliver. Carrying the range here rather than in the solver keeps `D-30`'s rule that a
/// parameter's meaning belongs to its component, and it is the same reason
/// <see cref="FluidScript.Core.Solvers.Equations.SystemLayout"/> reads the unit off this record instead of off the registry.
/// </remarks>
public readonly record struct ResolvedParameter(
    string Name, double Value, string SiUnit, double? Minimum = null, double? Maximum = null);
