namespace FluidScript.Core.Tests;

/// <summary>Edits a script's text for a test, failing where the text to replace is not there.</summary>
/// <remarks>
/// A <c>Replace</c> that finds nothing changes nothing, and the test then fails far from its cause -- or passes on the
/// unedited script. The language 1 samples' conversion to language 2 (<c>P6.11</c> package 7) broke several such edits
/// silently; this says which text went missing.
/// </remarks>
public static class ScriptEdits
{
    /// <summary>Replaces every occurrence of text the script is known to hold.</summary>
    /// <param name="script">The script.</param>
    /// <param name="from">The text to replace; the test fails when the script does not hold it.</param>
    /// <param name="to">What replaces it.</param>
    /// <returns>The edited script.</returns>
    public static string Edited(this string script, string from, string to)
    {
        Assert.Contains(from, script, StringComparison.Ordinal);
        return script.Replace(from, to, StringComparison.Ordinal);
    }
}
