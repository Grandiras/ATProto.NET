# Baseline: ATProtoNet 0.8.0

Mean and standard deviation are per operation (one frame for the Jetstream and firehose scenarios), allocations in bytes per operation. The full tables are the `*-report-github.md` files; the raw statistics are the `*-report-brief.json` files.

| Scenario | Mean | StdDev | Allocated (B) |
|---|---:|---:|---:|
| CarBenchmarks.Read | 89.0 us | 320.4 ns | 1,335,752 |
| CarBenchmarks.ReadVerifyingCids | 1,076.3 us | 2.9 us | 1,335,759 |
| DrislBenchmarks.Encode | 118.3 us | 1.1 us | 353,681 |
| DrislBenchmarks.Decode | 114.7 us | 246.3 ns | 71,793 |
| FirehoseBenchmarks.Decode | 2.6 us | 23.0 ns | 7,129 |
| FirehoseBenchmarks.DecodeAndVerify | 243.1 us | 606.7 ns | 41,525 |
| IdentityBenchmarks.ResolveHandle | 161.5 ns | 4.1 ns | 440 |
| IdentityBenchmarks.ResolveDid | 157.7 ns | 0.5 ns | 488 |
| JetstreamBenchmarks.Decode | 909.0 ns | 3.1 ns | 1,275 |
| JsonModelBenchmarks.DeserializeTimeline | 226.0 us | 1.1 us | 304,754 |
| MstBenchmarks.Build | 1,487.9 us | 7.8 us | 1,245,476 |
| MstBenchmarks.UpdateAndRecomputeRoot | 579.0 us | 16.5 us | 1,121,006 |
| XrpcBenchmarks.QuerySmall | 1.4 us | 9.4 ns | 3,336 |
| XrpcBenchmarks.QueryLarge | 290.6 us | 1.5 us | 307,003 |
