using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nature = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    normal_balance = table.Column<string>(type: "char(1)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allow_negative = table.Column<bool>(type: "boolean", nullable: false),
                    has_materialized_balance = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                    table.UniqueConstraint("ak_ledger_account_id_currency_code", x => new { x.id, x.currency });
                    table.CheckConstraint("ck_ledger_accounts_customer_rules", "account_id IS NULL OR (nature = 'LIABILITY' AND normal_balance = 'C' AND NOT allow_negative)");
                    table.CheckConstraint("ck_ledger_accounts_materialized_only_for_customers", "has_materialized_balance = (account_id IS NOT NULL)");
                    table.CheckConstraint("ck_ledger_accounts_nature", "nature IN ('ASSET', 'LIABILITY')");
                    table.CheckConstraint("ck_ledger_accounts_normal_balance", "normal_balance IN ('D', 'C')");
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reverses_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_transactions_ledger_transactions_reverses_transactio",
                        column: x => x.reverses_transaction_id,
                        principalSchema: "ledger",
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_balances",
                schema: "ledger",
                columns: table => new
                {
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    normal_balance = table.Column<string>(type: "char(1)", nullable: false),
                    balance_minor = table.Column<long>(type: "bigint", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    allow_negative = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_balances", x => x.ledger_account_id);
                    table.CheckConstraint("ck_account_balances_non_negative", "allow_negative OR balance_minor >= 0");
                    table.ForeignKey(
                        name: "fk_account_balances_ledger_account_ledger_account_id_currency",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalSchema: "ledger",
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "char(1)", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    account_sequence = table.Column<long>(type: "bigint", nullable: true),
                    balance_after_minor = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_positive", "amount_minor > 0");
                    table.CheckConstraint("ck_ledger_entries_direction", "direction IN ('D', 'C')");
                    table.CheckConstraint("ck_ledger_entries_sequence_and_balance_together", "(account_sequence IS NULL) = (balance_after_minor IS NULL)");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_accounts_ledger_account_id_currency",
                        columns: x => new { x.ledger_account_id, x.currency },
                        principalSchema: "ledger",
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "ledger",
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_balances_ledger_account_id_currency",
                schema: "ledger",
                table: "account_balances",
                columns: new[] { "ledger_account_id", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_account_id",
                schema: "ledger",
                table: "ledger_accounts",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_code",
                schema: "ledger",
                table: "ledger_accounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account_id_account_sequence",
                schema: "ledger",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "account_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger_account_id_currency",
                schema: "ledger",
                table: "ledger_entries",
                columns: new[] { "ledger_account_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                schema: "ledger",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_external_id",
                schema: "ledger",
                table: "ledger_transactions",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_reverses_transaction_id",
                schema: "ledger",
                table: "ledger_transactions",
                column: "reverses_transaction_id",
                unique: true);

            migrationBuilder.Sql(LedgerDatabaseObjects.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_balances",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_transactions",
                schema: "ledger");

            migrationBuilder.Sql(LedgerDatabaseObjects.Drop);
        }
    }
}
