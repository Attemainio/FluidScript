using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

using FluidScript.Core.Model;

namespace FluidScript.Api.Contracts;

/// <summary>The one JSON convention for every endpoint, and the schema emitted from the wire records (<c>D-46</c>).</summary>
/// <remarks>
/// Every response body -- the model contract, the endpoint records around it, problem details -- is
/// written with <see cref="ModelContractJson.Options"/>'s settings, so a client learns one convention:
/// camelCase, declaration order, <see langword="null"/> written, absent-when-not-applicable honoured,
/// non-ASCII unescaped. Requests are read case-insensitively, as the web defaults are.
/// </remarks>
public static class ApiJson
{
    private static readonly JsonSerializerOptions IndentedSchema = new() { WriteIndented = true };

    /// <summary>Copies the contract's serializer settings onto the host's options.</summary>
    /// <param name="target">The host's serializer options, mutated in place.</param>
    public static void Configure(JsonSerializerOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var source = ModelContractJson.Options;
        target.PropertyNamingPolicy = source.PropertyNamingPolicy;
        target.DictionaryKeyPolicy = source.DictionaryKeyPolicy;
        target.DefaultIgnoreCondition = source.DefaultIgnoreCondition;
        target.Encoder = source.Encoder;
        target.WriteIndented = source.WriteIndented;
        target.NumberHandling = source.NumberHandling;
        target.TypeInfoResolver = source.TypeInfoResolver;
        target.PropertyNameCaseInsensitive = true;
    }

    /// <summary>Emits the JSON Schema of the model contract from its records (<c>D-46</c> step 2).</summary>
    /// <returns>The schema, indented, with a trailing newline, as it is committed.</returns>
    /// <remarks>
    /// The schema is a build product: it is committed beside the serializer and a test regenerates it
    /// under the goldens flag and fails when the committed copy differs, so a change to a record's
    /// name, order or nullability is a reviewed diff rather than a surprise on the other side of the
    /// wire. Every object node carries a <c>title</c> and documented members a <c>description</c>
    /// (<see cref="SchemaDocumentation"/>), which is what the TypeScript side is generated from.
    /// </remarks>
    public static string ModelContractSchema() => Emit(typeof(ModelContract));

    /// <summary>Emits the JSON Schema of the compile response, the model contract's envelope.</summary>
    /// <returns>The schema, indented, with a trailing newline.</returns>
    public static string CompileResponseSchema() => Emit(typeof(CompileResponse));

    /// <summary>Emits the JSON Schema of the metadata document.</summary>
    /// <returns>The schema, indented, with a trailing newline.</returns>
    public static string MetadataSchema() => Emit(typeof(MetadataWire));

    private static string Emit(Type root) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(ModelContractJson.Options, root, SchemaDocumentation.ExporterOptions)
            .ToJsonString(IndentedSchema) + "\n";

    /// <summary>Emits the lexicon the editor's tokenizer is generated from (<see cref="LexiconWire"/>).</summary>
    /// <returns>The JSON, indented, with a trailing newline, as it is committed as <c>language.json</c>.</returns>
    public static string Lexicon() =>
        JsonSerializer.Serialize(LexiconWire.Current, IndentedContract) + "\n";

    private static readonly JsonSerializerOptions IndentedContract = new(ModelContractJson.Options) { WriteIndented = true };

    /// <summary>Parses a schema back, for a test that reads one.</summary>
    /// <param name="json">The schema text.</param>
    /// <returns>The node, or <see langword="null"/> when the text is not JSON.</returns>
    public static JsonNode? ParseSchema(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
