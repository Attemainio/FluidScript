namespace FluidScript.Core.Language.Syntax.Ast;

/// <summary>Which side of a subcircuit's attachment a statement declares.</summary>
/// <remarks><c>D-33</c>. <c>in</c> and <c>out</c> were the obvious spelling and are lexically
/// impossible, which is why <c>FS1109</c> exists.</remarks>
public enum AttachmentDirection
{
    /// <summary>Takes flow from the parent circuit.</summary>
    Inlet = 1,

    /// <summary>Returns flow to the parent circuit.</summary>
    Outlet,
}
