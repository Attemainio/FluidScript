using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Primitives;

/// <summary>Why a property request could not be answered.</summary>
/// <param name="Descriptor">The code this would be reported under, if anything reports it.</param>
/// <param name="Arguments">The values its message needs.</param>
/// <remarks>
/// <para>
/// <strong>An error is carried, not emitted</strong>, and that separation is <c>21</c>'s invariant 4.
/// A solver overshoots during iteration and asks for a state that does not exist, then backtracks;
/// emitting a diagnostic there would put hundreds of errors in the log for a circuit that solved
/// correctly. So this holds everything a diagnostic needs and leaves the decision to a caller that
/// knows whether the state was a trial point or the converged answer.
/// </para>
/// <para>
/// It carries no span because nothing at this layer has one. <see cref="At"/> is where a caller that
/// does supplies it.
/// </para>
/// </remarks>
public sealed record ResultError(DiagnosticDescriptor Descriptor, ImmutableArray<DiagnosticArgument> Arguments)
{
    /// <summary>Gets the code, such as <c>FS2003</c>.</summary>
    public string Code => Descriptor.Code;

    /// <summary>Gets the rendered message.</summary>
    public string Message => Descriptor.Render(Arguments.AsSpan());

    /// <summary>Builds the diagnostic this error would be reported as.</summary>
    /// <param name="span">Where in the source to anchor it, or <see langword="null"/> for a whole-model report.</param>
    /// <returns>The diagnostic.</returns>
    public Diagnostic At(TextSpan? span) => Diagnostic.Create(Descriptor, span, Arguments.AsSpan());

    /// <summary>Gets the diagnostics this error stands for, when it stands for several.</summary>
    /// <value>
    /// Empty for an error that is its own report. A stage that refuses for reasons it has already
    /// diagnosed -- the well-posedness check, with one code, component and range per finding -- puts
    /// them here, so that a caller reports <em>those</em> rather than one error carrying their text
    /// with the code, the component and the range lost (<c>S-65</c>).
    /// </value>
    public ImmutableArray<Diagnostic> Diagnostics { get; init; } = [];

    /// <summary>What a caller reports for this error: the diagnostics it stands for, or itself.</summary>
    /// <param name="span">Where to anchor the error's own diagnostic, when it is reported.</param>
    /// <returns>
    /// <see cref="Diagnostics"/> when at least one of them is an error; the error's own diagnostic when
    /// there are none; both when they are all warnings, so that a refusal always shows as an error and
    /// the warnings that explain it are not lost.
    /// </returns>
    public ImmutableArray<Diagnostic> Report(TextSpan? span)
    {
        if (Diagnostics.IsDefaultOrEmpty)
        {
            return [At(span)];
        }

        return Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? Diagnostics
            : Diagnostics.Add(At(span));
    }

    /// <summary>Builds an error from a descriptor and its arguments.</summary>
    /// <param name="descriptor">The code.</param>
    /// <param name="arguments">Its message's values, as name and text pairs.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static ResultError From(DiagnosticDescriptor descriptor, params (string Name, string Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(arguments);

        var built = ImmutableArray.CreateBuilder<DiagnosticArgument>(arguments.Length);

        foreach (var (name, value) in arguments)
        {
            built.Add(new DiagnosticArgument(name, value));
        }

        return new ResultError(descriptor, built.ToImmutable());
    }
}
