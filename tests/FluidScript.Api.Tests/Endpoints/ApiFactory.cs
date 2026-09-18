using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using FluidScript.Api.Contracts;
using FluidScript.Api.Pipeline;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Fixtures;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluidScript.Api.Tests.Endpoints;

/// <summary>The host in-process, with whatever a test swaps under it.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Registrations applied after the host's own, so a test can replace a service.</summary>
    public Action<IServiceCollection>? Overrides { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => Overrides?.Invoke(services));
    }
}

/// <summary>What every endpoint test does with a client.</summary>
public static class Api
{
    public static string Sample(string name) => File.ReadAllText(Path.Combine(RepositoryLayout.Samples, name));

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostWithTokenAsync(this HttpClient client, string path, object body, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(path, body, cancellationToken);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body[..Math.Min(body.Length, 400)]}");

        return JsonSerializer.Deserialize<T>(body, ModelContractJson.Options)
            ?? throw new InvalidOperationException("The body deserialized to null.");
    }

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}

/// <summary>Makes solvers that count what reaches them, so a test can watch cancellation and warm starts.</summary>
public sealed class CountingSolverFactory : ISolverFactory
{
    /// <summary>How long each counted step sleeps before the real solve, so a request is in flight long enough to cancel.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>How many counted steps run before the real solve.</summary>
    public int Steps { get; init; }

    /// <summary>Every solver made, in creation order: one per request.</summary>
    public ConcurrentQueue<CountingSolver> Created { get; } = new();

    public ISolver Create()
    {
        var solver = new CountingSolver(Delay, Steps);
        Created.Enqueue(solver);
        return solver;
    }
}

/// <summary>A Newton solver behind a counted, cancellable delay loop.</summary>
public sealed class CountingSolver(TimeSpan delay, int steps) : ISolver
{
    private readonly NewtonSolver _inner = new();
    private readonly Lock _gate = new();
    private int _iterations;
    private int? _cancelledAt;

    public string Name => "counting:" + _inner.Name;

    /// <summary>Counted steps run so far.</summary>
    public int Iterations => Volatile.Read(ref _iterations);

    /// <summary>The step count at which cancellation was first observed, or <see langword="null"/>.</summary>
    public int? CancelledAt
    {
        get
        {
            lock (_gate)
            {
                return _cancelledAt;
            }
        }
    }

    /// <summary>Every real solve: its first iterate, what it converged to, and how many Newton iterations it took.</summary>
    public List<(StateVector Guess, StateVector Solution, int NewtonIterations)> Calls { get; } = [];

    public Result<Unit> CanSolve(EquationSystem system) => _inner.CanSolve(system);

    public async Task<SolveResult> SolveAsync(EquationSystem system, StateVector initialGuess, IProgress<SolveProgress>? progress, CancellationToken cancellationToken)
    {
        for (var step = 0; step < steps; step++)
        {
            Observe(cancellationToken);
            Interlocked.Increment(ref _iterations);
            await Task.Delay(delay, CancellationToken.None);
        }

        Observe(cancellationToken);
        var result = await _inner.SolveAsync(system, initialGuess, progress, cancellationToken);

        lock (_gate)
        {
            Calls.Add((initialGuess, result.Solution, result.Iterations));
        }

        return result;
    }

    private void Observe(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            _cancelledAt ??= _iterations;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
