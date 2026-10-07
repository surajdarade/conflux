# Toxiproxy chaos testing

The Docker stack includes Toxiproxy for controlled dependency failures.

Start it:

```bash
docker compose -f deploy/docker/docker-compose.yml up -d toxiproxy
```

Create a PostgreSQL proxy:

```bash
curl -X POST http://localhost:8474/proxies \
  -H 'Content-Type: application/json' \
  -d '{"name":"postgres-proxy","listen":"0.0.0.0:15432","upstream":"postgres:5432"}'
```

Inject 500 ms latency:

```bash
curl -X POST http://localhost:8474/proxies/postgres-proxy/toxics \
  -H 'Content-Type: application/json' \
  -d '{"name":"latency","type":"latency","attributes":{"latency":500,"jitter":100}}'
```

Remove the toxic:

```bash
curl -X DELETE http://localhost:8474/proxies/postgres-proxy/toxics/latency
```

Chaos runs should compare baseline and degraded dependency behavior while verifying that retries, timeouts, circuit breakers, Outbox, Inbox and idempotency preserve the system invariants.
