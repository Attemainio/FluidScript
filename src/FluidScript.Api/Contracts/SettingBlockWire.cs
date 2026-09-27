using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>A language 2 block and the settings it takes (<c>19</c>).</summary>
/// <param name="Name"><c>project</c>, <c>circuit</c>, <c>run</c>, <c>style</c> or <c>controller</c>.</param>
/// <param name="Settings">Its settings, in <c>19</c>'s order. A run takes any other <c>name =</c> as an override; a controller's replace the kind's registry parameters.</param>
public sealed record SettingBlockWire(string Name, ImmutableArray<SettingWire> Settings);
