# Conflux System Invariants

These invariants are checked by unit/integration tests and by the chaos/load validation scripts where the required infrastructure is available.

## Inventory

- `AvailableQuantity >= 0`
- `ReservedQuantity >= 0`
- `ReservedQuantity == SUM(active reservation quantities)` per physical shard
- One `ReservationId` identifies one logical reservation
- A reservation cannot release more than once
- A failed conditional inventory update cannot oversell

## Orders

- One `(CustomerId, IdempotencyKey)` creates at most one logical order
- Concurrent retries cannot create duplicate business effects
- Confirmed orders require completed inventory reservation and payment capture
- Terminal states cannot transition to another terminal state illegally

## Messaging

- Every integration event is durably recorded before publication
- Kafka is treated as at-least-once
- Duplicate delivery is safe because consumers use Inbox/idempotency
- Failed messages move through retry topics before DLQ
- A DLQ recovery attempt is bounded and poison messages end in a permanent DLQ

## Payments

- Authorization, capture, void and refund are idempotent
- Payment row transitions are serialized
- A payment cannot be captured after it is voided/refunded

## Recovery

After a process/dependency failure, recovery must converge to a valid state without duplicating inventory, order or payment effects.
