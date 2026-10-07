using System.Security.Cryptography;
using System.Text;

namespace Conflux.Inventory.Infrastructure.Sharding;

/// <summary>
/// Routes SKU keys to logical inventory shards using a consistent hash ring.
/// </summary>
public sealed class ConsistentHashRing {
    private readonly ulong[] _hashes;
    private readonly string[] _shards;

    /// <summary>Creates a ring from the supplied shard names.</summary>
    /// <param name="shards">Logical shard names.</param>
    /// <param name="virtualNodesPerShard">Virtual nodes assigned to each shard.</param>
    public ConsistentHashRing(IEnumerable<string> shards, int virtualNodesPerShard = 128) {
        var normalized = shards
            .Where(shard => !string.IsNullOrWhiteSpace(shard))
            .Select(shard => shard.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0) {
            throw new ArgumentException("At least one shard is required.", nameof(shards));
        }

        if (virtualNodesPerShard <= 0) {
            throw new ArgumentOutOfRangeException(nameof(virtualNodesPerShard));
        }

        var nodes = new List<(ulong Hash, string Shard)>(
            normalized.Length * virtualNodesPerShard);

        foreach (var shard in normalized) {
            for (var node = 0; node < virtualNodesPerShard; node++) {
                nodes.Add((Hash($"{shard}:{node}"), shard));
            }
        }

        nodes.Sort(static (left, right) => left.Hash.CompareTo(right.Hash));

        _hashes = new ulong[nodes.Count];
        _shards = new string[nodes.Count];

        for (var index = 0; index < nodes.Count; index++) {
            _hashes[index] = nodes[index].Hash;
            _shards[index] = nodes[index].Shard;
        }
    }

    /// <summary>Gets the shard responsible for a SKU.</summary>
    public string GetShard(string sku) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        var hash = Hash(sku.Trim());
        var index = FindCeilingIndex(hash);

        return _shards[index];
    }

    private int FindCeilingIndex(ulong hash) {
        var low = 0;
        var high = _hashes.Length - 1;

        while (low <= high) {
            var middle = low + ((high - low) >> 1);

            if (_hashes[middle] < hash) {
                low = middle + 1;
            }
            else {
                high = middle - 1;
            }
        }

        // No ring point is >= hash, so wrap around to the first node.
        return low < _hashes.Length ? low : 0;
    }

    private static ulong Hash(string value) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt64(bytes, 0);
    }
}