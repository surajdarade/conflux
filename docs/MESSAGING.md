# Conflux Kafka Messaging

## Delivery model

Conflux uses transactional Outbox publication plus Kafka at-least-once delivery and durable Inbox processing.

## Retry topology

For every source topic:

```text
source
  -> source.retry.1
  -> source.retry.2
  -> source.dlq
  -> source.dlq.permanent
```

Retry delays use exponential backoff. Failed source messages are committed only after their retry/DLQ publication succeeds.

## DLQ recovery

A controlled automatic recovery worker performs at most one recovery attempt. A recovered message is marked with `x-conflux-recovered=true`. If it fails again, it is routed to `source.dlq.permanent` and is no longer automatically replayed.

## Operators

Inspect Kafka lag, retry topics, DLQ volume, Outbox backlog and Inbox failures before increasing traffic. Poison messages must be diagnosed rather than replayed indefinitely.
