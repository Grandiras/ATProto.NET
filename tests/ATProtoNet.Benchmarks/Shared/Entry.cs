using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace ATProtoNet.Benchmarks;

internal static class Entry
{
    /// <summary>
    /// The job every recorded result uses, so runs of any implementation compare like for like.
    /// It runs in process: BenchmarkDotNet 0.15 does not know the net11.0 moniker, so it cannot
    /// build the per-benchmark project the default toolchain generates.
    /// </summary>
    public static IConfig Config { get; } = DefaultConfig.Instance
        .AddJob(Job.Default.WithId("Baseline").WithToolchain(InProcessEmitToolchain.Instance)
            .WithLaunchCount(1).WithWarmupCount(5).WithIterationCount(15))
        .AddExporter(JsonExporter.Brief);

    public static int Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Entry).Assembly).Run(args, Config);
        return 0;
    }
}
