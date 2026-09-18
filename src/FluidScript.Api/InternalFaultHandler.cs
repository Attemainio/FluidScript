using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FluidScript.Api;

/// <summary>An exception that escaped a handler becomes <c>FS9001</c> in the log and a 500 with a correlation id (<c>41</c>).</summary>
/// <remarks>
/// A cancelled request is not a fault and is left alone: the client that cancelled it is not reading
/// the response. Everything else is a bug -- no pipeline stage throws on user input -- so the body
/// says so plainly and carries the id a report can quote.
/// </remarks>
public sealed partial class InternalFaultHandler(ILogger<InternalFaultHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return false;
        }

        // A body that cannot be read as the request record is the client's mistake, not a fault: 42's
        // 400 row. The development host raises it as an exception rather than answering itself.
        if (exception is BadHttpRequestException bad)
        {
            httpContext.Response.StatusCode = bad.StatusCode;

            await httpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = bad.StatusCode,
                    Title = "The request could not be read.",
                    Detail = bad.Message,
                },
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        var correlationId = httpContext.TraceIdentifier;
        LogFault(logger, exception, correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong inside FluidScript (FS9001).",
                Detail = "This is a bug, not a problem with the script. Quote the correlation id when reporting it.",
                Extensions = { ["code"] = "FS9001", ["correlationId"] = correlationId },
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "FS9001: an unexpected exception escaped a request; correlation id {CorrelationId}.")]
    private static partial void LogFault(ILogger logger, Exception exception, string correlationId);
}
