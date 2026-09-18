using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FluidScript.Api.Contracts;

/// <summary>Puts the records' XML documentation onto the schema they emit (<c>D-46</c> step 4).</summary>
/// <remarks>
/// A schema node for a type gets a <c>title</c>, the type's name without the <c>Wire</c> suffix, so a
/// TypeScript generator names the interface rather than inlining it; a property node gets a
/// <c>description</c> from the member's <c>&lt;summary&gt;</c>, or from the record's
/// <c>&lt;param&gt;</c> when the property is a positional parameter. The text comes from the compiler's
/// documentation file beside each assembly, read once; an assembly without one contributes nothing.
/// </remarks>
public static partial class SchemaDocumentation
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> Files = new();

    /// <summary>The exporter options every schema is emitted with.</summary>
    public static JsonSchemaExporterOptions ExporterOptions { get; } = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = Transform,
    };

    /// <summary>Adds the title and description a node's type or property carries.</summary>
    /// <param name="context">Where the exporter is: the type, and the property when it is one.</param>
    /// <param name="node">The node as emitted.</param>
    /// <returns>The same node, with <c>title</c> and <c>description</c> added where they apply.</returns>
    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
    {
        if (node is not JsonObject schema)
        {
            return node;
        }

        // A property node first: its description is the member's, whatever its type.
        if (context.PropertyInfo is { } property)
        {
            // PropertyInfo.Name is the JSON name; the CLR member behind it is the attribute provider.
            var member = property.AttributeProvider as MemberInfo;
            var declaring = member?.DeclaringType ?? property.DeclaringType;
            var description = DescriptionOf(declaring, member?.Name ?? property.Name);
            if (description is not null)
            {
                schema.Insert(0, "description", description);
            }
        }

        // Then the type, when the node is an object: a title names the interface, and a type's own
        // summary is its description when no property already said what it is here.
        var type = context.TypeInfo.Type;
        if (schema.ContainsKey("properties"))
        {
            schema.Insert(0, "title", TitleOf(type));
            var summary = Lookup(type.Assembly, "T:" + FullName(type));
            if (summary is not null && !schema.ContainsKey("description"))
            {
                schema.Insert(1, "description", summary);
            }
        }

        return schema;
    }

    /// <summary>The schema title for a type: its name without the <c>Wire</c> suffix, or the generic name's stem.</summary>
    /// <param name="type">The type.</param>
    /// <returns>A name for the TypeScript interface.</returns>
    private static string TitleOf(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        return name.EndsWith("Wire", StringComparison.Ordinal) && name.Length > 4 ? name[..^4] : name;
    }

    private static string? DescriptionOf(Type declaring, string property)
    {
        var typeName = FullName(declaring);
        return Lookup(declaring.Assembly, $"P:{typeName}.{property}")
            ?? LookupParam(declaring.Assembly, "T:" + typeName, property);
    }

    private static string FullName(Type type)
    {
        var name = type.FullName ?? type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        return name.Replace('+', '.');
    }

    private static string? Lookup(Assembly assembly, string id) =>
        Load(assembly).TryGetValue(id, out var text) ? text : null;

    private static string? LookupParam(Assembly assembly, string typeId, string property)
    {
        // A positional record documents its properties as <param> on the type; the parameter name
        // is the property's with its first letter in either case.
        foreach (var candidate in new[] { property, char.ToLowerInvariant(property[0]) + property[1..] })
        {
            if (Load(assembly).TryGetValue($"{typeId}#{candidate}", out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> Load(Assembly assembly) =>
        Files.GetOrAdd(assembly, static a =>
        {
            var path = Path.ChangeExtension(a.Location, ".xml");
            if (string.IsNullOrEmpty(a.Location) || !File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in XDocument.Load(path).Descendants("member"))
            {
                var id = member.Attribute("name")?.Value;
                if (id is null)
                {
                    continue;
                }

                if (member.Element("summary") is { } summary)
                {
                    entries[id] = Flatten(summary);
                }

                foreach (var param in member.Elements("param"))
                {
                    var name = param.Attribute("name")?.Value;
                    if (name is not null)
                    {
                        entries[$"{id}#{name}"] = Flatten(param);
                    }
                }
            }

            return entries;
        });

    /// <summary>Turns a doc element into one line of plain text: inline tags reduced to their text, whitespace collapsed.</summary>
    /// <param name="element">The <c>summary</c> or <c>param</c> element.</param>
    /// <returns>The sentence, or an empty string.</returns>
    private static string Flatten(XElement element)
    {
        var text = new StringBuilder();
        Append(element, text);
        return Whitespace().Replace(text.ToString(), " ").Trim();
    }

    private static void Append(XElement element, StringBuilder text)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText leaf:
                    text.Append(leaf.Value);
                    break;
                case XElement { Name.LocalName: "see" or "seealso" } see:
                    var target = see.Attribute("langword")?.Value ?? see.Attribute("cref")?.Value ?? see.Value;
                    text.Append(target[(target.LastIndexOf('.') + 1)..].TrimEnd(')'));
                    break;
                case XElement child:
                    Append(child, text);
                    break;
                default:
                    break;
            }
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
