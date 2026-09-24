using System.Text.Json;
using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.ExternalTransfers;
using Banking.Application.Reconciliation;
using Banking.Contracts;
using Banking.Contracts.Requests;
using Banking.Infrastructure.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Banking.Api.Endpoints;

internal static class ExternalTransfersEndpoints
{
    private static readonly JsonSerializerOptions Json = ContractJson.Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static RouteGroupBuilder MapExternalTransfers(this RouteGroupBuilder api)
    {
        var transfers = api.MapGroup("/external-transfers").WithTags("External transfers");

        transfers.MapPost("/", async (
                CreateExternalTransferRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                HttpContext http,
                CreateExternalTransferHandler handler,
                CancellationToken ct) =>
            {
                var command = new CreateExternalTransferCommand(
                    http.User.ToActor(),
                    request.SourceAccountId,
                    request.Amount,
                    request.Currency,
                    request.Destination.Bank,
                    request.Destination.Branch,
                    request.Destination.Account,
                    idempotencyKey);
                return ApiResults.From(await handler.HandleAsync(command, ct), http, view => view.RejectionReason is { } reason
                    ? ApiResults.Rejected(reason, "Transferência externa recusada.", "externalTransferId", view.Id)
                    : Results.Accepted($"/api/v1/external-transfers/{view.Id}", view));
            })
            .RequireAuthorization(Policies.Customer);

        transfers.MapGet("/{id:guid}", async (Guid id, HttpContext http, ExternalTransferQueriesHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.GetAsync(http.User.ToActor(), id, ct), Results.Ok));

        transfers.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext http, CancelExternalTransferHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.HandleAsync(http.User.ToActor(), id, ct), Results.Ok))
            .RequireAuthorization(Policies.Customer);

        api.MapGet("/admin/reconciliation", async (DateOnly? date, HttpContext http, ReconciliationHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.RunAsync(http.User.ToActor(), date, ct), Results.Ok))
            .WithTags("Admin");

        return api;
    }

    /// <summary>Fora do grupo autenticado: quem chama é o provider, e a prova de origem é a assinatura HMAC.</summary>
    public static WebApplication MapProviderWebhook(this WebApplication app)
    {
        app.MapPost("/webhooks/provider", async (
                HttpContext http, IOptions<ProviderOptions> options, ProviderWebhookHandler handler, TimeProvider time, CancellationToken ct) =>
            {
                if (string.IsNullOrEmpty(options.Value.WebhookSecret))
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Webhook não configurado.");
                }

                using var reader = new StreamReader(http.Request.Body);
                var body = await reader.ReadToEndAsync(ct);
                if (!WebhookSignature.IsValid(http.Request.Headers[WebhookSignature.Header], options.Value.WebhookSecret, body, time.GetUtcNow()))
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Assinatura inválida.");
                }

                ProviderWebhookRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<ProviderWebhookRequest>(body, Json);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Corpo inválido.");
                }

                var outcome = await handler.HandleAsync(request!.EventId, request.ClientReference, ct);
                return outcome == WebhookOutcome.UnknownTransfer ? Results.NotFound() : Results.Accepted();
            })
            .AllowAnonymous()
            .WithTags("Webhooks");

        return app;
    }

    /// <summary>Só existe com o mock e fora de produção (threat model T14).</summary>
    public static WebApplication MapMockProviderAdmin(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ProviderOptions>>().Value;
        if (!options.IsMock || app.Environment.IsProduction())
        {
            return app;
        }

        app.MapPut("/admin/provider/scenario", (ProviderScenarioRequest request, MockBankingProvider provider) =>
            {
                var scenario = request.Scenario.ToUpperInvariant();
                if (!MockBankingProvider.Scenarios.Contains(scenario))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: $"Cenário desconhecido. Use: {string.Join(", ", MockBankingProvider.Scenarios)}.");
                }

                provider.DefaultScenario = scenario;
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Admin)
            .WithTags("Admin");

        return app;
    }
}
