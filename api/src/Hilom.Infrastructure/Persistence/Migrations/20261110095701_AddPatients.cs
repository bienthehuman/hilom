using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateSequence(
                name: "patient_mrn_seq",
                schema: "patients");

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mrn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sex = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    civil_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    religion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contact_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    emergency_contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    emergency_contact_relation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    emergency_contact_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                    table.CheckConstraint("ck_patients_civil_status", "civil_status IN ('single', 'married', 'widowed', 'separated', 'annulled')");
                    table.CheckConstraint("ck_patients_sex", "sex IN ('male', 'female')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_patients_birth_date",
                schema: "patients",
                table: "patients",
                column: "birth_date");

            migrationBuilder.CreateIndex(
                name: "ix_patients_last_name_first_name",
                schema: "patients",
                table: "patients",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_mrn",
                schema: "patients",
                table: "patients",
                column: "mrn",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patients",
                schema: "patients");

            migrationBuilder.DropSequence(
                name: "patient_mrn_seq",
                schema: "patients");
        }
    }
}
