namespace FluidScript.Core.Diagnostics;

/// <summary>
/// A second place in the script that a diagnostic is also about.
/// </summary>
/// <param name="Span">Where in the script this location is.</param>
/// <param name="Message">
/// What this location contributes, phrased as a fragment the editor shows beside it — <c>first
/// declared here</c> rather than a second full sentence.
/// </param>
/// <remarks>
/// This carries more weight than its size suggests. A dependency cycle with four participants is
/// unactionable when the diagnostic points at one of them; with all four as related locations the
/// editor can highlight the whole loop, which is the difference between a message a user can act on
/// and one they have to reconstruct by hand.
/// </remarks>
public sealed record RelatedLocation(TextSpan Span, string Message);
