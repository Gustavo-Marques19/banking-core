using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Banking.Api.Composition;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Rajada máxima por usuário.</summary>
    public int TokenLimit { get; set; } = 60;

    /// <summary>Reposição sustentada: TokensPerPeriod a cada ReplenishmentPeriod.</summary>
    public int TokensPerPeriod { get; set; } = 10;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Buscas de conta de destino por usuário a cada minuto (threat model T22).</summary>
    public int LookupsPerMinute { get; set; } = 20;
}

/// <summary>
/// Token bucket por usuário (sub do token) ou por IP quando não autenticado. Protege o pool de conexões de quem
/// martela a própria conta esperando lock (threat model T15). Health checks ficam de fora.
/// </summary>
internal static class RateLimitingSetup
{
    public const string LookupPolicy = "account-lookup";

    public static IServiceCollection AddBankingRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/webhooks"))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var key = http.User.FindFirst("sub")?.Value is { } subject
                    ? $"sub:{subject}"
                    : $"ip:{http.Connection.RemoteIpAddress}";

                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = options.TokenLimit,
                    TokensPerPeriod = options.TokensPerPeriod,
                    ReplenishmentPeriod = options.ReplenishmentPeriod,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            // Soma-se ao limite global: a busca devolve dado de outro cliente, então o teto é bem menor.
            limiter.AddPolicy(LookupPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"sub:{http.User.FindFirst("sub")?.Value}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.LookupsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Muitas requisições. Tente de novo em instantes.",
                        extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }
}
