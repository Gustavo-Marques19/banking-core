using System.Security.Claims;
using Banking.Application.Common;

namespace Banking.Api.Http;

internal static class ActorExtensions
{
    public const string RoleClaim = "roles";

    public static Actor ToActor(this ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Token autenticado sem a claim sub.");
        return new Actor(subject, user.FindAll(RoleClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal));
    }
}
