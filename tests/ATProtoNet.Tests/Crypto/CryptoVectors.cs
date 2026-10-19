using System.Text.Json.Nodes;

namespace ATProtoNet.Tests.Crypto;

// Reads the conformance suite's vector files in place, so unit tests use the same pinned data.
internal static class CryptoVectors
{
    private static readonly string Root = FindRoot(AppContext.BaseDirectory);

    public static JsonNode Json(string relativePath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "tests", "ATProtoNet.Conformance", "vectors", relativePath)))!;

    public static byte[] Base64(string unpadded) => Convert.FromBase64String(unpadded.PadRight(unpadded.Length + (4 - unpadded.Length % 4) % 4, '='));

    private static string FindRoot(string directory) =>
        File.Exists(Path.Combine(directory, "ATProto.NET.slnx")) ? directory
        : FindRoot(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("No ATProto.NET.slnx above the tests."));
}
