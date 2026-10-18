```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method          | Mean       | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|---------------- |-----------:|----------:|----------:|-------:|-------:|----------:|
| Decode          |   2.638 μs | 0.0246 μs | 0.0230 μs | 0.4222 | 0.0051 |   6.96 KB |
| DecodeAndVerify | 243.116 μs | 0.7265 μs | 0.6067 μs | 2.2786 |      - |  40.55 KB |
