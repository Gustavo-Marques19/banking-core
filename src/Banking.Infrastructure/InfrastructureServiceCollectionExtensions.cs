using Banking.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BankingDbContext>(options =>
            BankingDbContextOptions.Configure(options, connectionString));

        services.AddHealthChecks()
            .AddDbContextCheck<BankingDbContext>("postgres", tags: [ReadinessTag]);

        return services;
    }
}
