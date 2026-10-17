```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 135H 1.70GHz, 1 CPU, 18 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-CNUJVU : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  UnrollFactor=1  

```
| Method                    | Workers | Mean      | Error     | StdDev    | Ratio | RatioSD | Completed Work Items | Lock Contentions | Allocated | Alloc Ratio |
|-------------------------- |-------- |----------:|----------:|----------:|------:|--------:|---------------------:|-----------------:|----------:|------------:|
| **LockSerializedReservation** | **1**       |  **2.149 ms** | **0.0429 ms** | **0.0904 ms** |  **1.00** |    **0.06** |               **1.0000** |                **-** |     **464 B** |        **1.00** |
| AtomicReservation         | 1       |  1.167 ms | 0.0242 ms | 0.0712 ms |  0.54 |    0.04 |               1.0000 |                - |     464 B |        1.00 |
|                           |         |           |           |           |       |         |                      |                  |           |             |
| **LockSerializedReservation** | **4**       | **14.178 ms** | **0.4768 ms** | **1.3907 ms** |  **1.01** |    **0.14** |               **4.0000** |          **16.0000** |    **1016 B** |        **1.00** |
| AtomicReservation         | 4       |  7.295 ms | 0.3937 ms | 1.1609 ms |  0.52 |    0.10 |               4.0000 |                - |    1016 B |        1.00 |
|                           |         |           |           |           |       |         |                      |                  |           |             |
| **LockSerializedReservation** | **16**      | **15.299 ms** | **0.5317 ms** | **1.5678 ms** |  **1.01** |    **0.14** |              **16.0000** |          **51.0000** |    **3224 B** |        **1.00** |
| AtomicReservation         | 16      | 12.582 ms | 0.4216 ms | 1.2430 ms |  0.83 |    0.12 |              16.0000 |                - |    3224 B |        1.00 |
|                           |         |           |           |           |       |         |                      |                  |           |             |
| **LockSerializedReservation** | **64**      | **21.434 ms** | **0.4244 ms** | **1.0003 ms** |  **1.00** |    **0.07** |              **64.0000** |          **65.0000** |   **12056 B** |        **1.00** |
| AtomicReservation         | 64      | 13.276 ms | 0.3337 ms | 0.9735 ms |  0.62 |    0.05 |              64.0000 |                - |   12056 B |        1.00 |
