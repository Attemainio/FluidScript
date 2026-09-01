namespace FluidScript.Core.Syntax;

/// <summary>What a run of non-token characters is.</summary>
/// <remarks>
/// Trivia carries no meaning to the binder and all of its meaning to the printer: reproducing a script
/// byte for byte is a matter of putting every trivium back where it was
/// (<c>plan/10-language/17-formatting-and-round-trip.md</c>).
/// </remarks>
public enum TriviaKind
{
    /// <summary>Spaces and horizontal tabs.</summary>
    /// <remarks>Indentation is this and nothing more: the language is line-oriented and never
    /// significant on leading whitespace.</remarks>
    Whitespace = 1,

    /// <summary>A <c>#</c> and the rest of its line.</summary>
    /// <remarks>The terminating newline is a separate <see cref="EndOfLine"/> trivium, so a comment
    /// never spans a line boundary.</remarks>
    Comment,

    /// <summary>One line break: <c>\n</c>, <c>\r\n</c> or a bare <c>\r</c>.</summary>
    /// <remarks>Held as one trivium whichever form it took, so the printer reproduces the original
    /// convention rather than normalising a file to the platform that happened to open it.</remarks>
    EndOfLine,
}
