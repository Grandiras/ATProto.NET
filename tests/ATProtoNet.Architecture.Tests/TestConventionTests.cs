using System.Text.RegularExpressions;

namespace ATProtoNet.Architecture.Tests;

// design/04-testing.md, rule 4: no real sleeps, no wall clock in unit tests. Integration tests
// and benchmarks run against real services and real time, so they are exempt.
public partial class TestConventionTests
{
    private static readonly string[] Exempt = ["ATProtoNet.IntegrationTests", "ATProtoNet.Benchmarks", "ATProtoNet.Architecture.Tests"];

    [GeneratedRegex(@"\b(Task\.Delay|Thread\.Sleep|Stopwatch|DateTime(Offset)?\.(Utc)?Now)\b")]
    private static partial Regex Banned();

    [Fact]
    public void TestSources_NoSleepsStopwatchesOrWallClock()
    {
        var tests = Repository.Root.GetDirectories("tests").Single();
        var offenders = tests.GetDirectories().Where(d => !Exempt.Any(d.Name.StartsWith))
            .SelectMany(Repository.SourceFiles)
            .SelectMany(f => File.ReadLines(f.FullName).Select((line, i) => (f, line, i)))
            .Where(x => Banned().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(Repository.Root.FullName, x.f.FullName)}:{x.i + 1}: {x.line.Trim()}");
        Assert.Empty(offenders);
    }
}
