using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWardsAndBeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facility");

            migrationBuilder.CreateTable(
                name: "wards",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ward_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wards", x => x.id);
                    table.CheckConstraint("ck_wards_ward_type", "ward_type IN ('general', 'pediatric', 'icu', 'maternity', 'surgical', 'isolation')");
                    table.ForeignKey(
                        name: "fk_wards_departments_department_id",
                        column: x => x.department_id,
                        principalSchema: "workforce",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beds", x => x.id);
                    table.ForeignKey(
                        name: "fk_beds_wards_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "facility",
                        principalTable: "wards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_beds_ward_id_label",
                schema: "facility",
                table: "beds",
                columns: new[] { "ward_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wards_code",
                schema: "facility",
                table: "wards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wards_department_id",
                schema: "facility",
                table: "wards",
                column: "department_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beds",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "wards",
                schema: "facility");
        }
    }
}
