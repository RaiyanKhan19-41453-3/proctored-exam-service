#!/usr/bin/env bash
# Dumps Postgres and the MinIO data directory into ./backups/<timestamp>/
set -euo pipefail
cd "$(dirname "$0")/.."
STAMP=$(date +%Y%m%d-%H%M%S)
DIR="backups/$STAMP"
mkdir -p "$DIR"
echo "Backing up to $DIR"
docker compose exec -T db pg_dump -U "${POSTGRES_USER:-exam}" "${POSTGRES_DB:-examservice}" > "$DIR/database.sql"
docker compose exec -T storage tar czf - -C /data . > "$DIR/objects.tar.gz"
echo "Done. Copy $DIR off this machine."
