# Conflux

Conflux is a distributed inventory and reservation platform built with **.NET 10, ASP.NET Core, PostgreSQL, Redis, Kafka, EF Core, and gRPC**.

The project focuses on **concurrent reservations, distributed coordination, correctness, idempotency, transactional messaging, workflow orchestration, and horizontal scalability**.

## Architecture

```text
                         ┌──────────────────┐
                         │     Clients      │
                         └────────┬─────────┘
                                  │
                           HTTP / gRPC
                                  │
                                  ▼
                    ┌─────────────────────────┐
                    │     ASP.NET Core API    │
                    │ Inventory / Reservation │
                    └────────────┬────────────┘
                                 │
              ┌──────────────────┼──────────────────┐
              │                  │                  │
              ▼                  ▼                  ▼
        ┌───────────┐      ┌───────────┐      ┌───────────┐
        │ PostgreSQL│      │   Redis   │      │   Kafka   │
        └─────┬─────┘      └───────────┘      └─────┬─────┘
              │                                      │
              ▼                                      ▼
        Inventory /                           Events / Sagas
        Reservations                          / Outbox
              │
              ▼
       Consistent Hashing
       ┌──────┼──────┐
       ▼      ▼      ▼
    Shard 0 Shard 1 ... Shard N
```

### Core Components

- **ASP.NET Core** — Inventory and reservation APIs
- **PostgreSQL + EF Core** — Transactional persistence
- **Redis** — Low-latency caching
- **Kafka** — Event-driven communication
- **Transactional Outbox** — Reliable event publication
- **Saga workflows** — Distributed workflow coordination
- **Idempotency** — Safe request retries
- **Consistent hashing** — Inventory shard routing
- **gRPC** — Internal service communication
- **OpenTelemetry** — Observability and diagnostics

## Project Structure

```text
conflux/
├── src/
├── tests/
├── benchmarks/
│   └── Conflux.Benchmarks/
├── load-tests/
│   └── Conflux.LoadTests/
├── deploy/
│   └── docker/
├── docs/
│   ├── BENCHMARKING.md
│   └── CAPACITY-PLANNING.md
└── README.md
```

# Running Conflux

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- PostgreSQL 17
- Redis 7.4
- Kafka

The repository uses the SDK version defined in `global.json`.

## Build

```powershell
dotnet build -c Release
```

## Run with Docker Compose

```powershell
docker compose -f .\deploy\docker\docker-compose.yml up -d --build
```

The Inventory API is exposed at:

```text
http://localhost:8080
```

## Create Inventory

```powershell
$body = @{
    Sku = "CONFLUX-001"
    AvailableQuantity = 1000
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:8080/api/v1/inventory/items" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

## Reserve Inventory

```powershell
$body = @{
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:8080/api/v1/inventory/items/<INVENTORY_ID>/reservations" `
    -Method Post `
    -ContentType "application/json" `
    -Headers @{
        "Idempotency-Key" = [Guid]::NewGuid().ToString()
    } `
    -Body $body
```

# Testing

Run the complete test suite:

```powershell
dotnet test -c Release
```

Current validated result:

```text
130 / 130 tests passing
```

# Performance Benchmarking

Conflux contains two performance-testing layers:

### BenchmarkDotNet

Microbenchmarks cover:

- Reservation contention
- Idempotency lookup
- Serialization
- Shard routing
- Cache lookup
- PostgreSQL vs Redis lookup

Run:

```powershell
dotnet run `
    --project .\benchmarks\Conflux.Benchmarks\Conflux.Benchmarks.csproj `
    -c Release
```

### End-to-End Load Testing

Build the load tester:

```powershell
dotnet build `
    .\load-tests\Conflux.LoadTests\Conflux.LoadTests.csproj `
    -c Release
```

The load tester measures:

- Throughput
- P50 latency
- P95 latency
- P99 latency
- Maximum latency
- Error rate
- HTTP status distribution

Results are written to:

```text
artifacts/load-tests/run.json
```

## Reservation Load Test

Create a benchmark inventory item:

```powershell
$sku = "LOADTEST-" + [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff")

$body = @{
    Sku = $sku
    AvailableQuantity = 100000
} | ConvertTo-Json

$created = Invoke-RestMethod `
    -Uri "http://localhost:8080/api/v1/inventory/items" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

$benchmarkInventoryId = $created.inventoryId
```

Run a concurrency test:

```powershell
dotnet run `
    --project .\load-tests\Conflux.LoadTests\Conflux.LoadTests.csproj `
    -c Release -- `
    '--base-url=http://localhost:8080' `
    '--method=POST' `
    "--path=/api/v1/inventory/items/$benchmarkInventoryId/reservations" `
    '--requests=1000' `
    '--concurrency=16' `
    '--warmup=0' `
    '--body-base64=eyJxdWFudGl0eSI6MX0=' `
    '--idempotency-key={guid}'
```

# Benchmark Results

All reservation load tests used **1,000 requests** and unique idempotency keys.

## Hot-Row Contention

All requests targeted the same inventory row.

| Concurrency | Throughput | P50 | P95 | P99 | Errors |
|---:|---:|---:|---:|---:|---:|
| 1 | 271.8 req/s | 3.33 ms | 5.21 ms | 6.45 ms | 0% |
| 4 | **438.8 req/s** | 7.33 ms | 17.47 ms | 23.61 ms | 0% |
| 16 | 368.3 req/s | 29.97 ms | 126.09 ms | 184.49 ms | 0% |
| 64 | 294.2 req/s | 127.66 ms | 733.24 ms | 1,102.09 ms | 0% |

The hot-row workload reaches peak measured throughput at approximately **C=4**. Increasing concurrency beyond that point increases queueing and tail latency rather than throughput.

## Distributed Inventory

Requests were distributed across **100 inventory rows**.

| Concurrency | Throughput | P50 | P95 | P99 | Errors |
|---:|---:|---:|---:|---:|---:|
| 4 | 632.2 req/s | 5.62 ms | 7.99 ms | 12.66 ms | 0% |
| 16 | **1,594.3 req/s** | 6.97 ms | 13.60 ms | 114.30 ms | 0% |
| 64 | **1,554.1 req/s** | 24.54 ms | 163.63 ms | 315.47 ms | 0% |

At C=64:

```text
Hot-row:       294.2 req/s
Distributed: 1,554.1 req/s
```

Distributing reservations across inventory rows produced approximately a **5.3× throughput improvement**, demonstrating significant hot-row contention in the reservation path.

## Shard-Routing Optimization

Consistent-hash routing was optimized from a linear ceiling lookup to binary search over immutable sorted hash arrays.

For 100,000 routing operations:

| Shards | Before | After |
|---:|---:|---:|
| 4 | 391.2 ms | 25.0 ms |
| 16 | 1,829.7 ms | 25.4 ms |
| 64 | 10,127.6 ms | 27.4 ms |

At 64 shards:

- **~370× faster routing**
- **~99.7% reduction in measured routing time**
- Allocations reduced from **63.19 MB to 9.08 MB**

Scaling from 4 to 64 shards improved from approximately **25.9× growth** in routing time to approximately **1.1×**.

# Benchmark Artifacts

Additional benchmarking and capacity-planning documentation:

```text
docs/BENCHMARKING.md
docs/CAPACITY-PLANNING.md
```

Benchmark results:

```text
artifacts/load-tests/run.json
```