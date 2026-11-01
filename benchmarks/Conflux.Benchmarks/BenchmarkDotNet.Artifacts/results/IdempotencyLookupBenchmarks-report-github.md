```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3


```
| Method | Entries | Mean     | Error    | StdDev    | Gen0   | Allocated |
|------- |-------- |---------:|---------:|----------:|-------:|----------:|
| **Lookup** | **1000**    | **47.96 ns** | **4.898 ns** | **14.441 ns** | **0.0068** |      **64 B** |
| **Lookup** | **100000**  | **50.58 ns** | **2.907 ns** |  **8.570 ns** | **0.0068** |      **64 B** |
| **Lookup** | **1000000** | **41.69 ns** | **1.936 ns** |  **5.677 ns** | **0.0068** |      **64 B** |
