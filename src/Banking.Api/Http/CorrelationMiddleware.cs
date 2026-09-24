using System.Diagnostics;
using Serilog.Context;

namespace Banking.Api.Http;

/// <summary>
/// Devolve X-Trace-Id em toda resposta e aceita X-Correlation-Id do cliente. Com o trace id, o suporte acha
/// trace, logs e auditoria da operação.
/// </summary>
internal sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string TraceHeader = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString() ?? context.TraceIdentifier;
        var supplied = context.Request.Headers[CorrelationHeader].ToString();
        var correlationId = IsValid(supplied) ? supplied : traceId;

        activity?.SetBaggage(Infrastructure.Audit.AuditTrailBaggage.CorrelationId, correlationId);
        context.Response.Headers[TraceHeader] = traceId;
        context.Response.Headers[CorrelationHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
