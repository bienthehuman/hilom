using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_account_links",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_patient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_mrn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    relationship = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    can_book = table.Column<bool>(type: "boolean", nullable: false),
                    can_view_records = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_account_links", x => x.id);
                    table.CheckConstraint("ck_patient_account_links_patient", "status IN ('pending', 'rejected') OR patient_id IS NOT NULL");
                    table.CheckConstraint("ck_patient_account_links_relationship", "relationship IN ('self', 'parent', 'guardian', 'spouse', 'child', 'representative')");
                    table.CheckConstraint("ck_patient_account_links_revoke_reason", "status <> 'revoked' OR (revoke_reason IS NOT NULL AND btrim(revoke_reason) <> '')");
                    table.CheckConstraint("ck_patient_account_links_status", "status IN ('pending', 'active', 'rejected', 'revoked')");
                    table.ForeignKey(
                        name: "fk_patient_account_links_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patient_account_links_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patient_account_links_patient_id",
                schema: "patients",
                table: "patient_account_links",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_account_links_status_requested_at",
                schema: "patients",
                table: "patient_account_links",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_account_links_user_id",
                schema: "patients",
                table: "patient_account_links",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_patient_account_links_active_self",
                schema: "patients",
                table: "patient_account_links",
                column: "patient_id",
                unique: true,
                filter: "relationship = 'self' AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_patient_account_links_user_patient_open",
                schema: "patients",
                table: "patient_account_links",
                columns: new[] { "user_id", "patient_id" },
                unique: true,
                filter: "status IN ('pending', 'active')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_account_links",
                schema: "patients");
        }
    }
}
