namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Resolves inventory SKUs and deterministic inventory identifiers to physical shards.
/// </summary>
public sealed class InventoryShardRouter
{
    private readonly ConsistentHashRing _ring;

    /// <summary>Initializes a new router.</summary>
    /// <param name="options">The physical shard configuration.</param>
    public InventoryShardRouter(InventoryShardingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Shards.Count == 0)
        {
            throw new InvalidOperationException("At least one inventory shard must be configured when physical sharding is enabled.");
        }

        _ring = new ConsistentHashRing(
            options.Shards.Select(shard => shard.Name),
            options.VirtualNodesPerShard);
    }

    /// <summary>Gets the physical shard for a SKU.</summary>
    public string GetShardForSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return _ring.GetShard(InventoryShardKey.CreateInventoryId(sku).ToString("N"));
    }

    /// <summary>Gets the physical shard for an inventory identifier.</summary>
    public string GetShardForInventoryId(Guid inventoryItemId)
    {
        if (inventoryItemId == Guid.Empty)
        {
            throw new ArgumentException("Inventory item ID cannot be empty.", nameof(inventoryItemId));
        }

        return _ring.GetShard(inventoryItemId.ToString("N"));
    }
}
