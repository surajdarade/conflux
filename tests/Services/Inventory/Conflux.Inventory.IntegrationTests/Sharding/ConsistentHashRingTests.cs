using Conflux.Inventory.Infrastructure.Sharding;
using FluentAssertions;
using Xunit;

namespace Conflux.Inventory.IntegrationTests.Sharding;

/// <summary>Verifies consistent-hash routing behavior.</summary>
public sealed class ConsistentHashRingTests
{
    /// <summary>Verifies the same SKU is stable across repeated routing.</summary>
    [Fact]
    public void GetShard_IsStableForSameSku()
    {
        var ring = new ConsistentHashRing(["inventory-0", "inventory-1", "inventory-2"]);
        var first = ring.GetShard("CONFLUX-001");
        var second = ring.GetShard("CONFLUX-001");
        second.Should().Be(first);
    }

    /// <summary>Verifies a ring distributes a representative SKU set over multiple shards.</summary>
    [Fact]
    public void GetShard_DistributesKeys()
    {
        var ring = new ConsistentHashRing(["inventory-0", "inventory-1", "inventory-2"], 64);
        var shards = Enumerable.Range(0, 1000)
            .Select(index => ring.GetShard($"SKU-{index}"))
            .Distinct()
            .ToArray();
        shards.Should().HaveCountGreaterThan(1);
    }
}

/// <summary>Verifies physical-shard routing remains consistent between SKU and inventory identifier.</summary>
public sealed class InventoryShardRouterTests
{
    /// <summary>Verifies SKU and its deterministic inventory identifier resolve to the same shard.</summary>
    [Fact]
    public void SkuAndInventoryId_RouteToSameShard()
    {
        var options = new InventoryShardingOptions
        {
            Enabled = true,
            VirtualNodesPerShard = 128,
            Shards =
            [
                new InventoryShardOptions { Name = "inventory-0", ConnectionString = "Host=localhost" },
                new InventoryShardOptions { Name = "inventory-1", ConnectionString = "Host=localhost" },
                new InventoryShardOptions { Name = "inventory-2", ConnectionString = "Host=localhost" }
            ]
        };
        var router = new InventoryShardRouter(options);
        var id = InventoryShardKey.CreateInventoryId("SKU-42");

        router.GetShardForInventoryId(id).Should().Be(router.GetShardForSku("sku-42"));
    }

    /// <summary>Verifies the deterministic inventory identifier is stable across processes.</summary>
    [Fact]
    public void InventoryId_IsDeterministic()
    {
        InventoryShardKey.CreateInventoryId("SKU-42")
            .Should().Be(InventoryShardKey.CreateInventoryId(" sku-42 "));
    }
}
