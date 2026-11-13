using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hilom.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The restricted role the app runs as (docs/AUDIT.md, rule 5). It reads and writes the module tables, but on
    /// <c>access.audit_log</c> it may only <c>SELECT</c> and <c>INSERT</c>, plus fill the three sealing columns, which
    /// the append-only trigger allows once per row. No <c>UPDATE</c> of an entry's contents, no <c>DELETE</c>, no
    /// <c>TRUNCATE</c>, and it owns nothing, so it can't drop or alter the table either.
    /// <para>
    /// The role is created here without a login if it doesn't exist yet. Giving it a login (and a password) is a
    /// deployment step: deploy/db/create-app-login.sh for Docker Compose.
    /// </para>
    /// </summary>
    public partial class AddAppRole : Migration
    {
        private const string Role = DatabaseConnection.AppRole;

        private const string Schemas = "access, facility, workforce, patients, scheduling, clinical";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Roles belong to the whole server, so another database on it may have created this one already.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    CREATE ROLE {Role} NOLOGIN;
                EXCEPTION WHEN duplicate_object THEN NULL;
                END
                $$;
                """);

            // Tables and sequences that exist now, and (default privileges) the ones later migrations create.
            migrationBuilder.Sql($"""
                GRANT USAGE ON SCHEMA {Schemas} TO {Role};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schemas} TO {Role};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Schemas} TO {Role};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Role};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas} GRANT USAGE, SELECT ON SEQUENCES TO {Role};
                """);

            // The audit log: read and append. Sealing fills prev_hash, hash and sealed_at once (AuditChain).
            migrationBuilder.Sql($"""
                REVOKE ALL ON access.audit_log FROM {Role};
                GRANT SELECT, INSERT ON access.audit_log TO {Role};
                GRANT UPDATE (prev_hash, hash, sealed_at) ON access.audit_log TO {Role};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The role itself stays: other databases on the server may still use it.
            migrationBuilder.Sql($"""
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas} REVOKE ALL ON TABLES FROM {Role};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas} REVOKE ALL ON SEQUENCES FROM {Role};
                REVOKE ALL ON ALL TABLES IN SCHEMA {Schemas} FROM {Role};
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA {Schemas} FROM {Role};
                REVOKE USAGE ON SCHEMA {Schemas} FROM {Role};
                """);
        }
    }
}
