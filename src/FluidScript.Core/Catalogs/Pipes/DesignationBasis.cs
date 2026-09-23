namespace FluidScript.Core.Catalogs.Pipes;

/// <summary>What the number a script writes in <c>dn</c> designates.</summary>
public enum DesignationBasis
{
    /// <summary>A nominal size: a label, not a length. Steel's DN25 has a 27.3 mm bore.</summary>
    NominalSize,

    /// <summary>The outside diameter in millimetres. Copper's 22 mm tube has a 20.2 mm bore.</summary>
    OutsideDiameter,
}
