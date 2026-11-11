using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admissions",
                schema: "clinical",
                columns: table => new
                {
                    encounter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    admitting_diagnosis = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    discharged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discharge_disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    discharged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admissions", x => x.encounter_id);
                    table.CheckConstraint("ck_admissions_discharge", "(discharged_at IS NULL AND discharge_disposition IS NULL AND discharged_by IS NULL) OR (discharged_at IS NOT NULL AND discharge_disposition IS NOT NULL AND discharged_by IS NOT NULL AND discharged_at >= admitted_at)");
                    table.CheckConstraint("ck_admissions_discharge_disposition", "discharge_disposition IN ('home', 'transferred_out', 'against_advice', 'expired')");
                    table.ForeignKey(
                        name: "fk_admissions_encounters_encounter_id",
                        column: x => x.encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admissions_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admissions_users_discharged_by",
                        column: x => x.discharged_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bed_assignments",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    during = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    moved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed_assignments", x => x.id);
                    table.CheckConstraint("ck_bed_assignments_during", "NOT isempty(during) AND NOT lower_inf(during) AND (upper_inf(during) OR upper(during) > lower(during))");
                    table.ForeignKey(
                        name: "fk_bed_assignments_admissions_admission_id",
                        column: x => x.admission_id,
                        principalSchema: "clinical",
                        principalTable: "admissions",
                        principalColumn: "encounter_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_assignments_beds_bed_id",
                        column: x => x.bed_id,
                        principalSchema: "facility",
                        principalTable: "beds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_assignments_users_moved_by",
                        column: x => x.moved_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bed_assignments_wards_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "facility",
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admissions_discharged_by",
                schema: "clinical",
                table: "admissions",
                column: "discharged_by");

            migrationBuilder.CreateIndex(
                name: "ix_admissions_patient_id_admitted_at",
                schema: "clinical",
                table: "admissions",
                columns: new[] { "patient_id", "admitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bed_assignments_admission_id",
                schema: "clinical",
                table: "bed_assignments",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_assignments_bed_id",
                schema: "clinical",
                table: "bed_assignments",
                column: "bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_assignments_moved_by",
                schema: "clinical",
                table: "bed_assignments",
                column: "moved_by");

            migrationBuilder.CreateIndex(
                name: "ix_bed_assignments_ward_id",
                schema: "clinical",
                table: "bed_assignments",
                column: "ward_id");

            // EF has no fluent API for these; the names match AdmissionConfiguration and BedAssignmentConfiguration.
            // A patient can't be admitted twice at once (docs/DATA_MODEL.md). An open admission runs forever.
            migrationBuilder.Sql(
                """
                ALTER TABLE clinical.admissions
                ADD CONSTRAINT ex_admissions_one_per_patient
                EXCLUDE USING gist (patient_id WITH =, tstzrange(admitted_at, discharged_at) WITH &&);
                """);

            // No two patients in one bed, and a patient in one bed, at any moment.
            migrationBuilder.Sql(
                """
                ALTER TABLE clinical.bed_assignments
                ADD CONSTRAINT ex_bed_assignments_one_patient_per_bed
                EXCLUDE USING gist (bed_id WITH =, during WITH &&);
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE clinical.bed_assignments
                ADD CONSTRAINT ex_bed_assignments_one_bed_per_admission
                EXCLUDE USING gist (admission_id WITH =, during WITH &&);
                """);

            // The ward copied onto a bed assignment is the bed's ward. Beds never change ward, so this never cascades.
            migrationBuilder.Sql(
                """
                ALTER TABLE facility.beds
                ADD CONSTRAINT ak_beds_id_ward_id UNIQUE (id, ward_id);

                ALTER TABLE clinical.bed_assignments
                ADD CONSTRAINT fk_bed_assignments_beds_bed_id_ward_id
                FOREIGN KEY (bed_id, ward_id) REFERENCES facility.beds (id, ward_id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping bed_assignments drops its constraints; the key it needed on beds goes separately.
            migrationBuilder.DropTable(
                name: "bed_assignments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "admissions",
                schema: "clinical");

            migrationBuilder.Sql("ALTER TABLE facility.beds DROP CONSTRAINT ak_beds_id_ward_id;");
        }
    }
}
