using System.Globalization;

namespace FluidScript.Core.Catalogs.Pipes;

/// <summary>Builds a pipe catalogue from its rows, so each catalogue file holds only its data.</summary>
/// <remarks>
/// <para>
/// <strong>A builder, not a base class, because what differs between the catalogues is data</strong>
/// (<c>D-147</c>). Steel to EN 10255, steel to EN 10220 and copper to EN 1057 each built the same
/// <see cref="Catalog{TSpec}"/> the same way — millimetres to metres, the series and roughness on every
/// row, ascending by outside diameter — and differed only in the rows, the sources, the roughness and
/// how a row is designated. A hierarchy with no behaviour in it would say the opposite.
/// </para>
/// <para>
/// <strong>The rows and the sources must be initialised before the catalogue that reads them.</strong>
/// Each catalogue calls this from a static initialiser, and static initialisers run in textual order:
/// a source list declared below its <c>Instance</c> is null in every row.
/// </para>
/// </remarks>
internal static class PipeCatalogBuilder
{
    /// <summary>Builds a catalogue designated by nominal size, every row under one provenance.</summary>
    /// <param name="id">The catalogue id a script pins.</param>
    /// <param name="version">The catalogue's version.</param>
    /// <param name="standard">The standard the rows conform to, cited by number.</param>
    /// <param name="series">The series every row belongs to, as a report prints it.</param>
    /// <param name="roughness">m, the absolute wall roughness of every row.</param>
    /// <param name="rows">DN, outside diameter in mm and wall in mm, ascending.</param>
    /// <param name="provenance">Where every row was read from.</param>
    /// <returns>The catalogue, each row designated <c>DN</c> and its size.</returns>
    public static ICatalog<PipeSpec> Build(
        string id,
        string version,
        string standard,
        string series,
        double roughness,
        IEnumerable<(int Dn, double OdMm, double WallMm)> rows,
        Provenance provenance) =>
        Build(
            id,
            version,
            standard,
            series,
            roughness,
            DesignationBasis.NominalSize,
            rows.Select(row => (row.Dn, row.OdMm, row.WallMm, "DN" + row.Dn.ToString(CultureInfo.InvariantCulture), provenance)));

    /// <summary>Builds a catalogue whose rows carry their own designation and provenance.</summary>
    /// <param name="id">The catalogue id a script pins.</param>
    /// <param name="version">The catalogue's version.</param>
    /// <param name="standard">The standard the rows conform to, cited by number.</param>
    /// <param name="series">The series every row belongs to, as a report prints it.</param>
    /// <param name="roughness">m, the absolute wall roughness of every row.</param>
    /// <param name="basis">What a row's designation number is: a nominal size or an outside diameter.</param>
    /// <param name="rows">DN, outside diameter in mm, wall in mm, designation and provenance, ascending.</param>
    /// <returns>The catalogue.</returns>
    public static ICatalog<PipeSpec> Build(
        string id,
        string version,
        string standard,
        string series,
        double roughness,
        DesignationBasis basis,
        IEnumerable<(int Dn, double OdMm, double WallMm, string Designation, Provenance Provenance)> rows) =>
        new Catalog<PipeSpec>(
            id,
            version,
            standard,
            rows.Select(row => new CatalogEntry<PipeSpec>
            {
                Designation = row.Designation,
                Spec = new PipeSpec
                {
                    NominalDiameter = row.Dn,
                    DesignationBasis = basis,
                    OutsideDiameter = row.OdMm / 1000,
                    WallThickness = row.WallMm / 1000,
                    Roughness = roughness,
                    Series = series,
                },

                Provenance = row.Provenance,
            }),
            PipeSpec.Fault,
            static spec => spec.OutsideDiameter);
}
