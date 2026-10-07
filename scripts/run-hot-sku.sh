#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${CONFLUX_BASE_URL:-http://localhost:8080}"
REQUESTS="${CONFLUX_HOT_SKU_REQUESTS:-10000}"
CONCURRENCY="${CONFLUX_HOT_SKU_CONCURRENCY:-256}"
SKU="${CONFLUX_HOT_SKU:-CONFLUX-HOT-SKU-$(date +%s%N)}"
OUT="${CONFLUX_HOT_SKU_OUTPUT:-artifacts/load-tests/hot-sku.json}"

mkdir -p "$(dirname "$OUT")"
create_response=$(curl -fsS -X POST "${BASE_URL}/api/v1/inventory/items" \
  -H 'Content-Type: application/json' \
  -d "{\"sku\":\"${SKU}\",\"availableQuantity\":${REQUESTS}}")
inventory_id=$(printf '%s' "$create_response" | jq -r '.inventoryId')
[[ -n "$inventory_id" && "$inventory_id" != "null" ]] || { echo "Could not create hot SKU inventory"; exit 1; }

dotnet run --project load-tests/Conflux.LoadTests -c Release --no-build -- \
  --base-url="$BASE_URL" \
  --method=POST \
  --path="/api/v1/inventory/items/${inventory_id}/reservations" \
  --body='{"quantity":1}' \
  --idempotency-key='hot-sku-{index}' \
  --requests="$REQUESTS" \
  --concurrency="$CONCURRENCY" \
  --warmup=100 \
  --output-directory="$(dirname "$OUT")" \
  --output="$(basename "$OUT")"

state=$(curl -fsS "${BASE_URL}/api/v1/inventory/items/${inventory_id}")
available=$(printf '%s' "$state" | jq -r '.availableQuantity')
reserved=$(printf '%s' "$state" | jq -r '.reservedQuantity')
[[ "$available" == "0" && "$reserved" == "$REQUESTS" ]] || {
  echo "HOT-SKU INVARIANT FAILED: available=${available}, reserved=${reserved}, expected=${REQUESTS}"
  exit 1
}

echo "Hot-SKU invariant passed: ${REQUESTS} successful reservations with zero overselling."
