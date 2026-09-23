using FluidScript.Api.Contracts;
using FluidScript.Core.Model.Contract;
using FluidScript.Fixtures;

namespace FluidScript.Api.Tests.Contracts;

/// <summary>Checked-in payloads, regenerated only on purpose.</summary>
/// <remarks>
/// <para>
/// A golden file is the contract as it stands: any change to a field, a unit, an order or a rounding
/// shows up as a diff a reviewer reads, which is the whole point (<c>26</c>'s "a contract change fails
/// it visibly"). It is regenerated only with <c>FLUIDSCRIPT_UPDATE_GOLDENS=1</c> in the environment,
/// so a test run cannot quietly accept a drift; the diff is then reviewed in the commit.
/// </para>
/// <para>
/// The indented form is what is checked in, because a reviewer diffs it; the compact form is the
/// wire's, and a separate test holds that both round-trip byte for byte.
/// </para>
/// </remarks>
public static class Goldens
{
    private const string UpdateVariable = "FLUIDSCRIPT_UPDATE_GOLDENS";

    public static string Directory { get; } =
        Path.Combine(RepositoryLayout.Tests, "FluidScript.Api.Tests", "Contracts", "Goldens");

    /// <summary>Asserts the contract matches its golden file, or rewrites the file when asked to.</summary>
    public static void Assert(string name, ModelContract contract)
    {
        var path = Path.Combine(Directory, name + ".json");
        var actual = ModelContractJson.Serialize(contract, indented: true) + "\n";

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Xunit.Assert.True(File.Exists(path), $"No golden '{name}'. Run once with {UpdateVariable}=1 to create it, then review it.");

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return;
        }

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var first = 0;

        while (first < expectedLines.Length && first < actualLines.Length
            && string.Equals(expectedLines[first], actualLines[first], StringComparison.Ordinal))
        {
            first++;
        }

        Xunit.Assert.Fail(
            $"Golden '{name}' differs at line {first + 1}.\n"
            + $"  expected: {(first < expectedLines.Length ? expectedLines[first] : "<end>")}\n"
            + $"  actual:   {(first < actualLines.Length ? actualLines[first] : "<end>")}\n"
            + $"If the change is intended, run once with {UpdateVariable}=1 and review the diff.");
    }
}
