#!/usr/bin/env bash
# Rebuilds the MatNotes container and redeploys the dev stack (the project's live-reload / test workflow).
#   ./scripts/redeploy.sh [--port 2301] [--clean]
set -euo pipefail

port=2301
clean=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) port="$2"; shift ;;
    --clean) clean=1 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

cd "$(dirname "$0")/.."
export MATNOTE_CHANNEL=local
export MATNOTE_BUILD_DATE="$(date -u +%Y%m%d)"
export MATNOTE_PORT="$port"

if [[ $clean -eq 1 ]]; then
  docker compose -f docker-compose.dev.yml down -v --remove-orphans
fi

docker compose -f docker-compose.dev.yml up -d --build --remove-orphans

echo
echo "MatNotes local-${MATNOTE_BUILD_DATE} is running on http://localhost:${port} (pgAdmin on http://localhost:2302)"
