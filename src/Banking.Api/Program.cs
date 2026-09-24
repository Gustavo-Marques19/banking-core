using Banking.Api.Composition;
using Banking.Api.Endpoints;
using Banking.Api.Http;
using Banking.Contracts;
using Banking.Domain.Accounts;
using Banking.Infrastructure;
using Banking.Infrastructure.Messaging;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Provider;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Banking")
    ?? throw new InvalidOperationException("ConnectionStrings:Banking não foi configurada.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ContentionExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => ContractJson.Apply(options.SerializerOptions));

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<MessagingOptions>(builder.Configuration.GetSection(MessagingOptions.SectionName));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.Configure<ConsumerOptions>(builder.Configuration.GetSection(ConsumerOptions.SectionName));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection(ProviderOptions.SectionName));
builder.Services.Configure<ExternalTransferWorkerOptions>(builder.Configuration.GetSection(ExternalTransferWorkerOptions.SectionName));
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddBankingApplication(builder.Configuration);
builder.Services.AddBankingAuthentication(builder.Configuration);

var app = builder.Build();

// Chave de PII ausente ou inválida derruba a subida, em vez de falhar no primeiro cadastro.
app.Services.GetRequiredService<IDocumentProtector>();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Liveness não consulta dependências: reiniciar o processo não resolve banco fora do ar.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadinessTag),
}).AllowAnonymous();

app.MapGroup("/api/v1")
    .MapCustomers()
    .MapAccounts()
    .MapDeposits()
    .MapTransfers()
    .MapExternalTransfers();

app.MapProviderWebhook();
app.MapMockProviderAdmin();

app.Run();

public partial class Program;
