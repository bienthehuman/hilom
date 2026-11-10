using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWardAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ward_assignments",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<NpgsqlRange<DateOnly>>(type: "daterange", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ward_assignments", x => x.id);
                    table.CheckConstraint("ck_ward_assignments_period_bounded_start", "NOT isempty(period) AND NOT lower_inf(period)");
                    table.ForeignKey(
                        name: "fk_ward_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ward_assignments_users_assigned_by",
                        column: x => x.assigned_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ward_assignments_wards_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "facility",
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ward_assignments_assigned_by",
                schema: "workforce",
                table: "ward_assignments",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ix_ward_assignments_employee_id",
                schema: "workforce",
                table: "ward_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_ward_assignments_ward_id",
                schema: "workforce",
                table: "ward_assignments",
                column: "ward_id");

            // One ward at a time per employee (docs/DATA_MODEL.md). EF has no fluent API for exclusion
            // constraints; the name matches WardAssignmentConfiguration.OneWardAtATime.
            migrationBuilder.Sql(
                """
                ALTER TABLE workforce.ward_assignments
                ADD CONSTRAINT ex_ward_assignments_one_ward_at_a_time
                EXCLUDE USING gist (employee_id WITH =, period WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ward_assignments",
                schema: "workforce");
        }
    }
}
