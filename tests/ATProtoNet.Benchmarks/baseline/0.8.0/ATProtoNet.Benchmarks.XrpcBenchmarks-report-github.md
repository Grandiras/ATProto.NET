```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method     | Mean       | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|----------- |-----------:|----------:|----------:|--------:|-------:|----------:|
| QuerySmall |   1.433 μs | 0.0101 μs | 0.0094 μs |  0.1984 |      - |   3.26 KB |
| QueryLarge | 290.604 μs | 1.5644 μs | 1.4634 μs | 18.0664 | 5.8594 | 299.81 KB |
