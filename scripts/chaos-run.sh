#!/usr/bin/env bash
set -euo pipefail

COMPOSE=(docker compose -f deploy/docker/docker-compose.yml -f deploy/docker/docker-compose.chaos.yml)
BASE_URL="${CONFLUX_BASE_URL:-http://localhost:8080}"

"${COMPOSE[@]}" up -d postgres kafka redis toxiproxy

until curl -fsS http://localhost:8474/version >/dev/null; do
  sleep 1
done

# Proxy creation is intentionally performed before starting services that depend on it.
create_proxy() {
  local name="$1" listen="$2" upstream="$3"
  curl -fsS -X POST http://localhost:8474/proxies \
    -H 'Content-Type: application/json' \
    -d "{\"name\":\"${name}\",\"listen\":\"0.0.0.0:${listen}\",\"upstream\":\"${upstream}\"}" >/dev/null || true
}

create_proxy postgres-proxy 15432 postgres:5432
create_proxy kafka-proxy 19092 kafka:9092

"${COMPOSE[@]}" up -d --build

until curl -fsS "${BASE_URL}/health" >/dev/null; do
  sleep 2
done

echo "Baseline health:"
curl -fsS "${BASE_URL}/health"
echo

inject_latency() {
  local proxy="$1"
  curl -fsS -X POST "http://localhost:8474/proxies/${proxy}/toxics" \
    -H 'Content-Type: application/json' \
    -d '{"name":"latency","type":"latency","attributes":{"latency":500,"jitter":100}}' >/dev/null || true
}

remove_latency() {
  local proxy="$1"
  curl -fsS -X DELETE "http://localhost:8474/proxies/${proxy}/toxics/latency" >/dev/null || true
}

for proxy in postgres-proxy kafka-proxy; do
  echo "Injecting latency into ${proxy}..."
  inject_latency "${proxy}"
  curl -fsS "${BASE_URL}/health" >/dev/null
  remove_latency "${proxy}"
done

verify_invariants() {
  for shard in 0 1 2 3; do
    db="conflux_inventory_shard_${shard}"
    min_available=$(docker compose -f deploy/docker/docker-compose.yml exec -T postgres psql -U postgres -d "$db" -tAc 'SELECT COALESCE(MIN("AvailableQuantity"),0) FROM inventory_items')
    min_reserved=$(docker compose -f deploy/docker/docker-compose.yml exec -T postgres psql -U postgres -d "$db" -tAc 'SELECT COALESCE(MIN("ReservedQuantity"),0) FROM inventory_items')
    [[ "${min_available//[[:space:]]/}" =~ ^[0-9]+$ ]] || { echo "Invalid available quantity in ${db}"; exit 1; }
    [[ "${min_reserved//[[:space:]]/}" =~ ^[0-9]+$ ]] || { echo "Invalid reserved quantity in ${db}"; exit 1; }
    active_reservations=$(docker compose -f deploy/docker/docker-compose.yml exec -T postgres psql -U postgres -d "$db" -tAc 'SELECT COALESCE(SUM("Quantity"),0) FROM inventory_reservations WHERE "ReleasedAt" IS NULL')
    total_reserved=$(docker compose -f deploy/docker/docker-compose.yml exec -T postgres psql -U postgres -d "$db" -tAc 'SELECT COALESCE(SUM("ReservedQuantity"),0) FROM inventory_items')
    [[ "${active_reservations//[[:space:]]/}" == "${total_reserved//[[:space:]]/}" ]] || { echo "Reservation invariant failed in ${db}: active=${active_reservations} inventory=${total_reserved}"; exit 1; }
  done
}

verify_invariants

echo "Chaos smoke test and inventory invariant verification passed. For business-path invariant validation set CONFLUX_CHAOS_LOAD_ARGS and run the dedicated load/invariant harness."
