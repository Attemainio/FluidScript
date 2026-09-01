namespace FluidScript.Core.Diagnostics;

/// <summary>
/// A diagnostic code that was allocated, is no longer emitted, and may never be allocated again.
/// </summary>
/// <param name="Code">The retired code, <c>FS</c> followed by exactly four digits.</param>
/// <param name="Reason">
/// What the code used to mean and why it stopped being emitted, in one sentence a maintainer reading
/// an old log or an old test can act on.
/// </param>
/// <remarks>
/// <para>
/// The registry carries these explicitly rather than dropping them, so that a lookup can tell
/// <em>never allocated</em> from <em>allocated and retired</em>. Those two cases want opposite
/// responses: the first is a typo, the second is a reference to a rule that changed.
/// </para>
/// <para>
/// The dangerous case, and the reason this type exists at all, is a code whose old condition now
/// parses cleanly. Every existing reference to it silently becomes wrong, and nothing announces it.
/// A nearby surviving condition therefore takes a new number instead of inheriting the vacated one;
/// the test is whether the <em>meaning</em> changed, not whether the new condition is close by.
/// </para>
/// </remarks>
public sealed record RetiredDiagnostic(string Code, string Reason);
