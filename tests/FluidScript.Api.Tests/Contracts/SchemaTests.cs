using FluidScript.Api.Contracts;
using FluidScript.Fixtures;

namespace FluidScript.Api.Tests.Contracts;

/// <summary>
/// <c>D-46</c> step 2: the JSON Schema emitted from the wire records is committed, and a build whose
/// records emit a different schema fails here until the committed copy is regenerated and reviewed.
/// </summary>
[Trait("Category", "Api")]
public sealed class SchemaTests
{
    private const string UpdateVariable = "FLUIDSCRIPT_UPDATE_GOLDENS";

    public static string Directory { get; } = Path.Combine(RepositoryLayout.Source, "FluidScript.Api", "Contracts", "Schemas");

    public static TheoryData<string> Schemas => ["model-contract", "compile-response", "metadata"];

    [Theory]
    [MemberData(nameof(Schemas))]
    public void TheCommittedSchemaIsWhatTheRecordsEmit(string name)
    {
        var actual = name switch
        {
            "model-contract" => ApiJson.ModelContractSchema(),
            "compile-response" => ApiJson.CompileResponseSchema(),
            _ => ApiJson.MetadataSchema(),
        };
        var path = Path.Combine(Directory, name + ".schema.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No committed schema '{name}'. Run once with {UpdateVariable}=1 to create it, then review it.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"The records emit a different '{name}' schema than the committed one. If the change is intended, run once with {UpdateVariable}=1 and review the diff.");
    }

    [Fact]
    public void TheModelContractSchemaNamesTheTopLevelFields()
    {
        var schema = ApiJson.ParseSchema(ApiJson.ModelContractSchema());

        Assert.NotNull(schema);
        var properties = schema["properties"]!.AsObject().Select(static p => p.Key).ToList();

        foreach (var field in new[] { "contractVersion", "provenance", "circuits", "components", "connections", "layout", "diagnostics" })
        {
            Assert.Contains(field, properties);
        }
    }
}
