```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method      | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------ |---------:|---------:|---------:|---------:|-------:|----------:|
| Serialize   | 282.0 ns | 15.87 ns | 46.80 ns | 260.1 ns | 0.0381 |     360 B |
| Deserialize | 593.5 ns | 23.93 ns | 70.18 ns | 566.8 ns | 0.0353 |     336 B |
