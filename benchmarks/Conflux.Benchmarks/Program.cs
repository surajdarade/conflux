using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Conflux.Inventory.Infrastructure.Sharding;
using StackExchange.Redis;
using Npgsql;

if (Environment.GetEnvironmentVariable("CONFLUX_EXTERNAL_BENCHMARKS") == "1") {
    BenchmarkRunner.Run<DatabaseVsRedisBenchmarks>();
}
else {
    BenchmarkRunner.Run<ReservationContentionBenchmarks>();
    BenchmarkRunner.Run<IdempotencyLookupBenchmarks>();
    BenchmarkRunner.Run<SerializationBenchmarks>();
    BenchmarkRunner.Run<ShardRoutingBenchmarks>();
    BenchmarkRunner.Run<CacheLookupBenchmarks>();
}

/// <summary>Compares serialized and atomic reservation under concurrent contention.</summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ReservationContentionBenchmarks {
    private const int InitialStock = 100_000;
    private const int TotalOperations = 100_000;

    private BaselineInventory _baseline = null!;
    private OptimizedInventory _optimized = null!;

    /// <summary>Gets the number of concurrent workers.</summary>
    [Params(1, 4, 16, 64)]
    public int Workers { get; set; }

    /// <summary>Initializes benchmark state for each iteration.</summary>
    [IterationSetup]
    public void Setup() {
        _baseline = new BaselineInventory(InitialStock);
        _optimized = new OptimizedInventory(InitialStock);
    }

    /// <summary>Measures lock-based reservation under concurrent contention.</summary>
    [Benchmark(Baseline = true)]
    public int LockSerializedReservation()
        => ReserveConcurrently(_baseline.TryReserve);

    /// <summary>Measures atomic reservation under concurrent contention.</summary>
    [Benchmark]
    public int AtomicReservation()
        => ReserveConcurrently(_optimized.TryReserve);

    private int ReserveConcurrently(Func<bool> operation) {
        var startGate = new ManualResetEventSlim(false);
        var tasks = new Task<int>[Workers];

        var baseOperationsPerWorker = TotalOperations / Workers;
        var remainder = TotalOperations % Workers;

        for (var worker = 0; worker < Workers; worker++) {
            var operations = baseOperationsPerWorker +
                (worker < remainder ? 1 : 0);

            tasks[worker] = Task.Run(() =>
            {
                startGate.Wait();

                var successful = 0;

                for (var index = 0; index < operations; index++) {
                    if (operation()) {
                        successful++;
                    }
                }

                return successful;
            });
        }

        startGate.Set();

        try {
            Task.WaitAll(tasks);
        }
        finally {
            startGate.Dispose();
        }

        var totalSuccessful = 0;

        foreach (var task in tasks) {
            totalSuccessful += task.Result;
        }

        return totalSuccessful;
    }

    private sealed class BaselineInventory {
        private readonly object _gate = new();
        private int _available;

        public BaselineInventory(int available) {
            _available = available;
        }

        public bool TryReserve() {
            lock (_gate) {
                if (_available <= 0) {
                    return false;
                }

                _available--;
                return true;
            }
        }
    }

    private sealed class OptimizedInventory {
        private int _available;

        public OptimizedInventory(int available) {
            _available = available;
        }

        public bool TryReserve() {
            while (true) {
                var current = Volatile.Read(ref _available);

                if (current <= 0) {
                    return false;
                }

                if (Interlocked.CompareExchange(
                        ref _available,
                        current - 1,
                        current) == current) {
                    return true;
                }
            }
        }
    }
}

/// <summary>Measures idempotency-key lookup cost at increasing cardinalities.</summary>
[MemoryDiagnoser]
public class IdempotencyLookupBenchmarks {
    private Dictionary<string, Guid> _keys = null!;

    /// <summary>Gets the number of entries in the idempotency index.</summary>
    [Params(1_000, 100_000, 1_000_000)]
    public int Entries { get; set; }

    /// <summary>Initializes the lookup index.</summary>
    [GlobalSetup]
    public void Setup() {
        _keys = Enumerable
            .Range(0, Entries)
            .ToDictionary(
                index => $"customer-{index % 10_000}:key-{index}",
                _ => Guid.NewGuid());
    }

    /// <summary>Measures an exact-key lookup.</summary>
    [Benchmark]
    public bool Lookup() {
        var index = Entries / 2;

        return _keys.TryGetValue(
            $"customer-{index % 10_000}:key-{index}",
            out _);
    }
}

/// <summary>Measures JSON event serialization and deserialization.</summary>
[MemoryDiagnoser]
public class SerializationBenchmarks {
    private readonly SampleEvent _event = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "SKU-42",
        2,
        1999.95m,
        "INR");

    private string _json = string.Empty;

    /// <summary>Prepares the serialized payload.</summary>
    [GlobalSetup]
    public void Setup() {
        _json = JsonSerializer.Serialize(_event);
    }

    /// <summary>Measures event serialization.</summary>
    [Benchmark]
    public string Serialize()
        => JsonSerializer.Serialize(_event);

    /// <summary>Measures event deserialization.</summary>
    [Benchmark]
    public SampleEvent? Deserialize()
        => JsonSerializer.Deserialize<SampleEvent>(_json);

    /// <summary>Represents a representative integration-event payload.</summary>
    public sealed record SampleEvent(
        Guid EventId,
        Guid CorrelationId,
        string Sku,
        int Quantity,
        decimal Amount,
        string Currency);
}


/// <summary>
/// Compares linear-search and binary-search consistent-hash routing
/// over identical SKU inputs and shard configurations.
/// </summary>
[MemoryDiagnoser]
public class ShardRoutingBenchmarks
{
    private ConsistentHashRing _binaryRing = null!;
    private LinearConsistentHashRing _linearRing = null!;
    private string[] _skus = null!;

    /// <summary>Gets the number of configured logical shards.</summary>
    [Params(4, 16, 64)]
    public int Shards { get; set; }

    /// <summary>
    /// Initializes both rings and the same 100K SKU input set.
    /// Validates that both algorithms produce identical shard mappings.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var shardNames = Enumerable
            .Range(0, Shards)
            .Select(index => $"inventory-{index}")
            .ToArray();

        // Existing production implementation: binary search.
        _binaryRing = new ConsistentHashRing(
            shardNames,
            256);

        // Benchmark-only reference implementation: linear search.
        _linearRing = new LinearConsistentHashRing(
            shardNames,
            256);

        // Identical input keys for both implementations.
        _skus = Enumerable
            .Range(0, 100_000)
            .Select(index => $"SKU-{index}")
            .ToArray();

        // Correctness check: both algorithms must route every SKU
        // to exactly the same logical shard.
        foreach (var sku in _skus)
        {
            var binaryShard = _binaryRing.GetShard(sku);
            var linearShard = _linearRing.GetShard(sku);

            if (!string.Equals(
                    binaryShard,
                    linearShard,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Routing mismatch for SKU '{sku}'. " +
                    $"Binary: {binaryShard}, Linear: {linearShard}");
            }
        }
    }

    /// <summary>Measures the original O(N) linear-search implementation.</summary>
    [Benchmark(Baseline = true)]
    public int LinearRoute100K()
    {
        var checksum = 0;

        foreach (var sku in _skus)
        {
            checksum ^= _linearRing
                .GetShard(sku)
                .GetHashCode();
        }

        return checksum;
    }

    /// <summary>Measures the optimized O(log N) binary-search implementation.</summary>
    [Benchmark]
    public int BinaryRoute100K()
    {
        var checksum = 0;

        foreach (var sku in _skus)
        {
            checksum ^= _binaryRing
                .GetShard(sku)
                .GetHashCode();
        }

        return checksum;
    }

    /// <summary>
    /// Benchmark-only copy of the consistent hash ring using
    /// linear search instead of binary search.
    /// Hashing, normalization, virtual-node construction and
    /// ordering intentionally match the production implementation.
    /// </summary>
    private sealed class LinearConsistentHashRing
    {
        private readonly ulong[] _hashes;
        private readonly string[] _shards;

        public LinearConsistentHashRing(
            IEnumerable<string> shards,
            int virtualNodesPerShard)
        {
            var normalized = shards
                .Where(shard => !string.IsNullOrWhiteSpace(shard))
                .Select(shard => shard.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (normalized.Length == 0)
            {
                throw new ArgumentException(
                    "At least one shard is required.",
                    nameof(shards));
            }

            if (virtualNodesPerShard <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(virtualNodesPerShard));
            }

            var nodes = new List<(ulong Hash, string Shard)>(
                normalized.Length * virtualNodesPerShard);

            foreach (var shard in normalized)
            {
                for (var node = 0; node < virtualNodesPerShard; node++)
                {
                    nodes.Add((
                        Hash($"{shard}:{node}"),
                        shard));
                }
            }

            nodes.Sort(static (left, right) =>
                left.Hash.CompareTo(right.Hash));

            _hashes = new ulong[nodes.Count];
            _shards = new string[nodes.Count];

            for (var index = 0; index < nodes.Count; index++)
            {
                _hashes[index] = nodes[index].Hash;
                _shards[index] = nodes[index].Shard;
            }
        }

        public string GetShard(string sku)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sku);

            var hash = Hash(sku.Trim());
            var index = FindCeilingIndexLinear(hash);

            return _shards[index];
        }

        // Original O(N) ring lookup.
        private int FindCeilingIndexLinear(ulong hash)
        {
            for (var index = 0; index < _hashes.Length; index++)
            {
                if (_hashes[index] >= hash)
                {
                    return index;
                }
            }

            // Wrap around when no ring point is >= hash.
            return 0;
        }

        // Identical SHA-256 hashing to production.
        private static ulong Hash(string value)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value));

            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}


/// <summary>Measures hot-cache lookup behavior at large cardinalities.</summary>
[MemoryDiagnoser]
public class CacheLookupBenchmarks {
    private Dictionary<string, string> _cache = null!;

    /// <summary>Gets the number of cached records.</summary>
    [Params(10_000, 100_000, 1_000_000)]
    public int Entries { get; set; }

    /// <summary>Initializes the cache.</summary>
    [GlobalSetup]
    public void Setup() {
        _cache = Enumerable
            .Range(0, Entries)
            .ToDictionary(
                index => $"SKU-{index}",
                index => $"payload-{index}");
    }

    /// <summary>Measures exact-key cache lookup.</summary>
    [Benchmark]
    public bool Lookup()
        => _cache.TryGetValue(
            $"SKU-{Entries / 2}",
            out _);
}

/// <summary>Measures real PostgreSQL and Redis lookup latency against configured infrastructure.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("External")]
public class DatabaseVsRedisBenchmarks {
    private NpgsqlDataSource _dataSource = null!;
    private IConnectionMultiplexer _redis = null!;
    private IDatabase _redisDatabase = null!;

    /// <summary>Initializes external benchmark dependencies.</summary>
    [GlobalSetup]
    public async Task Setup() {
        var postgres =
            Environment.GetEnvironmentVariable(
                "CONFLUX_BENCHMARK_POSTGRES")
            ?? throw new InvalidOperationException(
                "Set CONFLUX_BENCHMARK_POSTGRES for external benchmarks.");

        var redis =
            Environment.GetEnvironmentVariable(
                "CONFLUX_BENCHMARK_REDIS")
            ?? throw new InvalidOperationException(
                "Set CONFLUX_BENCHMARK_REDIS for external benchmarks.");

        _dataSource = NpgsqlDataSource.Create(postgres);

        _redis =
            await ConnectionMultiplexer.ConnectAsync(redis);

        _redisDatabase = _redis.GetDatabase();

        await _redisDatabase.StringSetAsync(
            "conflux:benchmark:key",
            "value");
    }

    /// <summary>Measures a parameterized PostgreSQL point lookup.</summary>
    [Benchmark]
    public async Task<string?> PostgreSqlLookup() {
        await using var command =
            _dataSource.CreateCommand(
                "SELECT 'value'");

        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>Measures a Redis point lookup.</summary>
    [Benchmark]
    public Task<RedisValue> RedisLookup()
        => _redisDatabase.StringGetAsync(
            "conflux:benchmark:key");

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task Cleanup() {
        await _dataSource.DisposeAsync();
        await _redis.DisposeAsync();
    }
}