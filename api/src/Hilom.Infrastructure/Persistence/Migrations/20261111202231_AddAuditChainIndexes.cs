using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditChainIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_log_chain_order",
                schema: "access",
                table: "audit_log",
                columns: new[] { "sealed_at", "occurred_at", "id" },
                filter: "sealed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_unsealed",
                schema: "access",
                table: "audit_log",
                columns: new[] { "occurred_at", "id" },
                filter: "sealed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_chain_order",
                schema: "access",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_unsealed",
                schema: "access",
                table: "audit_log");
        }
    }
}
