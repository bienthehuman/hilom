using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_status", "status IN ('requested', 'confirmed', 'checked_in', 'completed', 'declined', 'cancelled', 'no_show')");
                    table.ForeignKey(
                        name: "fk_appointments_availability_slots_slot_id",
                        column: x => x.slot_id,
                        principalSchema: "scheduling",
                        principalTable: "availability_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_users_booked_by",
                        column: x => x.booked_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_booked_by",
                schema: "scheduling",
                table: "appointments",
                column: "booked_by");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_id",
                schema: "scheduling",
                table: "appointments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_slot_id",
                schema: "scheduling",
                table: "appointments",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "ux_appointments_one_live_per_slot",
                schema: "scheduling",
                table: "appointments",
                column: "slot_id",
                unique: true,
                filter: "status IN ('requested', 'confirmed', 'checked_in', 'completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");
        }
    }
}
