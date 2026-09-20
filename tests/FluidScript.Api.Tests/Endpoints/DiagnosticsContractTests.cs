using FluidScript.Api.Contracts;
using FluidScript.Core.Model;
using FluidScript.Fixtures;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// <c>44</c> on the wire: both position forms agree over the whole corpus, a multi-byte character
/// before an error moves nothing, and the list is ordered by severity then offset.
/// </summary>
[Trait("Category", "Api")]
public sealed class DiagnosticsContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Validate = "/api/v1/validate";

    [Fact]
    public async Task LineCharacterAndOffsetLengthDescribeTheSameSpanOverTheCorpus()
    {
        using var client = factory.CreateClient();
        var checked_ = 0;

        foreach (var path in ScriptCorpus.EnumerateSampleFiles())
        {
            var script = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            using var response = await client.PostAsync(Validate, new { script });
            var body = await response.ReadAsync<ValidateResponse>();

            foreach (var diagnostic in body.Diagnostics)
            {
                foreach (var range in Ranges(diagnostic))
                {
                    AssertAgree(script, range, Path.GetFileName(path) + " " + diagnostic.Code);
                    checked_++;
                }
            }
        }

        Assert.True(checked_ > 0, "the corpus produced no diagnostic with a range");
    }

    [Fact]
    public async Task AMultiByteCharacterInACommentMovesNoSquiggleAfterIt()
    {
        // `character` counts UTF-16 code units, as the editor does; the emoji is two of them. `zzz` is
        // nothing the registry can read as a parameter, so it is FS1503 rather than a corrected spelling.
        const string Script = "fluidscript 1\n# \U0001F525 hot\nHE1 heat_exchanger zzz=30\n";
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Validate, new { script = Script });
        var body = await response.ReadAsync<ValidateResponse>();

        var unknown = Assert.Single(body.Diagnostics, static d => d.Code == "FS1503");
        Assert.NotNull(unknown.Range);
        Assert.Equal(3, unknown.Range.Length); // the name alone, never `zzz=30` (L-53)
        Assert.True(unknown.Range.Length >= 3, "the span does not cover the name"); // the binder spans `zzz=30`, not `zzz` alone: L-53
        Assert.Equal(2, unknown.Range.Start.Line);
        Assert.Equal("HE1 heat_exchanger ".Length, unknown.Range.Start.Character);
        AssertAgree(Script, unknown.Range, "FS1503");
    }

    [Fact]
    public async Task DiagnosticsAreOrderedBySeverityThenOffset()
    {
        // 44's worked example: the unit error is produced before the binder's, and read after it.
        const string Script = "fluidscript 1\nHE1 heat_exchanger zzz=30 in.t=20 out.t=20C+30C\n";
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(Validate, new { script = Script });
        var body = await response.ReadAsync<ValidateResponse>();

        var errors = body.Diagnostics.Where(static d => d.Severity == "error").ToList();
        Assert.Contains(errors, static d => d.Code == "FS1503");
        Assert.Contains(errors, static d => d.Code == "FS1302");
        Assert.True(errors.FindIndex(static d => d.Code == "FS1503") < errors.FindIndex(static d => d.Code == "FS1302"));

        var keys = body.Diagnostics.Select(static d => (Rank(d.Severity), d.Range?.Offset ?? int.MaxValue)).ToList();
        Assert.Equal(keys.OrderBy(static k => k.Item1).ThenBy(static k => k.Item2), keys);
    }

    [Fact]
    public async Task ARefusedSolveReportsTheCheckOwnDiagnosticsWithTheirCodesAndComponents()
    {
        // S-65: a script the well-posedness check refuses used to come back as one FS2004 carrying the
        // check's text, with the code, the component and the range of the real finding lost. The
        // boundary wired twice is FS2205, naming the node.
        const string Script = """
            fluidscript 1
            circuit probe
            fluid water

            S1  inlet t=60 p=300
            R1  outlet p=280
            HE1 heat_exchanger power=-20
            HE2 heat_exchanger power=-10

            connections
            S1 - HE1 - R1
            S1 - HE2 - R1
            """;
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/solve", new { sessionId = "s65", script = Script });
        var body = await response.ReadAsync<CompileResponse>();
        var diagnostics = body.Model!.Diagnostics;

        var fanOut = diagnostics.Where(static d => d.Code == "FS2205").ToList();
        Assert.Equal(2, fanOut.Count);
        Assert.Contains(fanOut, static d => d.Component == "S1");
        Assert.Contains(fanOut, static d => d.Component == "R1");
        // The range is L-55's: the check works on the graph, which carries no spans; the component is enough for the badge and the log.
        Assert.DoesNotContain(diagnostics, static d => d.Code == "FS2004");
        Assert.False(body.Model.Solve?.Converged ?? false);
    }

    [Fact]
    public async Task EveryEmittedCodeResolvesInMetadata()
    {
        using var client = factory.CreateClient();
        using var metadata = await client.GetAsync("/api/v1/metadata", TestContext.Current.CancellationToken);
        var codes = (await metadata.ReadAsync<MetadataWire>()).Diagnostics.Select(static d => d.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var path in ScriptCorpus.EnumerateSampleFiles())
        {
            using var response = await client.PostAsync(Validate, new { script = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken) });
            var body = await response.ReadAsync<ValidateResponse>();
            Assert.All(body.Diagnostics, diagnostic => Assert.Contains(diagnostic.Code, codes));
        }
    }

    private static IEnumerable<RangeWire> Ranges(DiagnosticWire diagnostic)
    {
        if (diagnostic.Range is { } range)
        {
            yield return range;
        }

        if (diagnostic.Suggestion is { } suggestion)
        {
            yield return suggestion.Range;
        }

        foreach (var related in diagnostic.Related)
        {
            yield return related.Range;
        }
    }

    private static void AssertAgree(string script, RangeWire range, string context)
    {
        var (line, character) = Position(script, range.Offset);
        Assert.True(range.Start.Line == line && range.Start.Character == character,
            $"{context}: offset {range.Offset} is line {line} character {character}, not {range.Start.Line}:{range.Start.Character}");

        var (endLine, endCharacter) = Position(script, range.Offset + range.Length);
        Assert.True(range.End.Line == endLine && range.End.Character == endCharacter,
            $"{context}: end offset {range.Offset + range.Length} is line {endLine} character {endCharacter}, not {range.End.Line}:{range.End.Character}");
    }

    private static (int Line, int Character) Position(string script, int offset)
    {
        var line = 0;
        var lineStart = 0;

        for (var i = 0; i < offset && i < script.Length; i++)
        {
            if (script[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart);
    }

    private static int Rank(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2,
    };
}
