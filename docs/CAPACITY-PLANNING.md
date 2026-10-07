# Conflux Capacity Planning

This document is a sizing model, not a performance claim. Replace assumptions with measurements from the benchmark artifacts before capacity commitments.

## Target workload

| Metric | Planning target |
|---|---:|
| Registered users | 10,000,000 |
| Peak gateway throughput | 100,000 RPS |
| Peak order creation | 10,000 orders/sec |
| Peak inventory reservations | 100,000 reservations/sec |
| Inventory physical shards | 4 baseline; scale horizontally |
| Kafka partitions | sized from measured consumer throughput |

## Core equations

`required_instances = ceil(peak_rps / measured_sustainable_rps_per_instance)`

`required_kafka_partitions = ceil(peak_events_per_sec / measured_events_per_partition_per_sec)`

`db_write_iops ≈ business_writes_per_request × peak_requests_per_sec`

`daily_event_storage ≈ events_per_sec × 86,400 × average_event_bytes`

`daily_db_storage ≈ rows_per_sec × 86,400 × average_row_bytes × index_multiplier`

## Inventory hot-key model

A single SKU is a serialized correctness boundary at the database row. Physical sharding distributes different SKUs across databases; it does not magically make one SKU infinitely parallel.

For a hot SKU, scale using:

1. SKU-level admission/rate limiting.
2. Sufficient database connection and CPU capacity on the owning shard.
3. Multiple inventory service instances behind the gateway.
4. Queueing/partitioning only if the measured database boundary becomes the bottleneck.
5. Never move authoritative inventory state to Redis.

## Benchmark matrix

Run every workload on the same machine/container limits and repeat at least five times:

| Scenario | Requests | Concurrency |
|---|---:|---:|
| Smoke | 1,000 | 16 |
| Baseline | 10,000 | 64 |
| Medium | 50,000 | 128 |
| High | 100,000 | 256 |
| Extreme | 250,000 | 512 |
| Million-request soak | 1,000,000 | 1,024 |

For hot-SKU tests, provision inventory equal to the intended number of successful reservations and verify:

`AvailableQuantity >= 0`

`ReservedQuantity = SUM(active reservation quantities)`

`successful reservations <= initial inventory`

## Scaling decision rule

Do not claim a target is sustainable merely because the test completed. A target is considered sustainable only when:

- error rate is within the declared SLO;
- p95/p99 remain within the declared latency budget;
- CPU, memory, GC and database saturation remain below operational limits;
- Kafka consumer lag returns to baseline after the load stops;
- Outbox backlog drains;
- no inventory/order/payment invariants are violated;
- repeated runs show no progressive degradation.
