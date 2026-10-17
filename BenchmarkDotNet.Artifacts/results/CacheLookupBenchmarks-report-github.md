```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method | Entries | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------- |-------- |---------:|---------:|---------:|-------:|----------:|
| **Lookup** | **10000**   | **28.56 ns** | **0.602 ns** | **1.256 ns** | **0.0042** |      **40 B** |
| **Lookup** | **100000**  | **31.88 ns** | **0.666 ns** | **0.684 ns** | **0.0042** |      **40 B** |
| **Lookup** | **1000000** | **27.02 ns** | **0.541 ns** | **0.623 ns** | **0.0051** |      **48 B** |
