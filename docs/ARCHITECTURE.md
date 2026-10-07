# Conflux Architecture

Conflux is a backend-only distributed transaction platform for high-contention purchases against scarce inventory.

## Core topology

```text
Clients
  |
  v
YARP Gateway ---- Redis rate limiting
  |
  +--> Identity ---- PostgreSQL
  +--> Catalog  ---- PostgreSQL + Redis cache
  +--> Inventory -- gRPC --> PostgreSQL shard ring (4 physical DBs)
  +--> Order -------- PostgreSQL + Inbox/Outbox + Checkout Saga
  +--> Payment ------ PostgreSQL + Outbox
  +--> Fulfillment -- PostgreSQL + Inbox

Outbox -> Kafka -> Consumers/Inbox
Kafka source -> retry.1 -> retry.2 -> DLQ -> permanent DLQ
```

## Distributed correctness boundaries

1. Inventory correctness is enforced by PostgreSQL conditional updates.
2. Physical inventory ownership is deterministic by SKU consistent hashing.
3. Every service owns its own database.
4. Cross-service atomicity is intentionally avoided; Saga compensation provides workflow convergence.
5. Outbox publication is durable and at-least-once.
6. Kafka consumers are idempotent through Inbox records.
7. Payment state transitions are serialized at the payment row.
8. Order creation is protected by a database uniqueness constraint on the customer/idempotency-key boundary.
9. Redis is never the inventory source of truth.

## Failure model

The system assumes client retries, duplicate delivery, dependency latency, dependency failure, publisher crashes, consumer crashes and process restarts. Recovery relies on durable database state, Outbox/Inbox records, deterministic idempotency keys, retry topics and bounded DLQ recovery.

## Scaling model

Different SKUs scale horizontally through physical inventory shards. A single hot SKU remains a database-row contention boundary and is protected with atomic updates, rate limiting and dedicated load validation. Adding service replicas does not move authoritative state into process memory.

See `docs/CAPACITY-PLANNING.md`, `docs/INVARIANTS.md`, `docs/MESSAGING.md`, `docs/FAILURE-MATRIX.md`, and `docs/SHARDING.md` for the operational model.
