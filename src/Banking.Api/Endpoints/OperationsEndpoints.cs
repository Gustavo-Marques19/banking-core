using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.Operations;
using Banking.Contracts.Requests;

namespace Banking.Api.Endpoints;

internal static class OperationsEndpoints
{
    public static RouteGroupBuilder MapOperations(this RouteGroupBuilder api)
    {
        var queues = api.MapGroup("/operations").WithTags("Operations").RequireAuthorization(Policies.Operator);

        queues.MapGet("/pending-deposits", async (HttpContext http, OperatorQueuesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.PendingDepositsAsync(http.User.ToActor(), ct), Results.Ok));

        queues.MapGet("/pending-reversals", async (HttpContext http, OperatorQueuesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.PendingReversalsAsync(http.User.ToActor(), ct), Results.Ok));

        queues.MapGet("/external-transfers-in-review", async (HttpContext http, OperatorQueuesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.InManualReviewAsync(http.User.ToActor(), ct), Results.Ok));

        queues.MapGet("/pending-manual-resolutions", async (HttpContext http, OperatorQueuesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.PendingResolutionsAsync(http.User.ToActor(), ct), Results.Ok));

        api.MapPost("/external-transfers/{id:guid}/manual-resolutions", async (
                Guid id, ManualResolutionRequest request, HttpContext http, ManualResolutionHandler handler, CancellationToken ct) =>
                ApiResults.From(
                    await handler.RequestAsync(http.User.ToActor(), id, request.Outcome, request.Evidence, ct),
                    view => Results.Accepted($"/api/v1/manual-resolutions/{view.Id}", view)))
            .WithTags("Operations")
            .RequireAuthorization(Policies.Operator);

        var resolutions = api.MapGroup("/manual-resolutions").WithTags("Operations").RequireAuthorization(Policies.Operator);

        resolutions.MapGet("/{id:guid}", async (Guid id, HttpContext http, ManualResolutionHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetAsync(http.User.ToActor(), id, ct), Results.Ok));

        resolutions.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, ManualResolutionHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.ApproveAsync(http.User.ToActor(), id, ct), Results.Ok));

        resolutions.MapPost("/{id:guid}/reject", async (Guid id, HttpContext http, ManualResolutionHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.RejectAsync(http.User.ToActor(), id, ct), Results.Ok));

        return api;
    }
}
