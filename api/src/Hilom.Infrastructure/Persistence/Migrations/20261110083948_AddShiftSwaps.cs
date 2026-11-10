using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_swap_requests",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_duty_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requester_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_duty_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    target_response = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_swap_requests", x => x.id);
                    table.CheckConstraint("ck_shift_swap_requests_status", "status IN ('pending_target', 'pending_approval', 'approved', 'declined_by_target', 'rejected', 'cancelled')");
                    table.CheckConstraint("ck_shift_swap_requests_two_people", "requester_employee_id <> target_employee_id");
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_employees_requester_employee_id",
                        column: x => x.requester_employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_employees_target_employee_id",
                        column: x => x.target_employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_roster_entries_requester_entry_id",
                        column: x => x.requester_entry_id,
                        principalSchema: "workforce",
                        principalTable: "roster_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_roster_entries_target_entry_id",
                        column: x => x.target_entry_id,
                        principalSchema: "workforce",
                        principalTable: "roster_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_shifts_requester_shift_id",
                        column: x => x.requester_shift_id,
                        principalSchema: "workforce",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_shifts_target_shift_id",
                        column: x => x.target_shift_id,
                        principalSchema: "workforce",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swap_requests_wards_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "facility",
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_requester_employee_id",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "requester_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_requester_shift_id",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "requester_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_reviewed_by",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_target_employee_id",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "target_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_target_shift_id",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "target_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_ward_id_status",
                schema: "workforce",
                table: "shift_swap_requests",
                columns: new[] { "ward_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_shift_swap_requests_open_requester_entry",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "requester_entry_id",
                unique: true,
                filter: "status IN ('pending_target', 'pending_approval')");

            migrationBuilder.CreateIndex(
                name: "ux_shift_swap_requests_open_target_entry",
                schema: "workforce",
                table: "shift_swap_requests",
                column: "target_entry_id",
                unique: true,
                filter: "status IN ('pending_target', 'pending_approval')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_swap_requests",
                schema: "workforce");
        }
    }
}
