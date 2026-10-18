```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method                 | Mean       | Error    | StdDev   | Gen0    | Gen1    | Allocated |
|----------------------- |-----------:|---------:|---------:|--------:|--------:|----------:|
| Build                  | 1,487.9 μs |  8.29 μs |  7.76 μs | 74.2188 | 31.2500 |   1.19 MB |
| UpdateAndRecomputeRoot |   579.0 μs | 19.71 μs | 16.46 μs | 66.4063 | 25.3906 |   1.07 MB |
