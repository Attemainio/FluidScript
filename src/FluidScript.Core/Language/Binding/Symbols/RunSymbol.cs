using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One run: where it starts, how long it runs, and what happens to the plant during it (<c>D-169</c>).</summary>
/// <remarks>
/// A language 2 file may hold several, and the interface plays the one chosen; the model says what the plant
/// is, and nothing here changes it. <see cref="RunProjection"/> turns the model and one run into the model that
/// run solves.
/// </remarks>
public sealed record RunSymbol
{
    /// <summary>Gets the run's title, as written between the quotes.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the case whose steady solve is the starting state (<c>D-141</c>), by position in <see cref="ProjectSettings.Scenarios"/>.</summary>
    /// <value><see langword="null"/> when the file declares no cases; the first case when <c>from</c> is not written.</value>
    public int? From { get; init; }

    /// <summary>Gets where t = 0 sits on every curve of time (<c>D-149</c>).</summary>
    /// <value>s since the Unix epoch, or <see langword="null"/> when the run states no <c>start</c>.</value>
    public double? Start { get; init; }

    /// <summary>Gets the simulated time.</summary>
    /// <value>s; positive. Default 600, <c>33</c>'s horizon.</value>
    public double Duration { get; init; } = 600;

    /// <summary>Gets the simulated time between kept states.</summary>
    /// <value>s; positive. Default 1, <c>33</c>'s frame interval.</value>
    public double Frame { get; init; } = 1;

    /// <summary>Gets the circuits held quasi-steady in this run, by name; every other circuit is dynamic.</summary>
    public ImmutableArray<string> Steady { get; init; } = [];

    /// <summary>Gets the run's steps and ramps, an override being a step at t = 0.</summary>
    public ImmutableArray<DisturbanceSymbol> Events { get; init; } = [];

    /// <summary>Gets each driver the run hands to a curve of time, by the driver's name: <c>outdoor = weather_jan</c>.</summary>
    /// <value>The curve each driver follows in this run, by name.</value>
    /// <remarks>Every curve of the driver then follows the clock through it (<c>D-149</c>'s composition, <c>19</c> §Drivers and cases).</remarks>
    public ImmutableDictionary<string, string> DriverCurves { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>Gets where the run's head sits in the source.</summary>
    public required TextSpan Span { get; init; }
}
