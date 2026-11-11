using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVitalSigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vital_signs",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    encounter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    temperature_c = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    systolic_bp = table.Column<short>(type: "smallint", nullable: true),
                    diastolic_bp = table.Column<short>(type: "smallint", nullable: true),
                    heart_rate = table.Column<short>(type: "smallint", nullable: true),
                    respiratory_rate = table.Column<short>(type: "smallint", nullable: true),
                    oxygen_saturation = table.Column<short>(type: "smallint", nullable: true),
                    pain_score = table.Column<short>(type: "smallint", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vital_signs", x => x.id);
                    table.CheckConstraint("ck_vital_signs_any_measurement", "num_nonnulls(temperature_c, systolic_bp, heart_rate, respiratory_rate, oxygen_saturation, pain_score, weight_kg, height_cm) > 0");
                    table.CheckConstraint("ck_vital_signs_blood_pressure", "(systolic_bp IS NULL) = (diastolic_bp IS NULL) AND (systolic_bp IS NULL OR diastolic_bp < systolic_bp)");
                    table.CheckConstraint("ck_vital_signs_diastolic_bp", "diastolic_bp IS NULL OR diastolic_bp BETWEEN 20 AND 200");
                    table.CheckConstraint("ck_vital_signs_heart_rate", "heart_rate IS NULL OR heart_rate BETWEEN 20 AND 300");
                    table.CheckConstraint("ck_vital_signs_height_cm", "height_cm IS NULL OR height_cm BETWEEN 20 AND 300");
                    table.CheckConstraint("ck_vital_signs_oxygen_saturation", "oxygen_saturation IS NULL OR oxygen_saturation BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_vital_signs_pain_score", "pain_score IS NULL OR pain_score BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_vital_signs_respiratory_rate", "respiratory_rate IS NULL OR respiratory_rate BETWEEN 2 AND 80");
                    table.CheckConstraint("ck_vital_signs_systolic_bp", "systolic_bp IS NULL OR systolic_bp BETWEEN 40 AND 300");
                    table.CheckConstraint("ck_vital_signs_temperature_c", "temperature_c IS NULL OR temperature_c BETWEEN 30 AND 45");
                    table.CheckConstraint("ck_vital_signs_weight_kg", "weight_kg IS NULL OR weight_kg BETWEEN 0.5 AND 500");
                    table.ForeignKey(
                        name: "fk_vital_signs_employees_recorded_by",
                        column: x => x.recorded_by,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vital_signs_encounters_encounter_id",
                        column: x => x.encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vital_signs_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vital_signs_encounter_id_recorded_at",
                schema: "clinical",
                table: "vital_signs",
                columns: new[] { "encounter_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vital_signs_patient_id_recorded_at",
                schema: "clinical",
                table: "vital_signs",
                columns: new[] { "patient_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vital_signs_recorded_by",
                schema: "clinical",
                table: "vital_signs",
                column: "recorded_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vital_signs",
                schema: "clinical");
        }
    }
}
