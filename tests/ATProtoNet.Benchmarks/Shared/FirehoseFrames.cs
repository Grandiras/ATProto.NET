using System.Text.Json;

namespace ATProtoNet.Benchmarks;

/// <summary>The captured <c>subscribeRepos</c> frames of the conformance vectors, with each account's signing key and PDS.</summary>
public sealed record FirehoseFrames(
    IReadOnlyList<(string Name, byte[] Frame)> Frames,
    IReadOnlyDictionary<string, string> SigningKeys,
    IReadOnlyDictionary<string, string> Pds)
{
    public static FirehoseFrames Load()
    {
        using var stream = typeof(FirehoseFrames).Assembly.GetManifestResourceStream("firehose-sync-frames.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new(
            [.. root.GetProperty("frames").EnumerateArray()
                .Select(f => (f.GetProperty("name").GetString()!, Convert.FromBase64String(f.GetProperty("frame").GetString()!)))],
            Map(root.GetProperty("signingKeys")),
            Map(root.GetProperty("pds")));
    }

    private static Dictionary<string, string> Map(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
}
