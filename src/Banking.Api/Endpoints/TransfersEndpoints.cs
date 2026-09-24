using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.LedgerQueries;
using Banking.Application.Reversals;
using Banking.Application.Transfers;
using Banking.Contracts.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Banking.Api.Endpoints;

internal static class TransfersEndpoints
{
    public static RouteGroupBuilder MapTransfers(this RouteGroupBuilder api)
    {
        var transfers = api.MapGroup("/transfers").WithTags("Transfers");

        transfers.MapPost("/", async (
                CreateTransferRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                HttpContext http,
                CreateTransferHandler handler,
                CancellationToken ct) =>
            {
                var command = new CreateTransferCommand(
                    http.User.ToActor(),
                    request.SourceAccountId,
                    request.DestinationAccountId,
                    request.Amount,
                    request.Currency,
                    request.Description,
                    idempotencyKey);
                return ApiResults.From(await handler.HandleAsync(command, ct), http, ToHttp);
            })
            .RequireAuthorization(Policies.Customer);

        transfers.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetTransferHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.HandleAsync(http.User.ToActor(), id, ct), Results.Ok));

        transfers.MapPost("/{id:guid}/reversals", async (Guid id, ReversalRequest request, HttpContext http, ReversalHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.RequestAsync(http.User.ToActor(), id, request.Reason, ct),
                    view => Results.Accepted($"/api/v1/reversals/{view.Id}", view)))
            .RequireAuthorization(Policies.Operator);

        var reversals = api.MapGroup("/reversals").WithTags("Transfers").RequireAuthorization(Policies.Operator);

        reversals.MapGet("/{id:guid}", async (Guid id, HttpContext http, ReversalHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetAsync(http.User.ToActor(), id, ct), Results.Ok));

        reversals.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, ReversalHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.ApproveAsync(http.User.ToActor(), id, ct), view => view.RejectionReason is { } reason
                ? ApiResults.Rejected(reason, "Estorno recusado na aprovação.", "reversalId", view.Id)
                : Results.Ok(view)));

        reversals.MapPost("/{id:guid}/reject", async (Guid id, HttpContext http, ReversalHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.RejectAsync(http.User.ToActor(), id, ct), Results.Ok));

        api.MapGet("/ledger/transactions/{id:guid}", async (Guid id, HttpContext http, GetLedgerTransactionHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.HandleAsync(http.User.ToActor(), id, ct), Results.Ok))
            .WithTags("Ledger")
            .RequireAuthorization(Policies.Operator);

        return api;
    }

    private static IResult ToHttp(TransferView view) => view.RejectionReason is { } reason
        ? ApiResults.Rejected(reason, "Transferência recusada.", "transferId", view.Id)
        : Results.Created($"/api/v1/transfers/{view.Id}", view);
}
