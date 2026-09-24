using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchemas : Migration
    {
        private static readonly string[] ModuleSchemas =
        [
            DatabaseSchemas.Accounts,
            DatabaseSchemas.Ledger,
            DatabaseSchemas.Payments,
            DatabaseSchemas.Platform,
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var schema in ModuleSchemas)
            {
                migrationBuilder.EnsureSchema(schema);
                migrationBuilder.Sql($"GRANT USAGE ON SCHEMA {schema} TO {DatabaseRoles.App};");
                migrationBuilder.Sql(
                    $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT USAGE, SELECT ON SEQUENCES TO {DatabaseRoles.App};");
            }

            // Ledger é append-only: a app só lê e insere. Exceções (account_balances) recebem UPDATE tabela a tabela.
            migrationBuilder.Sql(
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA {DatabaseSchemas.Ledger} GRANT SELECT, INSERT ON TABLES TO {DatabaseRoles.App};");

            foreach (var schema in new[] { DatabaseSchemas.Accounts, DatabaseSchemas.Payments, DatabaseSchemas.Platform })
            {
                migrationBuilder.Sql(
                    $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {DatabaseRoles.App};");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var schema in ModuleSchemas)
            {
                migrationBuilder.Sql($"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} REVOKE ALL ON TABLES FROM {DatabaseRoles.App};");
                migrationBuilder.Sql($"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} REVOKE ALL ON SEQUENCES FROM {DatabaseRoles.App};");
                migrationBuilder.Sql($"REVOKE USAGE ON SCHEMA {schema} FROM {DatabaseRoles.App};");
            }

            // platform guarda o histórico de migrations, então fica.
            migrationBuilder.DropSchema(DatabaseSchemas.Accounts);
            migrationBuilder.DropSchema(DatabaseSchemas.Ledger);
            migrationBuilder.DropSchema(DatabaseSchemas.Payments);
        }
    }
}
