```

BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 4.24GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=5  

```
| Method        | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------- |---------:|--------:|--------:|-------:|----------:|
| ResolveHandle | 161.5 ns | 4.36 ns | 4.08 ns | 0.0262 |     440 B |
| ResolveDid    | 157.7 ns | 0.65 ns | 0.51 ns | 0.0291 |     488 B |
