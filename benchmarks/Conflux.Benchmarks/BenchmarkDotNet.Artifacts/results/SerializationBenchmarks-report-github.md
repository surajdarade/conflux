```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3


```
| Method      | Mean     | Error    | StdDev    | Gen0   | Allocated |
|------------ |---------:|---------:|----------:|-------:|----------:|
| Serialize   | 330.1 ns | 22.65 ns |  66.80 ns | 0.0381 |     360 B |
| Deserialize | 656.7 ns | 37.36 ns | 110.16 ns | 0.0353 |     336 B |
