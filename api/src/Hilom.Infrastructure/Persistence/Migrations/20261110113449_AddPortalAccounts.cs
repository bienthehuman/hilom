using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_type",
                schema: "access",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "staff"); // Every account before 2.3 was made by an administrator or an invite.

            migrationBuilder.AddColumn<bool>(
                name: "is_portal",
                schema: "access",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_account_type",
                schema: "access",
                table: "users",
                sql: "account_type IN ('staff', 'portal')");

            migrationBuilder.CreateIndex(
                name: "ix_roles_is_portal",
                schema: "access",
                table: "roles",
                column: "is_portal",
                unique: true,
                filter: "is_portal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_account_type",
                schema: "access",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_roles_is_portal",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "account_type",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_portal",
                schema: "access",
                table: "roles");
        }
    }
}
