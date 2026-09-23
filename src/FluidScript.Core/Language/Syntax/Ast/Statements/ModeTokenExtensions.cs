using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Reads a solve-mode keyword token.</summary>
internal static class ModeTokenExtensions
{
    /// <summary>Converts an optional <c>dynamic</c>/<c>static</c> token to a mode.</summary>
    /// <param name="token">The token, or <see langword="null"/> when neither word was written.</param>
    /// <returns>The mode, or <see langword="null"/> when nothing was written.</returns>
    public static FluidMode? ToFluidMode(this Token? token) => token?.Keyword switch
    {
        ReservedWord.Dynamic => FluidMode.Dynamic,
        ReservedWord.Static => FluidMode.Static,
        _ => null,
    };
}
