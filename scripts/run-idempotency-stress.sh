#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${CONFLUX_BASE_URL:-http://localhost:8080}"
REQUESTS="${CONFLUX_IDEMPOTENCY_REQUESTS:-1000}"
CONCURRENCY="${CONFLUX_IDEMPOTENCY_CONCURRENCY:-256}"
SKU="${CONFLUX_IDEMPOTENCY_SKU:-CONFLUX-IDEMPOTENCY-SKU-$(date +%s%N)}"
CUSTOMER="${CONFLUX_IDEMPOTENCY_CUSTOMER:-00000000-0000-0000-0000-000000000001}"
KEY="${CONFLUX_IDEMPOTENCY_KEY:-conflux-idempotency-stress-v1}"
OUT="${CONFLUX_IDEMPOTENCY_OUTPUT:-artifacts/load-tests/idempotency.json}"

mkdir -p "$(dirname "$OUT")"
curl -fsS -X POST "${BASE_URL}/api/v1/inventory/items" \
  -H 'Content-Type: application/json' \
  -d "{\"sku\":\"${SKU}\",\"availableQuantity\":${REQUESTS}}" >/dev/null || true

dotnet run --project load-tests/Conflux.LoadTests -c Release --no-build -- \
  --base-url="$BASE_URL" \
  --method=POST \
  --path=/api/v1/orders \
  --body="{\"customerId\":\"${CUSTOMER}\",\"items\":[{\"sku\":\"${SKU}\",\"quantity\":1,\"unitPrice\":10,\"currency\":\"INR\"}]}" \
  --idempotency-key="$KEY" \
  --requests="$REQUESTS" \
  --concurrency="$CONCURRENCY" \
  --warmup=0 \
  --output-directory="$(dirname "$OUT")" \
  --output="$(basename "$OUT")"

created=$(jq -r '.StatusCodes["201"] // 0' "$OUT")
[[ "$created" == "1" ]] || { echo "IDEMPOTENCY INVARIANT FAILED: expected exactly one 201, got ${created}"; exit 1; }

echo "Idempotency invariant passed: exactly one logical order was created across ${REQUESTS} concurrent requests."
