using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.Accounts;
using Banking.Application.Notifications;
using Banking.Contracts.Requests;

namespace Banking.Api.Endpoints;

internal static class AccountsEndpoints
{
    public static RouteGroupBuilder MapAccounts(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapPost("/", async (OpenAccountRequest request, HttpContext http, OpenAccountHandler handler, CancellationToken ct) =>
                ApiResults.From(
                    await handler.HandleAsync(new OpenAccountCommand(http.User.ToActor(), request.Currency), ct),
                    view => Results.Created($"/api/v1/accounts/{view.Id}", view)))
            .RequireAuthorization(Policies.Customer);

        accounts.MapGet("/", async (HttpContext http, AccountQueriesHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.ListMineAsync(http.User.ToActor(), ct)))
            .RequireAuthorization(Policies.Customer);

        accounts.MapGet("/lookup", async (string? branch, string? number, AccountLookupHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.HandleAsync(branch, number, ct), Results.Ok))
            .RequireAuthorization(Policies.Customer)
            .RequireRateLimiting(RateLimitingSetup.LookupPolicy);

        accounts.MapGet("/{id:guid}", async (Guid id, HttpContext http, AccountQueriesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetAsync(http.User.ToActor(), id, ct), Results.Ok));

        accounts.MapGet("/{id:guid}/balance", async (Guid id, HttpContext http, AccountQueriesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetBalanceAsync(http.User.ToActor(), id, ct), Results.Ok));

        accounts.MapGet("/{id:guid}/transactions", async (
                Guid id, long? before, int? limit, HttpContext http, AccountQueriesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetStatementAsync(http.User.ToActor(), id, before, limit, ct), Results.Ok));

        accounts.MapGet("/{id:guid}/notifications", async (Guid id, HttpContext http, NotificationQueriesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.ListAsync(http.User.ToActor(), id, ct), Results.Ok));

        accounts.MapPost("/{id:guid}/block", async (Guid id, HttpContext http, AccountStatusHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.BlockAsync(http.User.ToActor(), id, ct), Results.Ok))
            .RequireAuthorization(Policies.Operator);

        accounts.MapPost("/{id:guid}/unblock", async (Guid id, HttpContext http, AccountStatusHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.UnblockAsync(http.User.ToActor(), id, ct), Results.Ok))
            .RequireAuthorization(Policies.Operator);

        return api;
    }
}
