# Deployment runbook — Proctored Exam Service

Nothing in this repo needs code changes to deploy. Set the environment, run one command, verify health.

## Required environment (copy `.env.example` to `.env` and fill)

| Variable | Notes |
|---|---|
| `POSTGRES_PASSWORD` | Strong DB password |
| `MINIO_ROOT_PASSWORD` | >= 8 chars |
| `JWT_KEY` | **Must be identical to the main platform's key** (>= 32 chars, not the dev default) |
| `MINIO_PUBLIC_ENDPOINT` | Browser-reachable MinIO URL, e.g. `https://exams-storage.example.com` |
| `CORS_ORIGIN` | Deployed web origin, e.g. `https://exams.example.com` |

Optional: `JWT_ISSUER`, `JWT_AUDIENCE`, `RATE_LIMIT_PER_MINUTE`, `OTEL_ENDPOINT`,
`AI_ENDPOINT`, `AI_API_KEY`, `AI_MODEL`, `AI_FACE_ENDPOINT`, `RESULT_WEBHOOK`,
`WEB_PORT`, `POSTGRES_*`, `MINIO_ROOT_USER`, `MINIO_PORT`, `MINIO_CONSOLE_PORT`.

## Deploy

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

The `migrate` job applies EF migrations first; the API waits for it to complete successfully.
No manual `dotnet ef` commands are needed.

## Verify

```bash
curl -fsS http://<host>:18090/api/health        # service health
curl -fsS http://<host>:18090/openapi/v1.json   # API contract
curl -fsS http://<host>:13010/                  # web
```

## Integrate with the main platform

Set `RESULT_WEBHOOK=https://<main-api>/api/integrations/exam-results` here, and
`EXAM_INTEGRATION_KEY=<shared-secret>` on the main platform. Results published here then create
in-app notifications for candidates there.

## Backups

```bash
./scripts/backup.sh                     # schedules externally (cron/CI)
./scripts/restore.sh backups/<timestamp>
```

Backups contain Postgres data and MinIO objects. Store them off-host.

## Scaling

Run multiple API replicas behind a load balancer. Every replica must get the same `JWT_KEY`,
`Redis__ConnectionString`, database and MinIO settings. SignalR uses the Redis backplane and the
rate limiter is global via Redis, so replicas share state. Keep `db`, `cache` and `storage` as
single services unless you also set up Postgres replication, Redis Sentinel and MinIO distributed mode.

## Rollback

Use the previously pushed image tag from GHCR (`ghcr.io/<owner>/proctored-exam-api:<sha>`), set
it in the compose image field, restore the last backup with `scripts/restore.sh`, and restart.
