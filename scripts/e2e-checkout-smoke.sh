#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${CONFLUX_BASE_URL:-http://localhost:8080}"
SUFFIX="$(date +%s%N)"
SKU="CONFLUX-E2E-${SUFFIX}"
CUSTOMER="00000000-0000-0000-0000-000000000123"
KEY="conflux-e2e-${SUFFIX}"

curl -fsS "${BASE_URL}/health" >/dev/null
inventory=""
for i in $(seq 1 60); do
  if inventory=$(curl -fsS -X POST "${BASE_URL}/api/v1/inventory/items" \
    -H 'Content-Type: application/json' \
    -d "{\"sku\":\"${SKU}\",\"availableQuantity\":10}"); then
    break
  fi
  sleep 1
done
[[ -n "$inventory" ]] || { echo "Inventory service did not become ready"; exit 1; }
inventory_id=$(printf '%s' "$inventory" | jq -r '.inventoryId')

order=""
for i in $(seq 1 30); do
  if order=$(curl -fsS -X POST "${BASE_URL}/api/v1/orders" \
    -H 'Content-Type: application/json' \
    -H "Idempotency-Key: ${KEY}" \
    -d "{\"customerId\":\"${CUSTOMER}\",\"items\":[{\"sku\":\"${SKU}\",\"quantity\":1,\"unitPrice\":100,\"currency\":\"INR\"}]}"); then
    break
  fi
  sleep 1
done
order_id=$(printf '%s' "$order" | jq -r '.orderId')
[[ -n "$order_id" && "$order_id" != "null" ]] || { echo "Order creation failed"; exit 1; }

for i in $(seq 1 60); do
  state=$(curl -fsS "${BASE_URL}/api/v1/orders/${order_id}")
  status=$(printf '%s' "$state" | jq -r '.status')
  if [[ "$status" == "Confirmed" || "$status" == "3" ]]; then
    break
  fi
  if [[ "$status" == "Failed" || "$status" == "Cancelled" ]]; then
    echo "Checkout failed: ${state}"; exit 1
  fi
  sleep 1
done

final_state=$(curl -fsS "${BASE_URL}/api/v1/orders/${order_id}")
final_status=$(printf '%s' "$final_state" | jq -r '.status')
[[ "$final_status" == "Confirmed" || "$final_status" == "3" ]] || { echo "Checkout did not converge: ${final_state}"; exit 1; }

inventory_state=$(curl -fsS "${BASE_URL}/api/v1/inventory/items/${inventory_id}")
available=$(printf '%s' "$inventory_state" | jq -r '.availableQuantity')
reserved=$(printf '%s' "$inventory_state" | jq -r '.reservedQuantity')
[[ "$available" == "9" && "$reserved" == "1" ]] || { echo "Inventory invariant failed: ${inventory_state}"; exit 1; }

fulfillment=$(curl -fsS "${BASE_URL}/api/v1/fulfillment/${order_id}")
printf '%s\n' "$fulfillment"
echo "E2E checkout smoke test passed: order=${order_id}, status=${final_status}, inventory=${inventory_id}."
