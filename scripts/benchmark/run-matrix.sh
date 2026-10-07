#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:8080}"
PATH_TO_TEST="${PATH_TO_TEST:-/health}"
OUT="${OUT:-artifacts/load-tests}"
mkdir -p "$OUT"

while IFS=' ' read -r name requests concurrency; do
  echo "Running $name..."
  dotnet run --project load-tests/Conflux.LoadTests -c Release --no-restore -- \
    --base-url="$BASE_URL" \
    --path="$PATH_TO_TEST" \
    --requests="$requests" \
    --concurrency="$concurrency" \
    --output-directory="$OUT" \
    --output="$name.json"
done <<'WORKLOADS'
smoke-1x 1000 1
baseline-16x 10000 16
medium-64x 50000 64
high-256x 100000 256
saturation-512x 250000 512
WORKLOADS

python scripts/benchmark/generate_report.py "$OUT"/*.json --output artifacts/benchmark-report
