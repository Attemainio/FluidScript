using FluidScript.Fixtures;

namespace FluidScript.Api.Tests;

/// <summary>
/// Holds the API test project open at M0, so that the second test project is discovered and run by a
/// bare <c>dotnet test</c> from the repository root rather than being added later and found not to be.
/// </summary>
/// <remarks>
/// Endpoint and contract tests need <c>Microsoft.AspNetCore.Mvc.Testing</c> and a
/// <c>WebApplicationFactory</c>; both arrive with the REST contract in P5.2 of
/// <c>plan/00-foundation/08-implementation-sequence.md</c>. Taking that dependency now would add a
/// package with nothing to exercise.
/// </remarks>
public sealed class HostCompositionTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ApiHostAssembly_IsReachableFromTheTestProject()
    {
        var host = typeof(Program).Assembly;

        Assert.Equal("FluidScript.Api", host.GetName().Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TestTreeMirrorsTheSourceTree()
    {
        // 03-repository-layout's invariant 3. Asserted from the API test project because it is the
        // one that would otherwise never run in a bare `dotnet test` if it were not discovered.
        foreach (var project in new[] { "FluidScript.Core", "FluidScript.Api" })
        {
            var sourceProject = Path.Combine(RepositoryLayout.Source, project);
            var testProject = Path.Combine(RepositoryLayout.Tests, $"{project}.Tests");

            Assert.True(Directory.Exists(sourceProject), $"Missing source project: {sourceProject}");
            Assert.True(Directory.Exists(testProject), $"Missing mirrored test project: {testProject}");
        }
    }
}
