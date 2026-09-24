using Banking.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Banking")
    ?? throw new InvalidOperationException("ConnectionStrings:Banking não foi configurada.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness não consulta dependências: reiniciar o processo não resolve banco fora do ar.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadinessTag),
});

app.Run();

public partial class Program;
