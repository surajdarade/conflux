using System.Security.Cryptography;
using System.Text;

namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Routes SKU keys to logical inventory shards using a consistent hash ring.
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly SortedDictionary<ulong, string> _ring = new();

    /// <summary>Creates a ring from the supplied shard names.</summary>
    /// <param name="shards">Logical shard names.</param>
    /// <param name="virtualNodesPerShard">Virtual nodes assigned to each shard.</param>
    public ConsistentHashRing(IEnumerable<string> shards, int virtualNodesPerShard = 128)
    {
        var normalized = shards
            .Where(shard => !string.IsNullOrWhiteSpace(shard))
            .Select(shard => shard.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one shard is required.", nameof(shards));
        }

        if (virtualNodesPerShard <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualNodesPerShard));
        }

        foreach (var shard in normalized)
        {
            for (var node = 0; node < virtualNodesPerShard; node++)
            {
                _ring[Hash($"{shard}:{node}")] = shard;
            }
        }
    }

    /// <summary>Gets the shard responsible for a SKU.</summary>
    public string GetShard(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        var hash = Hash(sku.Trim());
        var candidate = _ring.FirstOrDefault(pair => pair.Key >= hash);
        return candidate.Equals(default(KeyValuePair<ulong, string>))
            ? _ring.First().Value
            : candidate.Value;
    }

    private static ulong Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt64(bytes, 0);
    }
}
