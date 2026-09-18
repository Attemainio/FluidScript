using System.Text;

using FluidScript.Api.Contracts;
using FluidScript.Api.Pipeline;
using FluidScript.Api.Sessions;
using FluidScript.Core.Model;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FluidScript.Api.Endpoints;

/// <summary>The script endpoints of <c>42</c>: compile, validate, solve and format.</summary>
/// <remarks>
/// The handlers own only what precedes a response (<c>41</c>): the size limit, the session's
/// supersession, and turning a cancelled solve into an abandoned response. Everything about the
/// script is the pipeline's, and everything about the shape is the contracts'.
/// </remarks>
public static class ScriptEndpoints
{
    /// <summary>The REST major these routes serve under.</summary>
    public const int RestMajor = 1;

    /// <summary>Maps the three routes onto a version group.</summary>
    /// <param name="group">The <c>/api/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapScriptEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/compile", CompileAsync).WithName("compile").WithSummary("Parse, bind, lower, size and solve; the debounce path.");
        group.MapPost("/solve", SolveAsync).WithName("solve").WithSummary("As compile, with an unconnected component or a disconnected graph as an error.");
        group.MapPost("/validate", ValidateAsync).WithName("validate").WithSummary("Parse and bind only: diagnostics, no physics.");
        group.MapPost("/format", Format).WithName("format").WithSummary("The canonical layout (17), as text edits the editor applies as one undo step.");

        return group;
    }

    private static Task<Results<Ok<CompileResponse>, ProblemHttpResult, StatusCodeHttpResult>> CompileAsync(
        CompileRequest request, HttpContext http, ScriptPipeline pipeline, ISessionStore sessions, IOptions<ApiOptions> options) =>
        RunAsync(request, PipelineMode.Compile, http, pipeline, sessions, options);

    private static Task<Results<Ok<CompileResponse>, ProblemHttpResult, StatusCodeHttpResult>> SolveAsync(
        CompileRequest request, HttpContext http, ScriptPipeline pipeline, ISessionStore sessions, IOptions<ApiOptions> options) =>
        RunAsync(request, PipelineMode.Solve, http, pipeline, sessions, options);

    private static async Task<Results<Ok<ValidateResponse>, ProblemHttpResult>> ValidateAsync(
        ValidateRequest request, HttpContext http, ScriptPipeline pipeline, IOptions<ApiOptions> options)
    {
        if (request.Script is null)
        {
            return Missing("script");
        }

        if (OverSize(request.Script, options.Value) is { } tooLarge)
        {
            return tooLarge;
        }

        var result = await pipeline.RunAsync(new PipelineRequest(request.Script, PipelineMode.Validate, Solve: false, WarmStart: null), http.RequestAborted)
            .ConfigureAwait(false);

        return TypedResults.Ok(new ValidateResponse
        {
            ContractVersion = ModelContractBuilder.ContractVersion,
            LanguageMajor = result.LanguageMajor,
            Diagnostics = result.Diagnostics,
            Timings = result.Timings,
        });
    }

    private static Results<Ok<FormatResponse>, ProblemHttpResult> Format(FormatRequest request, IOptions<ApiOptions> options)
    {
        if (request.Script is null)
        {
            return Missing("script");
        }

        if (OverSize(request.Script, options.Value) is { } tooLarge)
        {
            return tooLarge;
        }

        // The formatter reads tokens and never throws on user input; a malformed script formats too (17).
        var edits = Formatter.Format(new SourceText(request.Script));
        return TypedResults.Ok(new FormatResponse(
            [.. edits.Select(static e => new TextEditWire(new SpanWire(e.Span.Start, e.Span.Length), e.NewText))]));
    }

    private static async Task<Results<Ok<CompileResponse>, ProblemHttpResult, StatusCodeHttpResult>> RunAsync(
        CompileRequest request, PipelineMode mode, HttpContext http, ScriptPipeline pipeline, ISessionStore sessions, IOptions<ApiOptions> options)
    {
        if (request.SessionId is null)
        {
            return Missing("sessionId");
        }

        if (request.Script is null)
        {
            return Missing("script");
        }

        if (OverSize(request.Script, options.Value) is { } tooLarge)
        {
            return tooLarge;
        }

        var session = sessions.GetOrCreate(new SessionKey(RestMajor, request.SessionId));
        var draft = session.Supersede(http.RequestAborted);

        try
        {
            var result = await pipeline.RunAsync(
                new PipelineRequest(request.Script, mode, request.Solve ?? true, session.LastSolution), draft.Token)
                .ConfigureAwait(false);

            if (result.Run is { Solve.Converged: true } run)
            {
                session.Remember(new WarmStart(run.Solve.Solution, run.TopologyHash));
            }

            return TypedResults.Ok(new CompileResponse
            {
                Model = result.Model,
                Diagnostics = result.Model is null ? result.Diagnostics : null,
                Timings = result.Timings,
            });
        }
        catch (OperationCanceledException) when (draft.IsCancellationRequested)
        {
            // Superseded by a newer draft, or the client went away: either way nobody is waiting for
            // this body. 499 is the conventional "client closed request"; it is never sent to a client
            // that is still listening, since a newer request from it is what cancelled this one (41).
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        finally
        {
            session.Release(draft);
        }
    }

    private static ProblemHttpResult Missing(string field) =>
        TypedResults.Problem(
            title: "The request is missing a required field.",
            detail: $"'{field}' is required.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = field });

    private static ProblemHttpResult? OverSize(string script, ApiOptions options)
    {
        var bytes = Encoding.UTF8.GetByteCount(script);

        return bytes > options.Limits.SourceBytes
            ? TypedResults.Problem(
                title: "The script is over the size limit.",
                detail: $"The script is {bytes} bytes; the limit is {options.Limits.SourceBytes}.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["bytes"] = bytes, ["limit"] = options.Limits.SourceBytes })
            : null;
    }
}
