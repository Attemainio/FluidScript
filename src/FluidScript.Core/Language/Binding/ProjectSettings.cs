using System.Collections.Immutable;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Binding;

/// <summary>File-wide settings from the <c>project</c> directive (<c>D-37</c>).</summary>
/// <param name="Name">The project's name, or <see langword="null"/> when no directive states one.</param>
/// <param name="DefaultMode">
/// The default solve mode for every circuit, or <see langword="null"/> when the directive states none.
/// </param>
/// <remarks>
/// Spacing is deliberately not here. <c>D-37</c> puts it in <see cref="StyleSettings"/>, and a second
/// home would create two paths for one value: the one that gets serialized and the one that does not.
/// </remarks>
public sealed record ProjectSettings(string? Name, FluidMode? DefaultMode)
{
    /// <summary>Gets each driver's value at the design condition (<c>D-58</c>).</summary>
    /// <value>
    /// Keyed by canonical driver name, so <c>design tout=-26</c> and <c>design outdoor=-26</c> land in
    /// one entry. Empty for a file that states no design point, which is every file that solves in
    /// time and reads no curve.
    /// </value>
    /// <remarks>
    /// It sits here rather than beside the curves because it is an input to the physics that outlives
    /// them: <c>D-58</c> makes it the <em>sizing</em> point in every mode, and the operating point as
    /// well only in a static solve.
    /// </remarks>
    public ImmutableDictionary<string, DesignValue> Design { get; init; } =
        ImmutableDictionary<string, DesignValue>.Empty;

    /// <summary>Gets the operating cases the plant is sized for, in the order written (<c>D-143</c>).</summary>
    /// <value>
    /// Empty for a file with no <c>scenarios</c> line, which is every file written before P6.8 and
    /// every file that needs only one case. <strong>The order is the binding</strong>: an array
    /// parameter's element <c>i</c> belongs to the name at position <c>i</c> and to nothing else.
    /// </value>
    public ImmutableArray<string> Scenarios { get; init; } = [];

    /// <summary>Gets the scenario the file operates at, from <c>design &lt;name&gt;</c> (<c>D-143</c>).</summary>
    /// <value>
    /// A name in <see cref="Scenarios"/>, or <see langword="null"/> when no scenarios are declared.
    /// Never null when they are: <c>FS1543</c> refuses a scenario list with no <c>design</c>, because
    /// a first column is a position and not a decision.
    /// </value>
    /// <remarks>
    /// This names where the plant <em>operates</em> and nothing about how it is sized. Every scenario
    /// is sized for; this one supplies the numbers the canvas draws, an export carries and a run
    /// starts from.
    /// </remarks>
    public string? DesignScenario { get; init; }

    /// <summary>Gets where a run's t = 0 sits on the time axis of every time curve, from <c>start=</c> (<c>D-149</c>).</summary>
    /// <value>
    /// s since the Unix epoch, as a time curve's rows are read, or <see langword="null"/> when the
    /// project line states none. A dynamic circuit reading a time curve needs one: a run then reads each
    /// curve at <c>Start + t</c>.
    /// </value>
    public double? Start { get; init; }

    /// <summary>Gets the position of <see cref="DesignScenario"/> in <see cref="Scenarios"/>.</summary>
    /// <value>Its index, or <c>-1</c> when no scenarios are declared or the name is not one of them.</value>
    public int DesignScenarioIndex =>
        DesignScenario is { } named ? Scenarios.IndexOf(named) : -1;
}
