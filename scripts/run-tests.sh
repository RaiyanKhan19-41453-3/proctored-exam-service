#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
docker run --rm --network host \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -lc 'dotnet test tests/ExamService.Tests/ExamService.Tests.csproj -v minimal'
