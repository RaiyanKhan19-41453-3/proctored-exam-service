# Proctored Exam Service

A **standalone microservice** for taking exams under supervision. It is not part of the main
coaching monolith — it has its own database, object storage, Git repo, Docker stack and CI-ready
test suite. It accepts the **same JWT** as the main platform (`Jwt__Key`, issuer and audience must match).

## Run

```bash
cp .env.example .env     # set JWT_KEY to the main platform's key
docker compose up -d --build
```

| Service | Port |
|---|---|
| Web (React) | 13010 |
| API + OpenAPI/Scalar | 18090 (`/openapi/v1.json`, `/scalar/v1`) |
| Postgres | 15433 |
| Redis | 16380 |
| MinIO | 9010 / 9011 |

## Features

- Exams with questions (MCQ, multi-select, true/false, short answer, essay) and publish workflow
- Attempts: start, autosave answers, submit; objective answers graded automatically
- Proctoring enforcement in the browser: tab blur, copy/paste, fullscreen exit with automatic
  re-entry, camera/mic stream loss — every event is reported and stored server-side
- Presigned snapshot uploads to MinIO; recordings tracked per attempt
- **AI-assisted reports** per candidate: objective marks, evaluated subjective marks, integrity
  score from proctor events, recommended decision (Approved/Flagged/Rejected). Examiner reviews
  only flagged reports. Works with an OpenAI-compatible endpoint (`AI__Endpoint`) or a local
  deterministic heuristic when none is configured.
- Server-side question and option randomization per attempt
- Device/IP/geo anomaly detection and answer-similarity (plagiarism) flags across candidates
- Live proctor dashboard over SignalR (Redis backplane) with real-time events and flags
- **Kiosk-ready, not kiosk-only**: `Exam.RequiresKiosk` defaults to false, so any normal browser
  works today. When a secure kiosk client is introduced later, flip the flag and send a
  kiosk attestation at attempt start — no backend rewrite needed.
- Tenant isolation via EF global query filters
- Hangfire worker auto-generates reports for submitted attempts
- OpenAPI + Scalar, health checks, tests with Testcontainers

## Security notes

- Browser-side enforcement is **detection, not prevention** — a determined attacker can bypass
  client code. Pair it with a locked-down exam browser/kiosk for high-stakes exams.
- JWT must be issued by the main platform with the same signing key, issuer and audience.
- Camera/mic snapshots live in MinIO under tenant-prefixed keys; audit trails are append-only.

## Tests

```bash
./scripts/run-tests.sh
```
