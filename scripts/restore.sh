#!/usr/bin/env bash
# Restores a backup directory created by backup.sh. DESTRUCTIVE: replaces current data.
set -euo pipefail
cd "$(dirname "$0")/.."
DIR=${1:?usage: scripts/restore.sh backups/<timestamp>}
[ -f "$DIR/database.sql" ] || { echo "missing database.sql"; exit 1; }
[ -f "$DIR/objects.tar.gz" ] || { echo "missing objects.tar.gz"; exit 1; }
docker compose stop api
docker compose exec -T db psql -U "${POSTGRES_USER:-exam}" -d "${POSTGRES_DB:-examservice}" -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;' >/dev/null
docker compose exec -T db psql -U "${POSTGRES_USER:-exam}" -d "${POSTGRES_DB:-examservice}" < "$DIR/database.sql" >/dev/null
docker compose exec -T storage sh -c 'rm -rf /data/*'
docker compose exec -T storage tar xzf - -C /data < "$DIR/objects.tar.gz"
docker compose start api
echo "Restore complete."
