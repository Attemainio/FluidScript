using System.Collections.Immutable;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Translation;

/// <summary>Puts a language 2 file's diagnostics into language 2's words (<c>19</c> §Diagnostics).</summary>
/// <remarks>
/// <para>
/// A code keeps its meaning in both languages, and most keep their message too. A message that quotes language 1's
/// syntax — <c>Add 'scenarios &lt;name&gt; &lt;name&gt;'</c>, <c>in[2].t</c> on an exchanger — has a second wording on its
/// descriptor, and this renders it from the arguments the emitting stage supplied. It runs where a stage that knows
/// the language hands its diagnostics on: the parse (<c>MajorParser</c>), the binding, and the model contract, which
/// gathers every later stage's. Rendering starts from the arguments every time, so running it twice changes nothing.
/// </para>
/// <para>
/// Some arguments carry language 1 spellings too: an exchanger's second side is <c>in[2]</c> there and
/// <c>secondary.in</c> here. They are respelled only where the code is about an exchanger — by its own subject, or by
/// the kind it names — because a tank's <c>in[2]</c> is language 2 as well. Where neither says, the language 1
/// spelling stands, which language 2 also reads.
/// </para>
/// </remarks>
public static class Language2Wording
{
    private const string Exchanger = "heat_exchanger";

    private static readonly Regex SecondSide = new(@"\b(in|out)\[2\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex KelvinDifference = new(@"\bdK\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Re-renders every diagnostic in language 2's words.</summary>
    /// <param name="diagnostics">A language 2 file's diagnostics, from any stage.</param>
    /// <returns>The same diagnostics, in order, each message in language 2's wording.</returns>
    public static ImmutableArray<Diagnostic> Apply(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.IsDefaultOrEmpty ? diagnostics : [.. diagnostics.Select(Apply)];

    /// <summary>Re-renders one diagnostic in language 2's words.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>It with its message in language 2's wording; itself when one wording serves both languages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic"/> is <see langword="null"/>.</exception>
    public static Diagnostic Apply(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (!DiagnosticRegistry.TryGet(diagnostic.Code, out var descriptor) || descriptor is null)
        {
            return diagnostic;
        }

        var arguments = Respelled(diagnostic.Code, diagnostic.Arguments);
        if (descriptor.Language2Template is null && arguments.SequenceEqual(diagnostic.Arguments))
        {
            return diagnostic;
        }

        return diagnostic with { Message = descriptor.RenderLanguage2([.. arguments]) };
    }

    /// <summary>The arguments with language 1's spellings replaced where the code says what they are.</summary>
    private static ImmutableArray<DiagnosticArgument> Respelled(string code, ImmutableArray<DiagnosticArgument> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return arguments;
        }

        var kind = arguments.FirstOrDefault(static argument => argument.Name == "kind").Value;

        return [.. arguments.Select(argument => argument with { Value = Respell(code, kind, argument) })];
    }

    private static string Respell(string code, string? kind, DiagnosticArgument argument) =>
        (code, argument.Name) switch
        {
            // Codes about an exchanger's sides, whatever they name.
            ("FS2109", "param") or ("FS2110", "param") or ("FS2112", "port")
                or ("FS2119", "inlet") or ("FS2119", "outlet") => Sides(argument.Value),
            ("FS2119", "side") => argument.Value switch
            {
                "1" => "primary",
                "2" => "secondary",
                _ => argument.Value,
            },

            // Codes that name the kind: respelled only for an exchanger.
            ("FS1503", "available") or ("FS1503", "parameter")
                or ("FS1505", "available") or ("FS1505", "port") when kind == Exchanger => Sides(argument.Value),

            // A worked example of a difference, which language 2 writes in K (D-172); dC stays, being language 2 too.
            ("FS1302", "example") => KelvinDifference.Replace(argument.Value, "K"),

            _ => argument.Value,
        };

    private static string Sides(string text) =>
        SecondSide.Replace(text, static match => match.Groups[1].Value == "in" ? "secondary.in" : "secondary.out");
}
