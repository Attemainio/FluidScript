using System.Net;

using FluidScript.Api.Contracts;
using FluidScript.Api.Pipeline;
using FluidScript.Api.Sessions;

using Microsoft.Extensions.DependencyInjection;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>
/// <c>41</c>'s sessions: a newer draft cancels the older within one iteration, a disconnect leaves no
/// work running, a session is a cache and never an answer, and the warm start it holds is used.
/// </summary>
[Trait("Category", "Api")]
public sealed class SessionTests
{
    private const string Compile = "/api/v1/compile";

    [Fact]
    public async Task ASupersedingRequestCancelsThePreviousSolveWithinOneIteration()
    {
        var solvers = new CountingSolverFactory { Delay = TimeSpan.FromMilliseconds(50), Steps = 20 };
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<ISolverFactory>(solvers) };
        using var client = host.CreateClient();
        var script = Api.Sample("m2-cooling-loop.fluid");

        var first = client.PostWithTokenAsync(Compile, new { sessionId = "typing", script }, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        using var second = await client.PostWithTokenAsync(Compile, new { sessionId = "typing", script }, TestContext.Current.CancellationToken);
        using var superseded = await first;

        Assert.Equal((HttpStatusCode)499, superseded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var older = solvers.Created.First();
        Assert.NotNull(older.CancelledAt);
        Assert.Equal(older.CancelledAt, older.Iterations);
        Assert.True(older.Iterations < 20, $"the older solve ran {older.Iterations} steps after being superseded");
    }

    [Fact]
    public async Task ADisconnectedClientLeavesNoRunningWork()
    {
        var solvers = new CountingSolverFactory { Delay = TimeSpan.FromMilliseconds(50), Steps = 40 };
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<ISolverFactory>(solvers) };
        using var client = host.CreateClient();
        using var gone = new CancellationTokenSource();

        // Let the request reach the solver, then hang up on it.
        var request = client.PostWithTokenAsync(Compile, new { sessionId = "gone", script = Api.Sample("m2-cooling-loop.fluid") }, gone.Token);
        var solver = await WaitForAsync(() => solvers.Created.FirstOrDefault(static s => s.Iterations > 0));
        Assert.NotNull(solver);
        await gone.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.NotNull(await WaitForAsync(() => solver.CancelledAt is null ? null : "cancelled"));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(solver.CancelledAt, solver.Iterations);

        var session = host.Services.GetRequiredService<ISessionStore>().GetOrCreate(new SessionKey(1, "gone"));
        Assert.NotNull(await WaitForAsync(() => session.HasDraftInFlight ? null : "released"));
    }

    [Fact]
    public async Task TheSecondRequestOnASessionStartsFromTheFirstsSolution()
    {
        var solvers = new CountingSolverFactory();
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<ISolverFactory>(solvers) };
        using var client = host.CreateClient();
        // The storage header settles in one pass, so the warm request's first pass and the cold request's
        // last are the same graph and the warm start is a converged point of it. The cooling loop, used
        // before `D-122`, takes two passes whose valve Kv differs, and a warm start from the second's
        // solution is no nearer the first's than the seed: 5 iterations either way.
        var script = Api.Sample("m4-storage-header.fluid");

        using var first = await client.PostAsync(Compile, new { sessionId = "warm", script });
        using var second = await client.PostAsync(Compile, new { sessionId = "warm", script });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var cold = solvers.Created.ElementAt(0);
        var warm = solvers.Created.ElementAt(1);
        Assert.NotEmpty(cold.Calls);
        Assert.NotEmpty(warm.Calls);

        // The warm request's first pass began where the cold request's last pass ended, and needed fewer Newton iterations than a cold first pass.
        Assert.Equal(cold.Calls[^1].Solution.Values, warm.Calls[0].Guess.Values);
        Assert.True(warm.Calls[0].NewtonIterations < cold.Calls[0].NewtonIterations,
            $"warm first pass took {warm.Calls[0].NewtonIterations} iterations, cold took {cold.Calls[0].NewtonIterations}");

        // And the answer is the same one, to the tolerance a warm start converges within.
        AssertSameAnswer(await first.ReadJsonAsync(), await second.ReadJsonAsync());
    }

    private static void AssertSameAnswer(System.Text.Json.JsonDocument a, System.Text.Json.JsonDocument b)
    {
        var left = a.RootElement.GetProperty("model").GetProperty("components").EnumerateArray().ToList();
        var right = b.RootElement.GetProperty("model").GetProperty("components").EnumerateArray().ToList();
        Assert.Equal(left.Count, right.Count);

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].TryGetProperty("state", out var x) || x.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            var y = right[i].GetProperty("state");

            foreach (var field in x.EnumerateObject())
            {
                if (field.Value.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !field.Value.TryGetProperty("value", out var v) || v.ValueKind != System.Text.Json.JsonValueKind.Number)
                {
                    continue;
                }

                var w = y.GetProperty(field.Name).GetProperty("value").GetDouble();
                Assert.True(
                    Math.Abs(v.GetDouble() - w) <= 1e-3 * Math.Max(1, Math.Abs(w)),
                    $"{left[i].GetProperty("id")}.{field.Name}: {v.GetDouble()} warm vs {w} cold");
            }
        }
    }

    [Fact]
    public async Task AWhitespaceEditStillWarmStarts()
    {
        var solvers = new CountingSolverFactory();
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<ISolverFactory>(solvers) };
        using var client = host.CreateClient();
        var script = Api.Sample("m2-cooling-loop.fluid");

        using var first = await client.PostAsync(Compile, new { sessionId = "ws", script });
        using var second = await client.PostAsync(Compile, new { sessionId = "ws", script = script + "\n\n# a comment\n" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(solvers.Created.ElementAt(0).Calls[^1].Solution.Values, solvers.Created.ElementAt(1).Calls[0].Guess.Values);
    }

    [Fact]
    public async Task AChangedTopologyStartsCold()
    {
        var solvers = new CountingSolverFactory();
        using var host = new ApiFactory { Overrides = s => s.AddSingleton<ISolverFactory>(solvers) };
        using var client = host.CreateClient();

        using var first = await client.PostAsync(Compile, new { sessionId = "topo", script = Api.Sample("m2-cooling-loop.fluid") });
        using var second = await client.PostAsync(Compile, new { sessionId = "topo", script = Api.Sample("m2-simple-loop.fluid") });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var previous = solvers.Created.ElementAt(0).Calls[^1].Solution.Values;
        var next = solvers.Created.ElementAt(1).Calls[0].Guess.Values;
        Assert.NotEqual(previous.Length, next.Length);
    }

    [Fact]
    public async Task DeletingEverySessionChangesNoResponse()
    {
        using var host = new ApiFactory();
        using var client = host.CreateClient();
        var script = Api.Sample("m2-distribution-header.fluid");

        using var before = await client.PostAsync(Compile, new { sessionId = "cache", script });
        var store = host.Services.GetRequiredService<ISessionStore>();
        Assert.True(store.Count >= 1);
        store.Clear();
        Assert.Equal(0, store.Count);
        using var after = await client.PostAsync(Compile, new { sessionId = "cache", script });

        Assert.Equal(await ScriptEndpointTests.ModelJson(before), await ScriptEndpointTests.ModelJson(after));
    }

    [Fact]
    public async Task AHundredConcurrentCompilesOfDifferentScriptsAnswerEachTheirOwn()
    {
        // 41's property-backend thread-safety check: every request solves with the real water backend
        // on whatever thread the pool gives it, and every answer must be its own script's.
        using var host = new ApiFactory();
        using var client = host.CreateClient();
        var template = Api.Sample("m2-cooling-loop.fluid");
        Assert.Contains("power=30", template, StringComparison.Ordinal);

        var powers = Enumerable.Range(0, 100).Select(static i => 20 + (i * 0.1)).ToArray();
        var responses = await Task.WhenAll(powers.Select(power => client.PostWithTokenAsync(
            Compile,
            new { sessionId = $"many-{power}", script = template.Replace("power=30", $"power={power:0.0}", StringComparison.Ordinal) },
            TestContext.Current.CancellationToken)));

        try
        {
            for (var i = 0; i < powers.Length; i++)
            {
                var body = await responses[i].ReadAsync<CompileResponse>();
                Assert.True(body.Model!.Solve!.Converged, $"power {powers[i]} did not converge");
                var exchanger = body.Model.Components.Single(static c => c.Id == "HE1");
                Assert.Equal(powers[i], exchanger.Parameters["power"].Value!.Value, 6);
                Assert.Equal(powers[i], exchanger.State!.Power!.Value!.Value, 2);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<T?> WaitForAsync<T>(Func<T?> probe, int attempts = 100)
        where T : class
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return null;
    }
}
