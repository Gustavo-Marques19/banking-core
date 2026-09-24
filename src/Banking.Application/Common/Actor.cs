namespace Banking.Application.Common;

/// <summary>Quem está pedindo. Vem do token validado, nunca de header ou corpo.</summary>
public sealed record Actor(string Subject, IReadOnlySet<string> Roles)
{
    public const string CustomerRole = "customer";
    public const string OperatorRole = "operator";
    public const string AdminRole = "admin";

    public bool IsOperator => Roles.Contains(OperatorRole);

    public bool IsAdmin => Roles.Contains(AdminRole);

    public static Actor System(string component) => new($"system:{component}", new HashSet<string>());
}
