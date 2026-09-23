using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Registry;

/// <summary>The ceilings one request may reach (<c>07</c>'s input limits), and the checks against them.</summary>
/// <param name="SourceBytes">The largest source text accepted, bytes of UTF-8. Default 1 MiB.</param>
/// <param name="Declarations">The most component declarations one script may carry. Default 10 000.</param>
/// <param name="Tokens">The most tokens one script may lex to. Default 100 000.</param>
/// <param name="Unknowns">The most solver unknowns one solve may carry. Default 800.</param>
/// <remarks>
/// <para>
/// The defaults are <c>07</c>'s and a host reports them through its metadata, so a client can read
/// the ceiling it will hit before it hits it. A host may lower them; nothing here stops it raising
/// them, though <c>07</c>'s budgets were measured under these.
/// </para>
/// <para>
/// Every check is a return value (<c>FS4601</c>), never a throw: a script over a limit is a script
/// under editing like any other, and the pipeline stops before the solve rather than refusing the
/// request. The source-size limit is the transport's to enforce and is carried here only so that
/// the four ceilings are stated in one place.
/// </para>
/// </remarks>
public sealed record InputLimits(
    long SourceBytes = 1024 * 1024,
    int Declarations = 10_000,
    int Tokens = 100_000,
    int Unknowns = 800)
{
    /// <summary>Gets <c>07</c>'s defaults.</summary>
    public static InputLimits Default { get; } = new();

    /// <summary>Checks a parsed script against the declaration and token ceilings.</summary>
    /// <param name="root">The tree.</param>
    /// <returns>One <c>FS4601</c> per ceiling exceeded; empty when the script is within both.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public ImmutableArray<Diagnostic> Check(ScriptSyntax root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var declarations = root.Statements.Count(static statement => statement is ComponentDeclarationSyntax);
        var tokens = root.Tokens.Length;
        var findings = ImmutableArray.CreateBuilder<Diagnostic>(2);

        if (declarations > Declarations)
        {
            findings.Add(Over("declarations", declarations, Declarations));
        }

        if (tokens > Tokens)
        {
            findings.Add(Over("tokens", tokens, Tokens));
        }

        return findings.ToImmutable();
    }

    /// <summary>Checks a counted system against the unknown ceiling.</summary>
    /// <param name="unknowns">What the counting table says the solve would carry.</param>
    /// <returns><c>FS4601</c> when over the ceiling; otherwise <see langword="null"/>.</returns>
    public Diagnostic? CheckUnknowns(int unknowns) =>
        unknowns > Unknowns ? Over("unknowns", unknowns, Unknowns) : null;

    private static Diagnostic Over(string what, int count, int max) =>
        Diagnostic.Create(
            LimitDiagnostics.OverLimit,
            span: null,
            new DiagnosticArgument("count", count.ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument("what", what),
            new DiagnosticArgument("max", max.ToString(CultureInfo.InvariantCulture)));
}
