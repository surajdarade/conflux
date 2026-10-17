```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method | Entries | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------- |-------- |---------:|---------:|---------:|---------:|-------:|----------:|
| **Lookup** | **1000**    | **36.50 ns** | **1.857 ns** | **5.418 ns** | **34.59 ns** | **0.0068** |      **64 B** |
| **Lookup** | **100000**  | **39.85 ns** | **1.062 ns** | **2.977 ns** | **39.10 ns** | **0.0068** |      **64 B** |
| **Lookup** | **1000000** | **37.47 ns** | **0.746 ns** | **0.859 ns** | **37.36 ns** | **0.0068** |      **64 B** |
