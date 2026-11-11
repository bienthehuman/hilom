using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicianOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "physician_orders",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    encounter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    ordered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    order_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    medication_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    frequency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    discontinued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discontinued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    discontinue_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_physician_orders", x => x.id);
                    table.CheckConstraint("ck_physician_orders_details", "length(btrim(details)) > 0");
                    table.CheckConstraint("ck_physician_orders_discontinue", "(status <> 'discontinued' AND discontinued_at IS NULL AND discontinued_by IS NULL AND discontinue_reason IS NULL) OR (status = 'discontinued' AND discontinued_at IS NOT NULL AND discontinued_by IS NOT NULL AND length(btrim(discontinue_reason)) > 0)");
                    table.CheckConstraint("ck_physician_orders_medication_fields", "order_type <> 'medication' OR (length(btrim(coalesce(medication_name, ''))) > 0 AND length(btrim(coalesce(dose, ''))) > 0 AND length(btrim(coalesce(route, ''))) > 0 AND length(btrim(coalesce(frequency, ''))) > 0)");
                    table.CheckConstraint("ck_physician_orders_order_type", "order_type IN ('medication', 'laboratory', 'diet', 'nursing', 'other')");
                    table.CheckConstraint("ck_physician_orders_status", "status IN ('active', 'completed', 'discontinued')");
                    table.ForeignKey(
                        name: "fk_physician_orders_encounters_encounter_id",
                        column: x => x.encounter_id,
                        principalSchema: "clinical",
                        principalTable: "encounters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physician_orders_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physician_orders_users_discontinued_by",
                        column: x => x.discontinued_by,
                        principalSchema: "access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_physician_orders_discontinued_by",
                schema: "clinical",
                table: "physician_orders",
                column: "discontinued_by");

            migrationBuilder.CreateIndex(
                name: "ix_physician_orders_encounter_id_ordered_at",
                schema: "clinical",
                table: "physician_orders",
                columns: new[] { "encounter_id", "ordered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_physician_orders_patient_id_ordered_at",
                schema: "clinical",
                table: "physician_orders",
                columns: new[] { "patient_id", "ordered_at" });

            // The physician profile is an owned type, so EF can't map this foreign key (PhysicianOrderConfiguration).
            migrationBuilder.Sql(
                """
                ALTER TABLE clinical.physician_orders
                ADD CONSTRAINT fk_physician_orders_physician_profiles_ordered_by
                FOREIGN KEY (ordered_by) REFERENCES workforce.physician_profiles (employee_id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "physician_orders",
                schema: "clinical");
        }
    }
}
