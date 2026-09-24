using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateSequence(
                name: "account_number_seq",
                schema: "accounts");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    document_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    document_blind_index = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.client_id, x.operation, x.key });
                });

            migrationBuilder.CreateTable(
                name: "limit_usage",
                schema: "payments",
                columns: table => new
                {
                    limit_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    used_minor = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_limit_usage", x => new { x.limit_kind, x.subject, x.usage_date });
                    table.CheckConstraint("ck_limit_usage_non_negative", "used_minor >= 0");
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounts_customer_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "accounts",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposits",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposits", x => x.id);
                    table.CheckConstraint("ck_deposits_amount_positive", "amount_minor > 0");
                    table.ForeignKey(
                        name: "fk_deposits_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_customer_id",
                schema: "accounts",
                table: "accounts",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_ledger_account_id",
                schema: "accounts",
                table: "accounts",
                column: "ledger_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_number",
                schema: "accounts",
                table: "accounts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_document_blind_index",
                schema: "accounts",
                table: "customers",
                column: "document_blind_index",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_subject",
                schema: "accounts",
                table: "customers",
                column: "subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposits_account_id",
                schema: "payments",
                table: "deposits",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposits_requested_by_idempotency_key",
                schema: "payments",
                table: "deposits",
                columns: new[] { "requested_by", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposits",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "limit_usage",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "accounts");

            migrationBuilder.DropSequence(
                name: "account_number_seq",
                schema: "accounts");
        }
    }
}
