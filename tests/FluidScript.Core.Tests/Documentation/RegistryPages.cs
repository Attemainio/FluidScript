using System.Text;

using FluidScript.Core.Language;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>
/// Renders the generated regions of <c>docs/functions/properties.md</c> and
/// <c>docs/functions/tags.md</c> from the component registry.
/// </summary>
/// <remarks>
/// Generated because they are the registry, restated. A hand-maintained list of every referenceable
/// property drifts the first time a kind gains one, and a reader who writes <c>PU1.power</c> because a
/// page promised it gets an error naming a property the page says exists.
/// </remarks>
public static class RegistryPages
{
    /// <summary>Identifies the generated region listing every referenceable property.</summary>
    public const string PropertiesRegion = "component-properties";

    /// <summary>Identifies the generated region listing every kind's tag code.</summary>
    public const string TagsRegion = "tag-codes";

    /// <summary>Renders every property, by kind.</summary>
    /// <returns>A markdown table of kind, property, unit and when the value exists.</returns>
    public static string RenderProperties()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Kind | Property | Unit | Available |");
        builder.AppendLine("|---|---|---|---|");

        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            foreach (var property in kind.Properties.Values.OrderBy(static p => p.Name, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"| `{kind.Keyword}` | `{property.Name}` | {Unit(property)} | {Availability(property)} |");
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>Renders every kind's tag code, with the tag it produces.</summary>
    /// <returns>A markdown table of kind, code and an example tag in circuit 400.</returns>
    public static string RenderTags()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Kind | Code | Tag in circuit 400 |");
        builder.AppendLine("|---|---|---|");

        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            var code = kind.TagCode is null ? "*none*" : $"`{kind.TagCode}`";
            var example = kind.TagCode is null ? "untagged" : $"`400{kind.TagCode}01`";

            builder.AppendLine($"| `{kind.Keyword}` | {code} | {example} |");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string Unit(PropertyInfo property) =>
        string.IsNullOrEmpty(property.CanonicalUnit) ? "—" : $"`{property.CanonicalUnit}`";

    private static string Availability(PropertyInfo property) => property.Availability switch
    {
        PropertyAvailability.Declared => "as written",
        PropertyAvailability.Sized => "after sizing",
        _ => "after the solve",
    };
}
