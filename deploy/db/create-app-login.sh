#!/usr/bin/env bash
# Gives the app's restricted database role (hilom_app, docs/AUDIT.md rule 5) a login with HILOM_DB_APP_PASSWORD.
# The role's privileges come from the AddAppRole migration; this only lets the API sign in as it.
#
# Docker Compose runs this once, when the database volume is first created (/docker-entrypoint-initdb.d).
# For a volume created before it existed, run it by hand (safe to repeat; it also resets the password):
#   docker compose exec db bash /docker-entrypoint-initdb.d/10-create-app-login.sh
set -euo pipefail

: "${HILOM_DB_APP_PASSWORD:?HILOM_DB_APP_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 \
  --username "${POSTGRES_USER}" --dbname "${POSTGRES_DB}" \
  --set=app_password="${HILOM_DB_APP_PASSWORD}" <<'SQL'
SELECT format('CREATE ROLE hilom_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hilom_app')
UNION ALL
SELECT format('ALTER ROLE hilom_app LOGIN PASSWORD %L', :'app_password')
WHERE EXISTS (SELECT FROM pg_roles WHERE rolname = 'hilom_app')
\gexec
SQL

echo "hilom_app can log in."
