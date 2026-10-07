# Conflux Physical Inventory Sharding

## Routing

Conflux uses a consistent-hash ring with virtual nodes. A SKU is converted into a deterministic inventory identifier and both the SKU and identifier resolve to the same shard.

## Physical layout

The Docker production-like topology provisions four PostgreSQL databases:

```text
conflux_inventory_shard_0
conflux_inventory_shard_1
conflux_inventory_shard_2
conflux_inventory_shard_3
```

Each shard has the same EF Core schema and its own Outbox. The Inventory API routes reads/writes to the owning shard, while the Inventory Outbox publisher polls every shard independently.

## Rebalancing

The ring is deterministic, but live data migration is intentionally a separate operational step. A safe migration must:

1. freeze ownership changes for the SKU range;
2. copy authoritative rows and reservation state;
3. validate row counts and invariants;
4. dual-read/verify during cutover;
5. switch the ring version;
6. keep the old shard read-only until the cutover is validated.

Never migrate an inventory SKU by copying only the `inventory_items` row; active reservations and Outbox state are part of the authoritative state.

## Existing unsharded data

The production-like deployment is intended to be initialized with physical sharding enabled. Existing legacy rows created with random inventory IDs must not be switched to the sharded router blindly. Migrate each SKU together with its reservations and Outbox records, then issue deterministic IDs and validate all downstream references before changing the ring configuration.
