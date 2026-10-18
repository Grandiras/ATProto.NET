```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method            | Mean        | Error    | StdDev   | Gen0    | Gen1    | Allocated |
|------------------ |------------:|---------:|---------:|--------:|--------:|----------:|
| Read              |    89.01 μs | 0.361 μs | 0.320 μs | 79.8340 | 64.4531 |   1.27 MB |
| ReadVerifyingCids | 1,076.27 μs | 3.452 μs | 2.882 μs | 78.1250 | 60.5469 |   1.27 MB |
