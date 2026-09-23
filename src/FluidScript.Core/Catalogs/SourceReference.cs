namespace FluidScript.Core.Catalogs;

/// <summary>One public document a catalogue row was read from.</summary>
/// <param name="Publisher">Who published it — a manufacturer, not a standards body's paywall.</param>
/// <param name="Url">The public URL it was read from.</param>
/// <param name="Retrieved">The date it was read, because a manufacturer's catalogue is revised.</param>
public sealed record SourceReference(string Publisher, string Url, DateOnly Retrieved);
