using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentCensus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Who is in each bed now (docs/DATA_MODEL.md, views): open bed assignments of stays not discharged.
            // EF doesn't create views; CensusEntryConfiguration maps this one.
            migrationBuilder.Sql(
                """
                CREATE VIEW clinical.current_census AS
                SELECT b.id AS bed_assignment_id,
                       a.encounter_id,
                       a.patient_id,
                       p.mrn,
                       p.first_name,
                       p.last_name,
                       b.ward_id,
                       b.bed_id,
                       e.attending_physician_id,
                       ph.first_name AS physician_first_name,
                       ph.last_name AS physician_last_name,
                       a.admitted_at,
                       floor(extract(epoch FROM now() - a.admitted_at) / 3600)::integer AS admitted_hours
                FROM clinical.bed_assignments b
                JOIN clinical.admissions a ON a.encounter_id = b.admission_id
                JOIN clinical.encounters e ON e.id = a.encounter_id
                JOIN patients.patients p ON p.id = a.patient_id
                JOIN workforce.employees ph ON ph.id = e.attending_physician_id
                WHERE upper_inf(b.during) AND a.discharged_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW clinical.current_census;");
        }
    }
}
