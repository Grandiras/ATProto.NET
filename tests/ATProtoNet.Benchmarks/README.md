# ATProtoNet.Benchmarks

BenchmarkDotNet scenarios for what the 0.7/0.8 performance passes optimised, and the 0.8
baseline that the phase gates compare against (Goal G8, `design/04-testing.md`).

```
tests/ATProtoNet.Benchmarks/
├── Shared/      entry point, job, deterministic inputs (no SDK types), shared by every project
├── Legacy/      the scenarios over the published ATProtoNet 0.8.0 package
└── baseline/0.8.0/   the recorded results
```

Both SDKs are named `ATProtoNet` and cannot load into one process, so each implementation is
its own project. `Legacy` runs every scenario; `Next` (`ATProtoNet.Benchmarks.Next`) imports the same
`Shared/Benchmarks.Shared.props` and implements the scenarios of each phase as it lands:

- **Phase 1:** `DrislBenchmarks` and `CarBenchmarks`. `Encode` starts from the JSON text
  (`DrislJson.ToDrisl`), where 0.8 started from a parsed `JsonElement`; `Decode` writes the JSON into
  a reused `Utf8JsonWriter` (`DrislJson.WriteJson`), where 0.8 returned a `JsonElement`. The CAR's MST
  nodes come from `Next/MstNodes.cs`, a stand-in built to the repository spec until phase 2's MST
  replaces it.

## Running

```bash
export PATH=$HOME/.dotnet-11:$PATH DOTNET_ROOT=$HOME/.dotnet-11   # the pinned SDK
dotnet run -c Release --project tests/ATProtoNet.Benchmarks/Legacy -- --filter '*'
dotnet run -c Release --project tests/ATProtoNet.Benchmarks/Legacy -- --filter '*Mst*'   # a subset
```

A full run takes about four minutes. Results (markdown, CSV, HTML, brief JSON) land in
`BenchmarkDotNet.Artifacts/results`; `--artifacts <dir>` moves them. The job is fixed in
`Shared/Entry.cs` (5 warm-ups, 15 iterations, one launch) so runs compare like for like; it runs
in process (`InProcessEmitToolchain`) because BenchmarkDotNet 0.15 does not recognise the
`net11.0` moniker and cannot build its per-benchmark project. Revisit when a BenchmarkDotNet
release supports it. Do not pass `--job`: it replaces the shared job.

## Scenarios

A scenario is identified by `<Class>.<Method>`, in the namespace `ATProtoNet.Benchmarks`. An
implementation provides the same classes and methods over its own API; `Corpus` and
`FirehoseFrames` in `Shared/` supply the same bytes to all of them, built in `GlobalSetup`. Each
setup checks that the input really exercises the path (nothing dropped, caches hit).

| Scenario | Measures | Per operation |
|---|---|---|
| `DrislBenchmarks.Encode` | JSON data model to DRISL/DAG-CBOR, 64 post records with blobs and links | the document |
| `DrislBenchmarks.Decode` | the same bytes back to JSON | the document |
| `CarBenchmarks.Read` | CAR parse of a 2,000-record repository (MST nodes and records) | the CAR |
| `CarBenchmarks.ReadVerifyingCids` | the same, recomputing every block's CID | the CAR |
| `MstBenchmarks.Build` | MST from 2,000 entries and its root CID | the build |
| `MstBenchmarks.UpdateAndRecomputeRoot` | 10 updates to a built tree, then the root CID | the 10 updates |
| `FirehoseBenchmarks.Decode` | parse the six captured `subscribeRepos` frames | one frame |
| `FirehoseBenchmarks.DecodeAndVerify` | parse and verify each frame (signature, inductive proof, chain) against an in-memory resolver | one frame |
| `JetstreamBenchmarks.Decode` | 200 mixed v1 frames (commits, identity, account) | one frame |
| `JsonModelBenchmarks.DeserializeTimeline` | a 100-post `getTimeline` response into the feed model | the response |
| `XrpcBenchmarks.QuerySmall` | `getProfile` through the client and an in-process `HttpMessageHandler` | the request |
| `XrpcBenchmarks.QueryLarge` | `getTimeline` (100 posts) the same way | the request |
| `IdentityBenchmarks.ResolveHandle` | handle to verified identity, DID document and handle cached | the resolution |
| `IdentityBenchmarks.ResolveDid` | DID to verified identity, both cached | the resolution |

What 0.8 could not be measured on:

- **MST diff.** 0.8 has no public diff, so `UpdateAndRecomputeRoot` stands in for it. `Next`
  keeps this scenario and adds `MstBenchmarks.Diff` (two trees differing in 10 entries).
- **Jetstream v2.** Only the v1 JSON frames are decoded.
- **Real networks.** Every scenario is in process; no sockets, DNS or clock waits.

## Baseline and the gates

`baseline/0.8.0/` holds, per class, the BenchmarkDotNet markdown (`*-report-github.md`) and brief
JSON (`*-report-brief.json`, with the host environment), `summary.md` (one table, mean and
allocation per operation) and `machine.md` (machine, job, command).

A phase gate that says "benchmark at or better than baseline" means: run the `Next` project's
scenarios of that area with the same command, and for each scenario the mean and the allocated
bytes per operation are no higher than the baseline's (join on `Type` and `Method` in the brief
JSON). Numbers depend on the machine, the CPU governor and the runtime, so compare on the same
machine, or re-run `Legacy` there first and compare against that run; repeat runs on this
machine agree within 5%, which is the noise floor. A scenario a phase has not implemented yet
is not part of its gate. Replacing the baseline (a new machine of record, a new BenchmarkDotNet)
is a deliberate commit that says so.
