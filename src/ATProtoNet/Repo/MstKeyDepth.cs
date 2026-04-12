using System.Security.Cryptography;

namespace ATProtoNet.Repo;

/// <summary>
/// Computes the depth (layer) of a key in the Merkle Search Tree.
/// <para>
/// The depth is determined by SHA-256 hashing the key and counting leading
/// zeros in 2-bit chunks (fanout of 4), as specified in the AT Protocol
/// repository specification.
/// </para>
/// </summary>
public static class MstKeyDepth
{
    /// <summary>
    /// Computes the MST depth for a key (byte array).
    /// </summary>
    /// <param name="key">The key bytes (typically UTF-8 encoded repo path).</param>
    /// <returns>The depth (number of leading zero 2-bit pairs in the SHA-256 hash).</returns>
    public static int ComputeDepth(ReadOnlySpan<byte> key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(key, hash);

        var depth = 0;
        foreach (var b in hash)
        {
            // Check each 2-bit chunk from the most significant bits
            if ((b & 0xC0) != 0) return depth; // bits 7-6
            depth++;
            if ((b & 0x30) != 0) return depth; // bits 5-4
            depth++;
            if ((b & 0x0C) != 0) return depth; // bits 3-2
            depth++;
            if ((b & 0x03) != 0) return depth; // bits 1-0
            depth++;
        }

        return depth; // All zeros (extremely unlikely)
    }

    /// <summary>
    /// Computes the MST depth for a string key (UTF-8 encoded).
    /// </summary>
    /// <param name="key">The key string (e.g., "app.bsky.feed.post/abc123").</param>
    /// <returns>The depth.</returns>
    public static int ComputeDepth(string key)
    {
        return ComputeDepth(System.Text.Encoding.UTF8.GetBytes(key));
    }
}
