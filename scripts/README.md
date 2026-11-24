# Scripts

- `run-tests.sh` — containerized test suite with Testcontainers
- `backup.sh` — dumps Postgres and MinIO objects into `backups/<timestamp>/`
- `restore.sh <dir>` — restores a backup (destructive; stops the API first)
- `load-test.mjs` — simulates `USERS` candidates starting, answering, submitting and generating a report
