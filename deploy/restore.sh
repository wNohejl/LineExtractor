#!/usr/bin/env bash
# Restore a custom-format dump into the server's database: a nightly backup from ./backups,
# or the development snapshot (data/snapshots/lineops.dump) for a first load.
#
#   ./restore.sh backups/lineops-2026-10-01T0400Z.dump
#   ./restore.sh lineops.dump --force      # replace a database that already holds games
#
# The same steps as scripts/restore-data.ps1: the schema is dropped and recreated rather than
# restored with --clean, because the partitioned odds tables trip --clean's constraint order.
# The desk and the worker are stopped first, so nothing writes into a half-restored schema,
# and started again after.
#
# Run this against a scratch copy once a quarter and record it as an incident with an RCA —
# a backup nobody has restored is a hope (going-public plan §3.3).
set -euo pipefail
cd "$(dirname "$0")"

dump="${1:?usage: ./restore.sh <file.dump> [--force]}"
force="${2:-}"
[[ -f "$dump" ]] || { echo "restore: no such file: $dump" >&2; exit 1; }

# SCRAM is required for local connections too (POSTGRES_INITDB_ARGS), so the tools inside the
# container need the password; it comes from .env, where compose reads it.
password=$(grep -E '^POSTGRES_PASSWORD=' .env | cut -d= -f2- | tr -d '"'"'"' \r')
pg() { docker compose exec -T -e PGPASSWORD="$password" postgres "$@"; }
psql() { pg psql -U lineops -d lineops -X -q -A -t -v ON_ERROR_STOP=1 "$@"; }

docker compose up -d postgres
until docker compose exec -T postgres pg_isready -U lineops -d lineops >/dev/null 2>&1; do sleep 1; done

games=$(echo 'select count(*) from "Games"' | psql 2>/dev/null || echo 0)
if [[ "${games:-0}" -gt 0 && "$force" != "--force" ]]; then
    echo "restore: this database holds $games games. Re-run with --force to replace them." >&2
    exit 1
fi

docker compose stop web worker
trap 'docker compose up -d web worker' EXIT

echo 'set client_min_messages = warning; drop schema public cascade; create schema public;' | psql
pg pg_restore -U lineops -d lineops --no-owner --no-privileges < "$dump"

echo "select s.\"Key\" || ': ' || count(*) from \"Games\" g join \"Sports\" s on s.\"Id\" = g.\"SportId\" group by s.\"Key\" order by 1" | psql
echo "restore: done; starting web and worker."
