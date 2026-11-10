using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEncounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.CreateTable(
                name: "encounters",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encounter_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_encounter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attending_physician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chief_complaint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    diagnosis_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_encounters", x => x.id);
                    table.CheckConstraint("ck_encounters_appointment_outpatient", "appointment_id IS NULL OR encounter_type = 'outpatient'");
                    table.CheckConstraint("ck_encounters_encounter_type", "encounter_type IN ('outpatient', 'inpatient')");
                    table.CheckConstraint("ck_encounters_ended_at", "(status = 'open') = (ended_at IS NULL) AND (ended_at IS NULL OR ended_at >= started_at)");
                    table.CheckConstraint("ck_encounters_parent_inpatient", "parent_encounter_id IS NULL OR encounter_type = 'inpatient'");
                    table.CheckConstraint("ck_encounters_status", "status IN ('open', 'closed', 'voided')");
                    table.ForeignKey(
                        name: "fk_encounters_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_encounters_encounters_parent_encounter_id",
                        column: x => x.parent_encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_encounters_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_encounters_attending_physician_id_status",
                schema: "clinical",
                table: "encounters",
                columns: new[] { "attending_physician_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_encounters_parent_encounter_id",
                schema: "clinical",
                table: "encounters",
                column: "parent_encounter_id");

            migrationBuilder.CreateIndex(
                name: "ix_encounters_patient_id_started_at",
                schema: "clinical",
                table: "encounters",
                columns: new[] { "patient_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_encounters_appointment_id",
                schema: "clinical",
                table: "encounters",
                column: "appointment_id",
                unique: true);

            // The physician profile is an owned type, so EF can't map this foreign key (EncounterConfiguration).
            migrationBuilder.Sql(
                """
                ALTER TABLE clinical.encounters
                ADD CONSTRAINT fk_encounters_physician_profiles_attending_physician_id
                FOREIGN KEY (attending_physician_id) REFERENCES workforce.physician_profiles (employee_id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encounters",
                schema: "clinical");
        }
    }
}
