```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method | Mean     | Error   | StdDev  | Gen0    | Gen1   | Allocated |
|------- |---------:|--------:|--------:|--------:|-------:|----------:|
| Encode | 118.3 μs | 1.31 μs | 1.10 μs | 21.1182 | 1.9531 | 345.39 KB |
| Decode | 114.7 μs | 0.28 μs | 0.25 μs |  4.2725 | 0.1221 |  70.11 KB |
