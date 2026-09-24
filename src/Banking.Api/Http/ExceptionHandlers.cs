using Banking.Application.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Banking.Api.Http;

/// <summary>Lock timeout ou deadlock: houve rollback, e repetir com a mesma Idempotency-Key é seguro (ADR-004).</summary>
internal sealed class ContentionExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ContentionException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "1";
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = exception.Message,
                Extensions = { ["code"] = "contention" },
            },
        });
    }
}
