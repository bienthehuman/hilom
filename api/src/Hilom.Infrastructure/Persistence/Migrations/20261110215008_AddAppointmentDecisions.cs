using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "scheduling",
                table: "appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by",
                schema: "scheduling",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "scheduling",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                schema: "scheduling",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                schema: "scheduling",
                table: "appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_cancel_reason",
                schema: "scheduling",
                table: "appointments",
                sql: "status <> 'cancelled' OR (cancel_reason IS NOT NULL AND btrim(cancel_reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_decline_reason",
                schema: "scheduling",
                table: "appointments",
                sql: "status <> 'declined' OR (decision_note IS NOT NULL AND btrim(decision_note) <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_cancel_reason",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_decline_reason",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "decided_by",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "decision_note",
                schema: "scheduling",
                table: "appointments");
        }
    }
}
