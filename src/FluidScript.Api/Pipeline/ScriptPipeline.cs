using System.Collections.Immutable;
using System.Diagnostics;

using FluidScript.Api.Contracts;
using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Compatibility;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Core.Topology;

using Microsoft.Extensions.Options;

namespace FluidScript.Api.Pipeline;

/// <summary>How far a request asks the pipeline to go, and how strict it is.</summary>
public enum PipelineMode
{
    /// <summary>Parse and bind only: diagnostics, no physics (<c>/validate</c>).</summary>
    Validate = 1,

    /// <summary>The debounce path: lower, size and solve, tolerating an unconnected component (<c>/compile</c>).</summary>
    Compile,

    /// <summary>The Solve button: as <see cref="Compile"/>, with <c>FS1507</c> and <c>FS1511</c> raised to errors (<c>/solve</c>).</summary>
    Solve,
}

/// <summary>One request to the pipeline.</summary>
/// <param name="Script">The script text.</param>
/// <param name="Mode">How far to go.</param>
/// <param name="Solve">Whether to run the solver at all; <see langword="false"/> stops after lowering.</param>
/// <param name="WarmStart">The session's last solution, offered to the outer loop; <see langword="null"/> for a cold start.</param>
public sealed record PipelineRequest(string Script, PipelineMode Mode, bool Solve, WarmStart? WarmStart);

/// <summary>What the pipeline produced.</summary>
/// <param name="Model">The contract, or <see langword="null"/> when the language gate refused the script or the mode was <see cref="PipelineMode.Validate"/>.</param>
/// <param name="Diagnostics">Every diagnostic in <c>44</c>'s order, rendered against the source; the model carries the same list when there is one.</param>
/// <param name="Timings">Stage timings.</param>
/// <param name="Run">The outer-loop result when something was solved, for the session to keep its solution.</param>
/// <param name="LanguageMajor">The major the script declared, or <see langword="null"/>.</param>
public sealed record PipelineResult(
    ModelContract? Model,
    ImmutableArray<DiagnosticWire> Diagnostics,
    TimingsWire Timings,
    OuterLoopResult? Run,
    int? LanguageMajor);

/// <summary>Runs Core's stages in order for one request, with the timings <c>42</c> ships and the limits <c>07</c> sets.</summary>
/// <remarks>
/// <para>
/// The stages are the ones every fixture runs -- version gate, parse, bind, lower with sizing, solve,
/// contract -- and this class adds nothing to them but a clock and the ceilings. It never throws on
/// what the script says: a script with errors is lowered without physics and returned as a model
/// whose circuits say <c>solved: false</c>; a script over a limit is returned with <c>FS4601</c> and
/// no solve; a script this build cannot read is returned as diagnostics alone.
/// </para>
/// <para>
/// Cancellation is the caller's token, handed to the outer loop, which checks it between passes and
/// the solver between iterations. An <see cref="OperationCanceledException"/> is the one thing that
/// escapes, on purpose: a superseded or abandoned request has no response worth building.
/// </para>
/// </remarks>
public sealed class ScriptPipeline(ISolverFactory solvers, IOptions<ApiOptions> options)
{
    /// <summary>Runs one request.</summary>
    /// <param name="request">What to run and how far.</param>
    /// <param name="cancellationToken">Reaches the solver between iterations.</param>
    /// <returns>The result.</returns>
    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();
        var limits = options.Value.Limits;
        var source = new SourceText(request.Script);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var compatibility = ScriptCompatibility.Inspect(source);
        diagnostics.AddRange(compatibility.Diagnostics);
        var major = compatibility.DetectedMajor?.Value;

        if (!compatibility.AllowedActions.Contains(CompatibilityAction.Compile))
        {
            return new PipelineResult(
                null,
                ModelContractBuilder.Diagnostics(source, diagnostics.ToImmutable()),
                new TimingsWire(0, 0, 0, 0, Elapsed(started)),
                null,
                major);
        }

        var parseStarted = Stopwatch.GetTimestamp();
        var parse = FluidScriptParser.Parse(source);
        var parseMs = Elapsed(parseStarted);
        diagnostics.AddRange(parse.Diagnostics);
        diagnostics.AddRange(limits.Check(parse.Root));

        var bindStarted = Stopwatch.GetTimestamp();
        var bind = new Binder(ComponentRegistry.Default).Bind(parse);
        var bindMs = Elapsed(bindStarted);
        diagnostics.AddRange(bind.Diagnostics);

        if (request.Mode == PipelineMode.Validate)
        {
            return new PipelineResult(
                null,
                ModelContractBuilder.Diagnostics(source, diagnostics.ToImmutable()),
                new TimingsWire(parseMs, bindMs, 0, 0, Elapsed(started)),
                null,
                major);
        }

        if (request.Mode == PipelineMode.Solve)
        {
            // The Solve button asks for an answer, and an answer from a circuit with a floating pump is
            // misleading; the same two codes stay warnings on the debounce path so a half-written script
            // still renders (42).
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Code is "FS1507" or "FS1511")
                {
                    diagnostics[i] = diagnostics[i] with { Severity = DiagnosticSeverity.Error };
                }
            }
        }

        var catalog = Catalog(compatibility.Catalog, diagnostics);
        var substance = Substance(bind.Model, diagnostics);
        var loop = new OuterLoop(solvers.Create(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog));

        cancellationToken.ThrowIfCancellationRequested();

        var sizeStarted = Stopwatch.GetTimestamp();
        var prepared = loop.Prepare(bind.Model, substance);
        var sizeMs = Elapsed(sizeStarted);
        var graph = prepared.Lowered.Graph;

        if (prepared.Lowered.Unresolved.IsEmpty
            && limits.CheckUnknowns(WellPosedness.Check(graph).Counting.Unknowns) is { } overLimit)
        {
            diagnostics.Add(overLimit);
        }

        OuterLoopResult? run = null;
        var solveMs = 0;

        if (request.Solve && !diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            var solveStarted = Stopwatch.GetTimestamp();
            var result = await loop.RunAsync(bind.Model, substance, request.WarmStart, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            solveMs = Elapsed(solveStarted);

            // The Newton solver answers cancellation with a result that says so rather than by throwing;
            // a superseded request has no use for that result either, so it leaves the same way.
            cancellationToken.ThrowIfCancellationRequested();

            if (result.TryGetValue(out run))
            {
                graph = run.Graph;
                diagnostics.AddRange(run.Solve.Diagnostics);
            }
            else
            {
                diagnostics.Add(result.Error!.At(null));
            }
        }

        var input = new ModelContractInput
        {
            Source = source,
            Root = parse.Root,
            Model = bind.Model,
            Graph = graph,
            Run = run,
            Diagnostics = diagnostics.ToImmutable(),
            Catalog = catalog,
            ElapsedMs = run is null ? null : solveMs,
        };

        var model = ModelContractJson.Build(input);

        return new PipelineResult(
            model,
            model.Diagnostics,
            new TimingsWire(parseMs, bindMs, sizeMs, solveMs, Elapsed(started)),
            run,
            major);
    }

    private static ICatalog<PipeSpec> Catalog(CatalogPin? pin, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var resolved = PipeCatalogs.Resolve(pin);

        if (resolved.TryGetValue(out var catalog))
        {
            diagnostics.AddRange(catalog.Notes.Select(static note => note.At(null)));
            return catalog.Catalog;
        }

        // A pin nothing matches is reported and the default takes its place, so the script still
        // lowers and draws; the diagnostic says which catalogue the sizes did not come from.
        diagnostics.Add(resolved.Error!.At(null));
        return PipeCatalogs.Default;
    }

    private static ISubstance Substance(SemanticModel model, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var written = model.Circuits.Select(static circuit => circuit.Substance).FirstOrDefault(static name => name is not null);

        if (written is null)
        {
            return Water.Instance;
        }

        var resolved = SubstanceRegistry.Default.Resolve(written);

        if (resolved.TryGetValue(out var substance))
        {
            return substance;
        }

        diagnostics.Add(resolved.Error!.At(null));
        return Water.Instance;
    }

    private static int Elapsed(long since) => (int)Math.Round(Stopwatch.GetElapsedTime(since).TotalMilliseconds);
}
