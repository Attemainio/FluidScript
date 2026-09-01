using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>
/// One message about a script or the design it describes.
/// </summary>
/// <remarks>
/// <para>
/// This is the return value that replaces an exception everywhere in the pipeline. A script under
/// editing is malformed most of the time, so malformed input is an ordinary outcome: every stage
/// takes whatever the stage above it produced, however damaged, and returns its own output alongside
/// a list of these.
/// </para>
/// <para>
/// It is also the machine-readable surface an agent uses to correct a script it generated, which is
/// why <see cref="Code"/> is stable and <see cref="Span"/> is exact rather than line-granular.
/// </para>
/// </remarks>
public sealed record Diagnostic
{
    /// <summary>Gets the stable code identifying what this diagnostic is about.</summary>
    /// <value><c>FS</c> followed by four digits, for example <c>FS1302</c>. Never reused for another meaning.</value>
    public required string Code { get; init; }

    /// <summary>Gets how much this affects the element it is about.</summary>
    /// <value>Fixed by the code: one code is never emitted with two different severities.</value>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>Gets the message shown to the user, already formatted with its arguments.</summary>
    /// <value>
    /// Rendered from the code's template. Any quantity it mentions is in the unit the script wrote,
    /// not SI — telling someone their 30 kW heat exchanger has a problem at 30 000 W is telling them
    /// about a number they never wrote.
    /// </value>
    public required string Message { get; init; }

    /// <summary>Gets where in the source this is about.</summary>
    /// <value>
    /// <see langword="null"/> for a diagnostic about an inferred component or about the design as a
    /// whole, neither of which has source text behind it.
    /// </value>
    public TextSpan? Span { get; init; }

    /// <summary>Gets the name of the component this concerns, when it concerns one.</summary>
    /// <value>
    /// <see langword="null"/> for diagnostics about the script rather than a component. Every physical
    /// warning sets this even when <see cref="Span"/> is <see langword="null"/>, because the canvas
    /// badges the component whether or not the user wrote it.
    /// </value>
    public string? ComponentName { get; init; }

    /// <summary>Gets an offered fix, when one is unambiguous.</summary>
    /// <value><see langword="null"/> when no single replacement is certainly correct.</value>
    public Suggestion? Suggestion { get; init; }

    /// <summary>Gets the other places in the script this diagnostic is also about.</summary>
    /// <value>
    /// Empty for most diagnostics. Used for the earlier declaration behind a duplicate, and for every
    /// member of a dependency cycle.
    /// </value>
    public ImmutableArray<RelatedLocation> Related { get; init; } = [];

    /// <summary>Creates a diagnostic from its code's definition.</summary>
    /// <param name="descriptor">The code being emitted.</param>
    /// <param name="span">Where in the source this is about, or <see langword="null"/> when it is not about source text.</param>
    /// <param name="arguments">Named values for the message template's placeholders, in any order.</param>
    /// <returns>
    /// A diagnostic carrying the descriptor's code and severity and the rendered message. Callers
    /// needing a suggestion, a component name or related locations add them with a <c>with</c>
    /// expression, which keeps the common two-argument call short.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    public static Diagnostic Create(
        DiagnosticDescriptor descriptor,
        TextSpan? span,
        params ReadOnlySpan<DiagnosticArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new Diagnostic
        {
            Code = descriptor.Code,
            Severity = descriptor.Severity,
            Message = descriptor.Render(arguments),
            Span = span,
        };
    }
}
