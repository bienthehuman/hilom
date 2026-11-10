using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempted_identifier = table.Column<string>(type: "citext", maxLength: 320, nullable: true),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acting_for_patient_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.CheckConstraint("ck_audit_log_kind", "kind IN ('event', 'data_change', 'read')");
                    table.CheckConstraint("ck_audit_log_outcome", "outcome IN ('success', 'denied', 'failed')");
                    table.CheckConstraint("ck_audit_log_severity", "severity IN ('info', 'notice', 'critical')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_correlation_id",
                schema: "access",
                table: "audit_log",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_critical",
                schema: "access",
                table: "audit_log",
                column: "occurred_at",
                filter: "severity = 'critical'");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_event_occurred_at",
                schema: "access",
                table: "audit_log",
                columns: new[] { "event", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                schema: "access",
                table: "audit_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_patient_id_occurred_at",
                schema: "access",
                table: "audit_log",
                columns: new[] { "patient_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_user_id_occurred_at",
                schema: "access",
                table: "audit_log",
                columns: new[] { "user_id", "occurred_at" });

            // Append-only (docs/AUDIT.md, rule 5): reject UPDATE, DELETE and TRUNCATE for everyone, including the
            // table owner. The one exception is filling the tamper-evidence columns once (sealing, phase 4).
            migrationBuilder.Sql("""
                CREATE FUNCTION access.audit_log_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE'
                       AND OLD.sealed_at IS NULL
                       AND (to_jsonb(NEW) - 'prev_hash' - 'hash' - 'sealed_at')
                           = (to_jsonb(OLD) - 'prev_hash' - 'hash' - 'sealed_at') THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'access.audit_log is append-only (% blocked)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER audit_log_append_only
                    BEFORE UPDATE OR DELETE ON access.audit_log
                    FOR EACH ROW EXECUTE FUNCTION access.audit_log_append_only();

                CREATE TRIGGER audit_log_no_truncate
                    BEFORE TRUNCATE ON access.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION access.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_log_no_truncate ON access.audit_log;
                DROP TRIGGER IF EXISTS audit_log_append_only ON access.audit_log;
                DROP FUNCTION IF EXISTS access.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "access");
        }
    }
}
