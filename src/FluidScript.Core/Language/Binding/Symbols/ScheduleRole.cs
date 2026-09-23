using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One driver a curve can depend on by name (<c>D-59</c>).</summary>
/// <param name="CanonicalName">The registered name, such as <c>tout</c>.</param>
/// <param name="Dimension">
/// The dimension a <c>design</c> value for this driver is read in, or <see langword="null"/> when the
/// language names none for the quantity. A design value under a dimensionful role is compared in that
/// dimension's canonical unit, which is what makes <c>design tout=-26</c> and <c>design tout=-26 C</c>
/// the same point on a curve whose own table is bare.
/// </param>
/// <remarks>
/// Registry data, not a closed set in the language: adding a driver is a registry change, never a
/// grammar change — the same trade <see cref="CircuitRole"/> makes.
/// </remarks>
public sealed record ScheduleRole(string CanonicalName, Dimension? Dimension);
