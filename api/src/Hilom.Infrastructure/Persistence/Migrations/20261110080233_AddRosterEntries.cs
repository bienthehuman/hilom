using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roster_entries",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duty_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_entries", x => x.id);
                    table.CheckConstraint("ck_roster_entries_status", "status IN ('scheduled', 'swapped_out', 'absent')");
                    table.ForeignKey(
                        name: "fk_roster_entries_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_roster_entries_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "workforce",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_roster_entries_wards_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "facility",
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roster_entries_shift_id",
                schema: "workforce",
                table: "roster_entries",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_roster_entries_ward_id_duty_date",
                schema: "workforce",
                table: "roster_entries",
                columns: new[] { "ward_id", "duty_date" });

            migrationBuilder.CreateIndex(
                name: "ux_roster_entries_one_shift_per_day",
                schema: "workforce",
                table: "roster_entries",
                columns: new[] { "employee_id", "duty_date" },
                unique: true,
                filter: "status = 'scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_entries",
                schema: "workforce");
        }
    }
}
