using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "employee_number_seq",
                schema: "workforce");

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sex = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    contact_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "citext", maxLength: 320, nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    license_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    employment_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    separated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.CheckConstraint("ck_employees_employment_type", "employment_type IN ('full_time', 'part_time', 'contract')");
                    table.CheckConstraint("ck_employees_license_number", "professional_type NOT IN ('physician', 'nurse') OR (license_number IS NOT NULL AND btrim(license_number) <> '')");
                    table.CheckConstraint("ck_employees_professional_type", "professional_type IN ('physician', 'nurse', 'allied_health', 'administrative')");
                    table.CheckConstraint("ck_employees_separated_on", "(status = 'separated') = (separated_on IS NOT NULL)");
                    table.CheckConstraint("ck_employees_sex", "sex IN ('male', 'female')");
                    table.CheckConstraint("ck_employees_status", "status IN ('active', 'on_leave', 'separated')");
                    table.ForeignKey(
                        name: "fk_employees_departments_department_id",
                        column: x => x.department_id,
                        principalSchema: "workforce",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_positions_position_id",
                        column: x => x.position_id,
                        principalSchema: "workforce",
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physician_profiles",
                schema: "workforce",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specialization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepts_appointments = table.Column<bool>(type: "boolean", nullable: false),
                    consultation_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_physician_profiles", x => x.employee_id);
                    table.CheckConstraint("ck_physician_profiles_consultation_minutes", "consultation_minutes BETWEEN 5 AND 240");
                    table.ForeignKey(
                        name: "fk_physician_profiles_employees_id",
                        column: x => x.employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_physician_profiles_specializations_specialization_id",
                        column: x => x.specialization_id,
                        principalSchema: "workforce",
                        principalTable: "specializations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_department_id",
                schema: "workforce",
                table: "employees",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employee_no",
                schema: "workforce",
                table: "employees",
                column: "employee_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_last_name_first_name",
                schema: "workforce",
                table: "employees",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_position_id",
                schema: "workforce",
                table: "employees",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                schema: "workforce",
                table: "employees",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_physician_profiles_specialization_id",
                schema: "workforce",
                table: "physician_profiles",
                column: "specialization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "physician_profiles",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "workforce");

            migrationBuilder.DropSequence(
                name: "employee_number_seq",
                schema: "workforce");
        }
    }
}
