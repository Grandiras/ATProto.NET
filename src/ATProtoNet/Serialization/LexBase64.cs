namespace ATProtoNet.Serialization;

// Base64 as the AT Protocol data model uses it for bytes: the standard alphabet, and padding optional on
// read.
internal static class LexBase64
{
    // Decodes standard base64 with or without trailing = padding. The data model specifies unpadded
    // output, which Convert.FromBase64String rejects on its own.
    //
    // Throws FormatException: base64 is not valid base64.
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
