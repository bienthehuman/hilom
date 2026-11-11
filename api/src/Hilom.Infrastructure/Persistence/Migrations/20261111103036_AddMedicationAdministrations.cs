using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicationAdministrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medication_administrations",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    administered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dose_given = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    encounter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_administrations", x => x.id);
                    table.CheckConstraint("ck_medication_administrations_dose_given", "(outcome = 'given' AND length(btrim(coalesce(dose_given, ''))) > 0) OR (outcome <> 'given' AND dose_given IS NULL)");
                    table.CheckConstraint("ck_medication_administrations_outcome", "outcome IN ('given', 'held', 'refused', 'not_available')");
                    table.CheckConstraint("ck_medication_administrations_reason", "outcome = 'given' OR length(btrim(coalesce(notes, ''))) > 0");
                    table.CheckConstraint("ck_medication_administrations_void", "(voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by IS NOT NULL AND length(btrim(void_reason)) > 0)");
                    table.ForeignKey(
                        name: "fk_medication_administrations_employees_administered_by",
                        column: x => x.administered_by,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_administrations_encounters_encounter_id",
                        column: x => x.encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_administrations_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_administrations_physician_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "clinical",
                        principalTable: "physician_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medication_administrations_administered_by",
                schema: "clinical",
                table: "medication_administrations",
                column: "administered_by");

            migrationBuilder.CreateIndex(
                name: "ix_medication_administrations_encounter_id_administered_at",
                schema: "clinical",
                table: "medication_administrations",
                columns: new[] { "encounter_id", "administered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_administrations_order_id_administered_at",
                schema: "clinical",
                table: "medication_administrations",
                columns: new[] { "order_id", "administered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_administrations_patient_id_administered_at",
                schema: "clinical",
                table: "medication_administrations",
                columns: new[] { "patient_id", "administered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medication_administrations",
                schema: "clinical");
        }
    }
}
