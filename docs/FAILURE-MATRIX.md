# Conflux Failure Matrix

| Failure | Expected behavior | Verification |
|---|---|---|
| Client timeout | Client may retry using idempotency key | Idempotency stress test |
| Inventory timeout | Bounded gRPC retry; saga compensates | Chaos test |
| Payment timeout | HTTP resilience + payment idempotency | Chaos test |
| PostgreSQL latency | Request latency rises; no invariant violation | Toxiproxy |
| PostgreSQL outage | Local operation fails; Outbox/In-flight state remains recoverable | Toxiproxy + restart |
| Kafka outage | Outbox backlog accumulates and later drains | Stop broker + recovery |
| Consumer crash | Kafka redelivers; Inbox prevents duplicate effect | Kill consumer |
| Duplicate Kafka event | Consumer recognizes Inbox record | Duplicate-delivery integration test |
| Publisher crash | Claim lease expires and message is retried | Publisher restart |
| Poison event | Retry 1 → retry 2 → DLQ | Kafka retry integration test |
| Recovered poison event fails again | Permanent DLQ | DLQ recovery test |
| Hot SKU contention | Conditional update prevents overselling | `scripts/run-hot-sku.sh` |
| Multiple inventory shards | SKU remains on deterministic owner shard | shard routing tests |
