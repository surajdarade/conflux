# Conflux Benchmarking

Conflux reports measured performance only. No throughput, latency, CPU, memory or speedup number is fabricated in the repository.

## 1. Environment capture

Record for every run:

- CPU model and logical processors.
- RAM.
- OS.
- .NET SDK (`dotnet --info`).
- Docker version and container CPU/memory limits.
- PostgreSQL/Kafka/Redis versions.
- Conflux commit SHA.

## 2. Microbenchmarks

```bash
dotnet run --project benchmarks/Conflux.Benchmarks -c Release
```

The suite covers:

- reservation contention;
- idempotency lookup at 1K/100K/1M entries;
- JSON event serialization/deserialization;
- consistent-hash routing across 4/16/64 shards;
- cache lookup at 10K/100K/1M entries.

Real PostgreSQL-vs-Redis lookup benchmarking is opt-in:

```bash
export CONFLUX_EXTERNAL_BENCHMARKS=1
export CONFLUX_BENCHMARK_POSTGRES='Host=localhost;Port=5432;Database=conflux_inventory_shard_0;Username=postgres;Password=postgres'
export CONFLUX_BENCHMARK_REDIS='localhost:6379'
dotnet run --project benchmarks/Conflux.Benchmarks -c Release
```

## 3. Bounded HTTP load testing

```bash
dotnet run --project load-tests/Conflux.LoadTests -c Release -- \
  --base-url=http://localhost:8080 \
  --path=/health \
  --requests=250000 \
  --concurrency=512 \
  --output-directory=artifacts/load-tests \
  --output=250k.json
```

The generator uses a bounded worker pool and stores one latency/status slot per request instead of creating one Task per request.

## 4. Required business-path scenarios

### Hot SKU

```bash
CONFLUX_HOT_SKU_REQUESTS=10000 \
CONFLUX_HOT_SKU_CONCURRENCY=256 \
./scripts/run-hot-sku.sh
```

Increase to 100K, 250K and 1M only after the smaller run passes.

Acceptance:

- no negative available inventory;
- successful reservations never exceed initial stock;
- reserved quantity equals active reservation sum.

### Duplicate order idempotency

```bash
CONFLUX_IDEMPOTENCY_REQUESTS=1000 \
CONFLUX_IDEMPOTENCY_CONCURRENCY=256 \
./scripts/run-idempotency-stress.sh
```

Acceptance: exactly one `201` for the logical customer/idempotency-key pair.

### Full checkout

```bash
./scripts/e2e-checkout-smoke.sh
```

Acceptance: Order converges to `Confirmed`, inventory decrements exactly once and Fulfillment is created.

## 5. Chaos

```bash
./scripts/chaos-run.sh
```

The chaos overlay sends Inventory PostgreSQL and Kafka through Toxiproxy. It injects dependency latency and verifies inventory invariants after recovery.

## 6. Recommended large-run matrix

| Workload | Requests | Concurrency |
|---|---:|---:|
| Smoke | 1,000 | 16 |
| Baseline | 10,000 | 64 |
| Medium | 50,000 | 128 |
| High | 100,000 | 256 |
| Extreme | 250,000 | 512 |
| Million-request soak | 1,000,000 | 1,024 |

Run each workload at least five times on identical infrastructure. Record p50/p95/p99, throughput, error rate, CPU, memory, GC and Kafka lag.

## 7. Report generation

```bash
python -m pip install pandas matplotlib tabulate
python scripts/benchmark/generate_report.py artifacts/load-tests/*.json --output artifacts/benchmark-report
```

## 8. Profiling

```bash
dotnet-counters monitor --process-id <PID> System.Runtime Microsoft.AspNetCore.Hosting
dotnet-trace collect --process-id <PID> --output artifacts/conflux.nettrace
dotnet-gcdump collect --process-id <PID> --output artifacts/conflux.gcdump
```
