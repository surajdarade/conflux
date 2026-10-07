# Conflux Production Validation Operations

## Start the production-like topology

```bash
docker compose -f deploy/docker/docker-compose.yml up -d --build
```

## Hot-SKU validation

```bash
CONFLUX_HOT_SKU_REQUESTS=10000 \
CONFLUX_HOT_SKU_CONCURRENCY=256 \
./scripts/run-hot-sku.sh
```

Scale to the intended workload only after the smaller run passes.

## Idempotency validation

```bash
CONFLUX_IDEMPOTENCY_REQUESTS=1000 \
CONFLUX_IDEMPOTENCY_CONCURRENCY=256 \
./scripts/run-idempotency-stress.sh
```

## Chaos validation

```bash
./scripts/chaos-run.sh
```

The script uses the Toxiproxy compose overlay and verifies inventory invariants after dependency degradation.

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

## Load-test matrix

```bash
dotnet run --project load-tests/Conflux.LoadTests -c Release -- \
  --base-url=http://localhost:8080 \
  --path=/health \
  --requests=250000 \
  --concurrency=512 \
  --output-directory=artifacts/load-tests \
  --output=250k.json
```

## Profiling

```bash
dotnet-counters monitor --process-id <PID> System.Runtime Microsoft.AspNetCore.Hosting
dotnet-trace collect --process-id <PID> --output artifacts/conflux.nettrace
dotnet-gcdump collect --process-id <PID> --output artifacts/conflux.gcdump
```
