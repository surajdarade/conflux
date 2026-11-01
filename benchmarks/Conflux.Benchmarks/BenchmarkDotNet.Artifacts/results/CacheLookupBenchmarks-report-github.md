```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3


```
| Method | Entries | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------- |-------- |---------:|---------:|---------:|---------:|-------:|----------:|
| **Lookup** | **10000**   | **31.31 ns** | **2.017 ns** | **5.621 ns** | **29.09 ns** | **0.0042** |      **40 B** |
| **Lookup** | **100000**  | **34.10 ns** | **1.431 ns** | **4.036 ns** | **32.62 ns** | **0.0042** |      **40 B** |
| **Lookup** | **1000000** | **43.26 ns** | **2.116 ns** | **6.173 ns** | **44.21 ns** | **0.0051** |      **48 B** |
