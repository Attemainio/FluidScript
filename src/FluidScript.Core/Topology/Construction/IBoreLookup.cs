namespace FluidScript.Core.Topology.Construction;

/// <summary>Turns a nominal diameter designation into the bore a hydraulic calculation uses.</summary>
/// <remarks>
/// <para>
/// <strong>DN is a designation, not a diameter.</strong> DN25 steel pipe has a 27.3 mm bore, and
/// computing an area from 25 mm is a 16 % area error and roughly a factor of two in pressure gradient,
/// with nothing in the result looking wrong. The mapping is catalogue data
/// (<see href="27-component-catalog.md"/>, <c>P3.5</c>), and this interface is the seam that lets
/// lowering exist a package before the catalogue does.
/// </para>
/// <para>
/// It is not a general catalogue lookup and should not grow into one: everything else a catalogue row
/// holds — wall thickness, material, pressure class, the two public sources every row carries — is
/// wanted after lowering, not during it.
/// </para>
/// </remarks>
public interface IBoreLookup
{
    /// <summary>The inside diameter of a pipe of this nominal size.</summary>
    /// <param name="nominalDiameter">The DN designation, as a bare number.</param>
    /// <returns>m, or <see langword="null"/> when the designation is not in the catalogue.</returns>
    /// <param name="material">A catalogue id the pipe named with <c>material=</c>, or <see langword="null"/> for the script's catalogue (<c>C-36</c>).</param>
    double? BoreFor(double nominalDiameter, string? material = null);
}
