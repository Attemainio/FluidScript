using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests;

/// <summary>Parses a script as the pipeline does: under the major its version line selects (<see cref="MajorParser"/>).</summary>
/// <remarks>
/// A test that binds a script parses it here, so a sample or an inline script is read in whichever language it is
/// written in (<c>D-174</c>). Only a test of one language's grammar or printer calls that language's parser itself.
/// </remarks>
public static class ScriptParse
{
    /// <summary>Parses source text.</summary>
    /// <param name="source">The script.</param>
    /// <returns>The tree the binder reads, with the parser's diagnostics.</returns>
    public static ParseResult Parse(SourceText source) =>
        MajorParser.Parse(source, ScriptCompatibility.Inspect(source).DetectedMajor, ComponentRegistry.Default);

    /// <summary>Parses a script's text.</summary>
    /// <param name="text">The script.</param>
    /// <returns>The tree the binder reads, with the parser's diagnostics.</returns>
    public static ParseResult Parse(string text) => Parse(new SourceText(text));
}
