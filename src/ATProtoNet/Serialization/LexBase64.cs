namespace ATProtoNet.Serialization;

/// <summary>
/// Base64 as the AT Protocol data model uses it for <c>bytes</c>: the standard alphabet, and
/// padding optional on read.
/// </summary>
internal static class LexBase64
{
    /// <summary>
    /// Decodes standard base64 with or without trailing <c>=</c> padding. The data model specifies
    /// unpadded output, which <see cref="Convert.FromBase64String"/> rejects on its own.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="base64"/> is not valid base64.</exception>
    public static byte[] Decode(string base64)
    {
        ArgumentNullException.ThrowIfNull(base64);

        return (base64.Length % 4) switch
        {
            2 => Convert.FromBase64String(base64 + "=="),
            3 => Convert.FromBase64String(base64 + "="),
            _ => Convert.FromBase64String(base64),
        };
    }
}
