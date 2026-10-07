using System.Security.Cryptography;
using System.Text;

namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Creates stable shard keys for inventory records.
/// </summary>
public static class InventoryShardKey
{
    /// <summary>
    /// Creates the deterministic inventory identifier used by physically sharded deployments.
    /// </summary>
    /// <param name="sku">The normalized or unnormalized SKU.</param>
    /// <returns>A deterministic identifier derived from the SKU.</returns>
    public static Guid CreateInventoryId(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sku.Trim().ToUpperInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
