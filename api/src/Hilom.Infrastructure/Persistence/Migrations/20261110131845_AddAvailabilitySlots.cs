using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilitySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "availability_slots",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    physician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    during = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_slots", x => x.id);
                    table.CheckConstraint("ck_availability_slots_during", "NOT isempty(during) AND NOT lower_inf(during) AND NOT upper_inf(during) AND upper(during) > lower(during)");
                    table.CheckConstraint("ck_availability_slots_status", "status IN ('open', 'closed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_physician_id_status",
                schema: "scheduling",
                table: "availability_slots",
                columns: new[] { "physician_id", "status" });

            // EF has no fluent API for these; the names match AvailabilitySlotConfiguration.
            // A physician's open slots never overlap (docs/DATA_MODEL.md). Closed slots are history.
            migrationBuilder.Sql(
                """
                ALTER TABLE scheduling.availability_slots
                ADD CONSTRAINT ex_availability_slots_no_overlap
                EXCLUDE USING gist (physician_id WITH =, during WITH &&) WHERE (status = 'open');
                """);

            // Slots belong to a physician profile, an owned type stored in workforce.physician_profiles.
            migrationBuilder.Sql(
                """
                ALTER TABLE scheduling.availability_slots
                ADD CONSTRAINT fk_availability_slots_physician_profiles_physician_id
                FOREIGN KEY (physician_id) REFERENCES workforce.physician_profiles (employee_id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_slots",
                schema: "scheduling");
        }
    }
}
