using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InternalTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "internal_transfers",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    description = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_internal_transfers", x => x.id);
                    table.CheckConstraint("ck_internal_transfers_amount_positive", "amount_minor > 0");
                    table.CheckConstraint("ck_internal_transfers_distinct_accounts", "source_account_id <> destination_account_id");
                    table.ForeignKey(
                        name: "fk_internal_transfers_accounts_destination_account_id",
                        column: x => x.destination_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_internal_transfers_accounts_source_account_id",
                        column: x => x.source_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfers_destination_account_id",
                schema: "payments",
                table: "internal_transfers",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfers_requested_by_idempotency_key",
                schema: "payments",
                table: "internal_transfers",
                columns: new[] { "requested_by", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfers_source_account_id",
                schema: "payments",
                table: "internal_transfers",
                column: "source_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_transfers",
                schema: "payments");
        }
    }
}
