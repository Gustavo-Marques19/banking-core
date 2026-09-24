using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banking.IntegrationTests.Support;

/// <summary>Emissor de tokens para os testes, no lugar do Keycloak. A validação na API é a mesma.</summary>
internal static class TestTokens
{
    public const string Issuer = "https://tests.banking.local/realms/banking";
    public const string Audience = "banking-api";

    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string For(TestUser user, string? audience = Audience, DateTime? expires = null, SecurityKey? key = null)
    {
        var claims = new List<Claim> { new("sub", user.Subject), new("preferred_username", user.Subject) };
        claims.AddRange(user.Roles.Select(r => new Claim("roles", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, key is SymmetricSecurityKey ? SecurityAlgorithms.HmacSha256 : SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

internal sealed record TestUser(string Subject, params string[] Roles)
{
    public static TestUser NewCustomer() => new("customer-" + Guid.NewGuid().ToString("N"), "customer");

    public static TestUser NewOperator() => new("operator-" + Guid.NewGuid().ToString("N"), "operator");

    public static TestUser NewAdmin() => new("admin-" + Guid.NewGuid().ToString("N"), "admin");
}
