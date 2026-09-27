namespace ATProtoNet.Http;

// Reads bodies whose size is chosen by whoever sends them — documents fetched from a URL that came from
// untrusted input, repository downloads, webhook deliveries — up to a ceiling.
//
// A declared length over the ceiling is refused before anything is read. The read enforces the ceiling
// as well, because a chunked body declares no length and a declared one can be a lie. A declared length
// sizes the first buffer only up to MaxInitialBytes: it is the sender's word, so a large claim must not
// reserve memory before the bytes arrive.
internal static class BoundedContent
{
    // The most a declared length pre-allocates.
    internal const int MaxInitialBytes = 1024 * 1024;

    // The first buffer of a body that declares no length.
    private const int UndeclaredInitialBytes = 16 * 1024;

    // Reads a response body into memory, or returns null once it is known to exceed maxBytes.
    public static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(
        this HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        var declared = content.Headers.ContentLength;
        if (declared > maxBytes)
            return null;

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await stream.ReadBoundedAsync(maxBytes, declared, cancellationToken).ConfigureAwait(false);
    }

    // Reads a stream to its end, or returns null once it is known to exceed maxBytes. At most maxBytes +
    // 1 bytes are read.
    //
    // declaredLength: The length the sender declared, if any.
    public static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(
        this Stream stream, long maxBytes, long? declaredLength, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (declaredLength > maxBytes)
            return null;

        // No array holds more than Array.MaxLength bytes, so a longer body is over any ceiling.
        var ceiling = Math.Min(maxBytes, Array.MaxLength - 1L);

        // The byte of headroom is how an overlong body shows itself: filling it means the body is over.
        var buffer = new byte[InitialCapacity(declaredLength, ceiling) + 1];
        var read = 0;
        while (true)
        {
            if (read == buffer.Length)
            {
                if (read > ceiling)
                    return null;

                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, ceiling + 1));
            }

            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                return buffer.AsMemory(0, read);

            read += n;
        }
    }

    // The first buffer for a body: its declared length, up to MaxInitialBytes and the ceiling.
    internal static int InitialCapacity(long? declaredLength, long maxBytes) =>
        (int)Math.Min(declaredLength ?? UndeclaredInitialBytes, Math.Min(maxBytes, MaxInitialBytes));
}
