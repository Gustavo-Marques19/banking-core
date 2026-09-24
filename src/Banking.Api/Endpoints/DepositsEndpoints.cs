using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.Deposits;
using Banking.Contracts.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Banking.Api.Endpoints;

internal static class DepositsEndpoints
{
    public static RouteGroupBuilder MapDeposits(this RouteGroupBuilder api)
    {
        api.MapPost("/accounts/{id:guid}/deposits", async (
                Guid id,
                DepositRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                HttpContext http,
                MakeDepositHandler handler,
                CancellationToken ct) =>
            {
                var command = new MakeDepositCommand(
                    http.User.ToActor(), id, request.Amount, request.Currency, request.Reason, idempotencyKey);
                return ApiResults.From(await handler.HandleAsync(command, ct), http, ToHttp);
            })
            .WithTags("Deposits")
            .RequireAuthorization(Policies.Operator);

        api.MapGet("/deposits/{id:guid}", async (Guid id, HttpContext http, MakeDepositHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.GetAsync(http.User.ToActor(), id, ct), Results.Ok))
            .WithTags("Deposits")
            .RequireAuthorization(Policies.Operator);

        api.MapPost("/deposits/{id:guid}/approve", async (Guid id, HttpContext http, DepositApprovalHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.ApproveAsync(http.User.ToActor(), id, ct), ToDecisionHttp))
            .WithTags("Deposits")
            .RequireAuthorization(Policies.Operator);

        api.MapPost("/deposits/{id:guid}/reject", async (Guid id, HttpContext http, DepositApprovalHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.RejectAsync(http.User.ToActor(), id, ct), Results.Ok))
            .WithTags("Deposits")
            .RequireAuthorization(Policies.Operator);

        return api;
    }

    private static IResult ToHttp(DepositView view) => view.RejectionReason is { } reason
        ? ApiResults.Rejected(reason, "Depósito recusado.", "depositId", view.Id)
        : view.Status == "pending_approval"
            ? Results.Accepted($"/api/v1/deposits/{view.Id}", view)
            : Results.Created($"/api/v1/deposits/{view.Id}", view);

    private static IResult ToDecisionHttp(DepositView view) => view.RejectionReason is { } reason
        ? ApiResults.Rejected(reason, "Depósito recusado na aprovação.", "depositId", view.Id)
        : Results.Ok(view);
}
