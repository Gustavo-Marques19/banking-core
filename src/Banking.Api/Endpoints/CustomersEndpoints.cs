using Banking.Api.Composition;
using Banking.Api.Http;
using Banking.Application.Customers;
using Banking.Contracts.Requests;

namespace Banking.Api.Endpoints;

internal static class CustomersEndpoints
{
    public static RouteGroupBuilder MapCustomers(this RouteGroupBuilder api)
    {
        var customers = api.MapGroup("/customers").WithTags("Customers");

        customers.MapPost("/", async (RegisterCustomerRequest request, HttpContext http, RegisterCustomerHandler handler, CancellationToken ct) =>
                ApiResults.From(
                    await handler.HandleAsync(new RegisterCustomerCommand(http.User.ToActor(), request.Name, request.Document), ct),
                    view => Results.Created($"/api/v1/customers/{view.Id}", view)))
            .RequireAuthorization(Policies.Customer);

        customers.MapGet("/me", async (HttpContext http, GetCustomerHandler handler, CancellationToken ct) =>
                ApiResults.From(await handler.HandleCurrentAsync(http.User.ToActor(), ct), Results.Ok))
            .RequireAuthorization(Policies.Customer);

        customers.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetCustomerHandler handler, CancellationToken ct) =>
            ApiResults.From(await handler.HandleAsync(http.User.ToActor(), id, ct), Results.Ok));

        return api;
    }
}
