namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Configures physical inventory database sharding.
/// </summary>
public sealed class InventoryShardingOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "InventorySharding";

    /// <summary>Gets or sets whether physical sharding is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the number of virtual nodes per shard.</summary>
    public int VirtualNodesPerShard { get; set; } = 256;

    /// <summary>Gets or sets the configured physical shards.</summary>
    public List<InventoryShardOptions> Shards { get; set; } = [];
}

/// <summary>
/// Describes one physical inventory shard.
/// </summary>
public sealed class InventoryShardOptions
{
    /// <summary>Gets or sets the stable shard name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the PostgreSQL connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
