using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using FluidScript.Core.Model;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>Renders the model contract's field reference from the wire records and their XML docs.</summary>
/// <remarks>
/// The wire records are the contract (<c>26</c>); a page listing them by hand would be a second copy
/// that drifts. This walks every record <see cref="ModelContract"/> reaches, in declaration order --
/// which is wire order -- and takes each field's meaning from the <c>FluidScript.Core.xml</c> the
/// build writes beside the assembly, so a field's one description is its doc comment.
/// </remarks>
public static partial class ContractPage
{
    public const string FieldsRegion = "contract-fields";

    private static readonly Lazy<ImmutableDictionary<string, string>> Summaries = new(LoadSummaries);

    public static string Render()
    {
        var builder = new StringBuilder();
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(ModelContract));

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (!seen.Add(type))
            {
                continue;
            }

            builder.AppendLine($"### `{Name(type)}`");
            builder.AppendLine();

            if (Summaries.Value.TryGetValue("T:" + type.FullName, out var summary))
            {
                builder.AppendLine(summary);
                builder.AppendLine();
            }

            builder.AppendLine("| Field | Type | Meaning |");
            builder.AppendLine("|---|---|---|");

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var (typeName, nested) = Describe(property.PropertyType);

                // A nullable reference type is not visible on the Type; the compiler's annotation is.
                if (!property.PropertyType.IsValueType
                    && new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable)
                {
                    typeName += " or `null`";
                }

                var absent = property.GetCustomAttribute<AbsentWhenNullAttribute>() is not null;
                var meaning = Summaries.Value.GetValueOrDefault("P:" + type.FullName + "." + property.Name, string.Empty);

                if (absent)
                {
                    meaning += " Absent when not applicable.";
                }

                builder.AppendLine($"| `{CamelCase(property.Name)}` | {typeName} | {meaning.Trim()} |");

                foreach (var next in nested)
                {
                    queue.Enqueue(next);
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static (string Name, ImmutableArray<Type> Nested) Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            var (name, nested) = Describe(inner);
            return (name + " or `null`", nested);
        }

        if (type == typeof(string))
        {
            return ("string", []);
        }

        if (type == typeof(int))
        {
            return ("integer", []);
        }

        if (type == typeof(double))
        {
            return ("number", []);
        }

        if (type == typeof(bool))
        {
            return ("boolean", []);
        }

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();

            if (arguments.Length == 2)
            {
                var (value, nested) = Describe(arguments[1]);
                return ($"object of {value}", nested);
            }

            var (element, inner2) = Describe(arguments[0]);
            return ($"array of {element}", inner2);
        }

        return type.Namespace == typeof(ModelContract).Namespace
            ? ($"[`{Name(type)}`](#{Name(type).ToLowerInvariant()})", [type])
            : (type.Name, []);
    }

    /// <summary>The record's name without the <c>Wire</c> suffix, which is Core's and not the reader's.</summary>
    private static string Name(Type type) =>
        type.Name.EndsWith("Wire", StringComparison.Ordinal) ? type.Name[..^4] : type.Name;

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static ImmutableDictionary<string, string> LoadSummaries()
    {
        var path = Path.ChangeExtension(typeof(ModelContract).Assembly.Location, ".xml");
        var document = XDocument.Load(path);

        return document.Descendants("member")
            .Where(static member => member.Element("summary") is not null)
            .ToImmutableDictionary(
                static member => member.Attribute("name")!.Value,
                static member => Prose(member.Element("summary")!),
                StringComparer.Ordinal);
    }

    /// <summary>Doc-comment XML as markdown: <c>&lt;c&gt;</c> to code, <c>&lt;see&gt;</c> to its name, the rest as text.</summary>
    private static string Prose(XElement summary)
    {
        var builder = new StringBuilder();

        foreach (var node in summary.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;

                case XElement { Name.LocalName: "c" } code:
                    builder.Append('`').Append(code.Value).Append('`');
                    break;

                case XElement { Name.LocalName: "see" } see:
                    var target = see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? string.Empty;
                    builder.Append('`').Append(target[(target.LastIndexOf('.') + 1)..]).Append('`');
                    break;

                case XElement { Name.LocalName: "paramref" or "typeparamref" } reference:
                    builder.Append('`').Append(CamelCase(reference.Attribute("name")?.Value ?? string.Empty)).Append('`');
                    break;

                case XElement other:
                    builder.Append(other.Value);
                    break;
            }
        }

        return Whitespace().Replace(builder.ToString(), " ").Trim().Replace("|", "\\|", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
