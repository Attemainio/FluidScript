using System.Collections.Immutable;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Binding;

/// <summary>File-wide settings from the <c>project</c> block (<c>D-37</c>, <c>19</c>).</summary>
/// <param name="Name">The project's title, or <see langword="null"/> when no block states one.</param>
/// <remarks>
/// Spacing is deliberately not here. <c>D-37</c> puts it in <see cref="StyleSettings"/>, and a second
/// home would create two paths for one value: the one that gets serialized and the one that does not.
/// </remarks>
public sealed record ProjectSettings(string? Name)
{
    /// <summary>Gets the operating cases the plant is sized for, in the order written (<c>D-143</c>).</summary>
    /// <value>
    /// Empty for a file whose project block states no <c>cases</c>, which is every file that needs only one
    /// case. <strong>The order is the binding</strong>: an array
    /// parameter's element <c>i</c> belongs to the name at position <c>i</c> and to nothing else.
    /// </value>
    public ImmutableArray<string> Scenarios { get; init; } = [];

    /// <summary>Gets the case the file operates at: the first <c>cases</c> names (<c>D-143</c>, <c>D-175</c>).</summary>
    /// <value>A name in <see cref="Scenarios"/>, or <see langword="null"/> when no cases are declared.</value>
    /// <remarks>
    /// This names where the plant <em>operates</em> and nothing about how it is sized. Every scenario
    /// is sized for; this one supplies the numbers the canvas draws, an export carries and a run
    /// starts from.
    /// </remarks>
    public string? DesignScenario { get; init; }

    /// <summary>Gets where a run's t = 0 sits on the time axis of every time curve, from a run's <c>start</c> (<c>D-149</c>, <c>D-169</c>).</summary>
    /// <value>
    /// s since the Unix epoch, as a time curve's rows are read, or <see langword="null"/> when the
    /// run states none. A dynamic circuit reading a time curve needs one: a run then reads each
    /// curve at <c>Start + t</c>.
    /// </value>
    public double? Start { get; init; }

    /// <summary>Gets the position of <see cref="DesignScenario"/> in <see cref="Scenarios"/>.</summary>
    /// <value>Its index, or <c>-1</c> when no scenarios are declared or the name is not one of them.</value>
    public int DesignScenarioIndex =>
        DesignScenario is { } named ? Scenarios.IndexOf(named) : -1;
}
