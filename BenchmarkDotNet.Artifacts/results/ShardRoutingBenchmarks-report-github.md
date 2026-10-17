```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method    | Shards | Mean     | Error    | StdDev   | Gen0      | Allocated |
|---------- |------- |---------:|---------:|---------:|----------:|----------:|
| **Route100K** | **4**      | **35.83 ms** | **1.450 ms** | **4.136 ms** | **1000.0000** |   **9.08 MB** |
| **Route100K** | **16**     | **33.61 ms** | **1.406 ms** | **4.122 ms** | **1000.0000** |   **9.08 MB** |
| **Route100K** | **64**     | **41.93 ms** | **2.463 ms** | **7.262 ms** | **1000.0000** |   **9.08 MB** |
