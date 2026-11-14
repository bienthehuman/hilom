using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "email_code_attempts",
                schema: "access",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_code_expires_at",
                schema: "access",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_code_hash",
                schema: "access",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_code_sent_at",
                schema: "access",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verified_at",
                schema: "access",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Accounts from before email confirmation keep working: treat their email as confirmed when made.
            migrationBuilder.Sql("UPDATE access.users SET email_verified_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_code_attempts",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_code_expires_at",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_code_hash",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_code_sent_at",
                schema: "access",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                schema: "access",
                table: "users");
        }
    }
}
