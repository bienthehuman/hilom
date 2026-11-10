using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientAllergies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_allergies",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allergen = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reaction = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_allergies", x => x.id);
                    table.CheckConstraint("ck_patient_allergies_category", "category IN ('drug', 'food', 'environmental', 'other')");
                    table.CheckConstraint("ck_patient_allergies_entered_in_error_note", "status <> 'entered_in_error' OR (status_note IS NOT NULL AND btrim(status_note) <> '')");
                    table.CheckConstraint("ck_patient_allergies_severity", "severity IN ('mild', 'moderate', 'severe')");
                    table.CheckConstraint("ck_patient_allergies_status", "status IN ('active', 'resolved', 'entered_in_error')");
                    table.ForeignKey(
                        name: "fk_patient_allergies_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patient_allergies_patient_id",
                schema: "patients",
                table: "patient_allergies",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ux_patient_allergies_active_allergen",
                schema: "patients",
                table: "patient_allergies",
                columns: new[] { "patient_id", "allergen" },
                unique: true,
                filter: "status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_allergies",
                schema: "patients");
        }
    }
}
