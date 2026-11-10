using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_invites",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", maxLength: 320, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    initial_role_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_invites", x => x.id);
                    table.CheckConstraint("ck_staff_invites_accepted_or_revoked", "accepted_at IS NULL OR revoked_at IS NULL");
                    table.ForeignKey(
                        name: "fk_staff_invites_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "workforce",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invites_employee_id",
                schema: "workforce",
                table: "staff_invites",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invites_token_hash",
                schema: "workforce",
                table: "staff_invites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_staff_invites_one_open_per_employee",
                schema: "workforce",
                table: "staff_invites",
                column: "employee_id",
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_invites",
                schema: "workforce");
        }
    }
}
