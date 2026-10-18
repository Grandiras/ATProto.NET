```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method | Mean     | Error   | StdDev  | Gen0   | Allocated |
|------- |---------:|--------:|--------:|-------:|----------:|
| Decode | 909.0 ns | 3.29 ns | 3.08 ns | 0.0757 |   1.25 KB |
