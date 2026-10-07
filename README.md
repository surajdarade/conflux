# Conflux

Conflux is a backend-only distributed systems platform for safely processing high-contention purchases against scarce inventory.

## Stack

- .NET 10 / ASP.NET Core
- YARP API Gateway
- REST + gRPC
- PostgreSQL database-per-service
- Physical Inventory PostgreSQL sharding with consistent hashing
- Kafka with Outbox/Inbox, retry topics and DLQ
- Redis caching and distributed rate limiting
- Checkout Saga with compensation
- OpenTelemetry / Prometheus / Grafana / Jaeger
- Docker Compose + .NET Aspire
- xUnit + Testcontainers + architecture/contract tests
- BenchmarkDotNet + bounded large-scale HTTP load testing

## Build and test

```bash
dotnet restore Conflux.slnx
dotnet build Conflux.slnx -c Release
dotnet test Conflux.slnx -c Release
```

## Run production-like topology

```bash
docker compose -f deploy/docker/docker-compose.yml up -d --build
```

Gateway: `http://localhost:8080`

Health: `http://localhost:8080/health`

Prometheus: `http://localhost:9090`

Grafana: `http://localhost:3000`

Jaeger: `http://localhost:16686`

The production-like Inventory deployment uses four PostgreSQL shard databases and migrates every shard on startup.

## Full-stack E2E

```bash
./scripts/e2e-checkout-smoke.sh
```

## Hot-SKU correctness stress

```bash
CONFLUX_HOT_SKU_REQUESTS=10000 \
CONFLUX_HOT_SKU_CONCURRENCY=256 \
./scripts/run-hot-sku.sh
```

The script verifies zero overselling and the reservation accounting invariant after the load completes.

## Idempotency stress

```bash
CONFLUX_IDEMPOTENCY_REQUESTS=1000 \
CONFLUX_IDEMPOTENCY_CONCURRENCY=256 \
./scripts/run-idempotency-stress.sh
```

## Chaos validation

```bash
./scripts/chaos-run.sh
```

The chaos overlay routes Inventory PostgreSQL and Kafka through Toxiproxy, injects dependency latency, then verifies inventory invariants.

## BenchmarkDotNet

```bash
dotnet run --project benchmarks/Conflux.Benchmarks -c Release
```

For real PostgreSQL-vs-Redis measurements:

```bash
export CONFLUX_EXTERNAL_BENCHMARKS=1
export CONFLUX_BENCHMARK_POSTGRES='Host=localhost;Port=5432;Database=conflux_inventory_shard_0;Username=postgres;Password=postgres'
export CONFLUX_BENCHMARK_REDIS='localhost:6379'
dotnet run --project benchmarks/Conflux.Benchmarks -c Release
```

## Large HTTP load test

The load generator uses a bounded worker model, so large request counts do not create one in-memory Task per request.

```bash
dotnet run --project load-tests/Conflux.LoadTests -c Release -- \
  --base-url=http://localhost:8080 \
  --path=/health \
  --requests=250000 \
  --concurrency=512 \
  --output-directory=artifacts/load-tests \
  --output=250k.json
```

## CI validation

- `.github/workflows/ci.yml` — build, full-stack E2E, 10K hot-SKU correctness stress, tests and vulnerability audit.
- `.github/workflows/benchmark.yml` — full-stack benchmark workflow with configurable request count/concurrency.
- `.github/workflows/chaos.yml` — automated Toxiproxy chaos validation.

## Documentation

- `docs/ARCHITECTURE.md`
- `docs/IMPLEMENTATION-AUDIT.md`
- `docs/CAPACITY-PLANNING.md`
- `docs/INVARIANTS.md`
- `docs/MESSAGING.md`
- `docs/FAILURE-MATRIX.md`
- `docs/SHARDING.md`
- `docs/OPERATIONS.md`
- `docs/BENCHMARKING.md`

## Runtime verification note

This package was assembled in an environment without the .NET SDK and Docker engine, so no new runtime result is claimed as executed here. The repository includes CI and executable validation workflows that must be run on a .NET 10 + Docker environment before treating throughput/latency figures as measured production capacity.
