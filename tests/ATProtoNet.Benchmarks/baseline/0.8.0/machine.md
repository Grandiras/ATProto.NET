# Machine and run

```
BenchmarkDotNet v0.15.8, Linux Fedora Linux 44 (Workstation Edition)
AMD Ryzen 5 7600X 2.99GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 11.0.0 (11.0.0-rc.2.26504.105, 11.0.26.50505), X64 RyuJIT x86-64-v4

Job=Baseline  Toolchain=InProcessEmitToolchain  IterationCount=15
LaunchCount=1  WarmupCount=5
```

- Date: 2026-10-18, at commit `669a69e` plus the benchmark project.
- Package: `ATProtoNet` 0.8.0 from nuget.org (`tests/ATProtoNet.Benchmarks/Legacy`), Release.
- Command: `dotnet run -c Release --project tests/ATProtoNet.Benchmarks/Legacy -- --filter '*'`
- Wall time: about 4 minutes. Two full runs on this machine agree within 5% on every scenario.
