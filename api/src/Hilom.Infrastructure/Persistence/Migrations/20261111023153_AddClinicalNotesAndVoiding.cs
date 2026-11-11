using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalNotesAndVoiding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "clinical",
                table: "vital_signs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                schema: "clinical",
                table: "vital_signs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by",
                schema: "clinical",
                table: "vital_signs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clinical_notes",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    authored_by = table.Column<Guid>(type: "uuid", nullable: false),
                    authored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaces_note_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_clinical_notes", x => x.id);
                    table.CheckConstraint("ck_clinical_notes_body", "length(btrim(body)) > 0");
                    table.CheckConstraint("ck_clinical_notes_note_type", "note_type IN ('assessment', 'progress', 'nursing', 'diet', 'discharge_summary')");
                    table.CheckConstraint("ck_clinical_notes_void", "(voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by IS NOT NULL AND length(btrim(void_reason)) > 0)");
                    table.ForeignKey(
                        name: "fk_clinical_notes_clinical_notes_replaces_note_id",
                        column: x => x.replaces_note_id,
                        principalSchema: "clinical",
                        principalTable: "clinical_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_notes_employees_authored_by",
                        column: x => x.authored_by,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_notes_encounters_encounter_id",
                        column: x => x.encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinical_notes_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_vital_signs_void",
                schema: "clinical",
                table: "vital_signs",
                sql: "(voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by IS NOT NULL AND length(btrim(void_reason)) > 0)");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_notes_authored_by",
                schema: "clinical",
                table: "clinical_notes",
                column: "authored_by");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_notes_encounter_id_authored_at",
                schema: "clinical",
                table: "clinical_notes",
                columns: new[] { "encounter_id", "authored_at" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_notes_patient_id_authored_at",
                schema: "clinical",
                table: "clinical_notes",
                columns: new[] { "patient_id", "authored_at" });

            migrationBuilder.CreateIndex(
                name: "ux_clinical_notes_replaces_note_id",
                schema: "clinical",
                table: "clinical_notes",
                column: "replaces_note_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_notes",
                schema: "clinical");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vital_signs_void",
                schema: "clinical",
                table: "vital_signs");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "clinical",
                table: "vital_signs");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "clinical",
                table: "vital_signs");

            migrationBuilder.DropColumn(
                name: "voided_by",
                schema: "clinical",
                table: "vital_signs");
        }
    }
}
