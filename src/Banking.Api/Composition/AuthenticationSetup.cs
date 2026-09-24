using Banking.Api.Http;
using Banking.Application.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Banking.Api.Composition;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>URL do realm no Keycloak, ex.: http://keycloak:8080/realms/banking.</summary>
    public string? Authority { get; set; }

    public string Audience { get; set; } = "banking-api";

    public bool RequireHttpsMetadata { get; set; } = true;
}

internal static class AuthenticationSetup
{
    public static IServiceCollection AddBankingAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
                jwt.TokenValidationParameters.RoleClaimType = ActorExtensions.RoleClaim;
                jwt.TokenValidationParameters.NameClaimType = "preferred_username";
                jwt.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Customer, p => p.RequireRole(Actor.CustomerRole))
            .AddPolicy(Policies.Operator, p => p.RequireRole(Actor.OperatorRole))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Actor.AdminRole));

        return services;
    }
}
