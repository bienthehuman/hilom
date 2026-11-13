using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // From the 4.4 quality pass: EXPLAIN ANALYZE against the Standard demo (docs/ARCHITECTURE.md, performance).
            // Day lists and the booking search filter slots by start time; EF can't model this expression index.
            migrationBuilder.Sql("CREATE INDEX ix_availability_slots_start ON scheduling.availability_slots (lower(during));");

            migrationBuilder.CreateIndex(
                name: "ix_encounters_started_at_id",
                schema: "clinical",
                table: "encounters",
                columns: new[] { "started_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_bed_assignments_open_by_ward",
                schema: "clinical",
                table: "bed_assignments",
                column: "ward_id",
                filter: "upper_inf(during)");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_attempted_identifier",
                schema: "access",
                table: "audit_log",
                column: "occurred_at",
                filter: "attempted_identifier IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_kind_occurred_at_id",
                schema: "access",
                table: "audit_log",
                columns: new[] { "kind", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_admissions_current",
                schema: "clinical",
                table: "admissions",
                column: "encounter_id",
                filter: "discharged_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX scheduling.ix_availability_slots_start;");

            migrationBuilder.DropIndex(
                name: "ix_encounters_started_at_id",
                schema: "clinical",
                table: "encounters");

            migrationBuilder.DropIndex(
                name: "ix_bed_assignments_open_by_ward",
                schema: "clinical",
                table: "bed_assignments");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_attempted_identifier",
                schema: "access",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_kind_occurred_at_id",
                schema: "access",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_admissions_current",
                schema: "clinical",
                table: "admissions");
        }
    }
}
