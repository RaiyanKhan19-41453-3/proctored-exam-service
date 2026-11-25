# Proctored Exam Service

A **standalone microservice** for taking exams from a normal browser, with durable evidence capture, AI-assisted grading and live proctoring. It is separate from the main coaching platform: its own database, object storage, Docker stack, CI and release cycle. It accepts the **same JWT** as the main platform (`JWT_KEY`, issuer and audience must match).

> Main platform: [coaching-platform](https://github.com/RaiyanKhan19-41453-3/coaching-platform) · Tests: [coaching-e2e](https://github.com/RaiyanKhan19-41453-3/coaching-e2e)

## Contents

- [Quick start](#quick-start)
- [How an exam works](#how-an-exam-works)
- [Features](#features)
- [Architecture](#architecture)
- [API](#api)
- [Configuration](#configuration)
- [Deployment](#deployment)
- [Testing](#testing)
- [Limitations](#limitations)

## Quick start

```bash
cp .env.example .env        # JWT_KEY must match the main platform
docker compose up -d --build
```

| Service | URL | Purpose |
| --- | --- | --- |
| Web | http://localhost:13010 | Candidate and proctor UI |
| API | http://localhost:18090 | ASP.NET Core controllers |
| OpenAPI / Scalar | http://localhost:18090/openapi/v1.json · /scalar/v1 | API contract and docs |
| Health | http://localhost:18090/api/health | Liveness |
| Postgres | localhost:15433 | Exam data |
| Redis | localhost:16380 | SignalR backplane + global rate limit |
| MinIO | localhost:9010 (console 9011) | Recordings and snapshots |

## How an exam works

1. An examiner creates an exam, questions and publishes it.
2. The examiner assigns a candidate → the service returns a **single-use attempt token**.
3. The candidate accepts a recording consent, then starts the attempt with that token.
4. Questions and options are **randomized per attempt**; answers autosave with an offline queue.
5. Every 20 s a camera frame is captured and uploaded; microphone audio is recorded in chunks. Face-presence, multiple-face, face-covering and audio anomalies become proctor events.
6. On submit, objective answers auto-grade; subjective answers are AI-evaluated (or queued for human review when no AI endpoint is configured).
7. A consolidated report per candidate combines scores, integrity score and a recommended decision. Only flagged reports need manual review.
8. Publishing results writes an outbox event and pushes candidate notifications to the main platform.

## Features

- MCQ, multi-select, true/false, short-answer and essay questions
- Server-issued single-use attempt tokens, consent capture, optional face-match identity verification
- Hard server-side deadlines with automatic submission
- Tab/copy/paste/fullscreen/camera/microphone loss detection
- Periodic snapshots, audio chunks, checksums, 90-day retention and cleanup
- AI answer evaluation and integrity analysis (OpenAI-compatible endpoint) with deterministic local fallback
- Answer-similarity, shared-device, shared-IP and geo anomaly flags
- Live proctor dashboard over SignalR with Redis backplane
- Candidate result publication and cross-service notification delivery
- AWS S3 or MinIO storage via a single `IObjectStorage` interface

## Architecture

```
Api/Controllers, Hubs, Jobs, Middleware
        ↓
Application: DTOs, service interfaces, CQRS features, domain abstractions
        ↓
Domain: exams, attempts, evidence, reports, policies
        ↓
Infrastructure: EF Core + migrations, MinIO/S3, AI evaluator, evidence analyzer
```

All state lives in Postgres, Redis and MinIO/S3, so API replicas are safe to run behind a load balancer. EF row-version tokens, unique indexes and Hangfire `[DisableConcurrentExecution]` prevent cross-replica races.

## API

Representative endpoints (full contract in OpenAPI):

| Method | Path | Role |
| --- | --- | --- |
| POST | `/api/exams`, `/api/exams/{id}/publish` | Admin/Teacher |
| POST | `/api/candidates/assign` | Admin/Teacher |
| POST | `/api/candidate/consent`, `/api/candidate/verify` | Candidate |
| POST | `/api/attempts?examId=` | Candidate (attempt token) |
| POST | `/api/attempts/{id}/answers`, `/submit`, `/report`, `/events` | Attempt owner |
| GET | `/api/attempts/{id}/questions`, `/{id}/result` | Attempt owner |
| POST | `/api/proctor/exams/{id}/detect`, `/flags/{id}/review` | Admin/Teacher |
| GET | `/hubs/proctor` | Admin/Teacher (SignalR) |

## Configuration

Copy `.env.example` to `.env`:

| Variable | Notes |
| --- | --- |
| `JWT_KEY` | Must match the main platform; ≥ 32 chars |
| `POSTGRES_PASSWORD`, `MINIO_ROOT_PASSWORD` | Storage credentials |
| `STORAGE_PROVIDER` | `minio` (default) or `s3` |
| `AWS_REGION`, `AWS_BUCKET`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | S3 mode (IAM role preferred) |
| `AI_ENDPOINT`, `AI_API_KEY`, `AI_MODEL`, `AI_FACE_ENDPOINT` | Optional AI evaluation and identity matching |
| `RESULT_WEBHOOK` | Main platform URL for published-result notifications |
| `RATE_LIMIT_PER_MINUTE`, `OTEL_ENDPOINT` | Rate limiting and tracing |

## Deployment

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Required env and rollback notes: [DEPLOY.md](DEPLOY.md). A one-shot migration job runs before the API starts.

## Testing

```bash
./scripts/run-tests.sh                  # 15 tests with Testcontainers
./scripts/load-test.mjs                 # configurable concurrent-candidate simulation
cd ../e2e && ./run-e2e.sh               # browser + four adversarial suites (fuzzing, races, IDOR)
```

## Limitations

- Browser-side proctoring is **detection, not prevention**. A kiosk/secure browser is the escalated option; the platform is switch-ready via `Exam.RequiresKiosk` and a kiosk attestation at attempt start.
- Face matching requires a real `AI_FACE_ENDPOINT`; without it identity verification is unavailable and exams should set `RequireIdentityVerification=false`.
- Postgres, Redis and MinIO are single services until HA is configured.

## Related repositories

- [coaching-platform](https://github.com/RaiyanKhan19-41453-3/coaching-platform)
- [coaching-e2e](https://github.com/RaiyanKhan19-41453-3/coaching-e2e)
