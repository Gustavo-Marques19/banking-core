using Banking.Api.Http;
using Banking.Application.Audit;

namespace Banking.Api.Endpoints;

internal static class AuditEndpoints
{
    public static RouteGroupBuilder MapAudit(this RouteGroupBuilder api)
    {
        var audit = api.MapGroup("/admin/audit").WithTags("Admin");

        audit.MapGet("/", async (string? resourceId, HttpContext http, AuditHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.ListAsync(http.User.ToActor(), resourceId, ct), Results.Ok));

        audit.MapGet("/verify", async (HttpContext http, AuditHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.VerifyAsync(http.User.ToActor(), ct), Results.Ok));

        return api;
    }
}
