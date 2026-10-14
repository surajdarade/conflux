# Kafka retry isolation and broker validation

## Consumer identities

Retry and dead-letter topics can be isolated by setting `KafkaRetry:RetryIdentity`:

- Order: `order-checkout-saga`
- Fulfillment: `fulfillment-order-confirmed`
- Catalog Projection: `catalog-projection`

For example, identity `catalog-projection` yields `conflux.catalog.events.retry.catalog-projection.1` and `conflux.catalog.events.dlq.catalog-projection`. An empty identity retains legacy topic names. Topic-name compatibility does not migrate records between legacy and identity-specific topics.

Catalog Outbox publishes its transactional outbox and does not register the retry or DLQ workers, so it should not carry `KafkaRetry__*` environment settings.

## Real-broker integration tests

The project `tests/BuildingBlocks/Conflux.Kafka.IntegrationTests` exercises the actual Kafka retry publisher, retry worker, and DLQ recovery worker against a broker. Tests are opt-in: without `CONFLUX_KAFKA_BOOTSTRAP_SERVERS`, xUnit marks them skipped.

The repository script `scripts/test-kafka-integration.ps1` runs them inside the existing `docker_default` network so the test client can use the broker's advertised address `kafka:9092`:

```powershell
.\scripts\test-kafka-integration.ps1
```

The tests create uniquely named `conflux.integration-*` topics with one partition and replication factor one. They verify retry forwarding, DLQ recovery metadata, and quarantine of a retry message that lacks identity metadata. The fixture deletes its test topics at the end where broker topic deletion is enabled. If deletion is disabled, unique names prevent leftovers from affecting later runs.

The script does not start, stop, or recreate application services. It requires the existing `docker-kafka-1` container and `docker_default` network to be running. The SDK image may need to be pulled and NuGet restore requires network access.

## Safety notes

- Never publish synthetic test messages to `conflux.order.events`, `conflux.payment.events`, or `conflux.catalog.events`.
- Do not delete legacy retry/DLQ topics as part of the identity migration.
- End offsets indicate retained log position, not consumer lag or proof that a topic is empty after compaction/deletion.
- Unit tests use fake clients; broker integration tests are separate evidence and must be run explicitly.
