namespace Banking.Infrastructure.Persistence;

/// <summary>Roles criadas por infra/postgres/init/01-roles.sh.</summary>
public static class DatabaseRoles
{
    /// <summary>Dona dos schemas. Só as migrations usam.</summary>
    public const string Migrator = "banking_migrator";

    /// <summary>Usada pela API e pelos workers. Não tem DDL.</summary>
    public const string App = "banking_app";
}
