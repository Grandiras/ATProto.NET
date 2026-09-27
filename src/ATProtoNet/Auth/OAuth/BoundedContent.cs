namespace ATProtoNet.Auth.OAuth;

// Reads response bodies whose size is chosen by whoever answers: documents fetched from a URL that
// came from untrusted input.
internal static class BoundedContent
{
    // Reads a response body into memory, or returns null once it is known to exceed maxBytes. At most
    // maxBytes + 1 bytes are read.
    //
    // A declared Content-Length over the ceiling is refused before anything is read. The read enforces the
    // ceiling as well, because a chunked body declares no length and a declared one can be a lie.
    //
    // content: The response content.
    //
    // maxBytes: The largest body accepted.
    public static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(
        this HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxBytes, Array.MaxLength);

        var declared = content.Headers.ContentLength;
        if (declared > maxBytes)
            return null;

        // Sized from the declared length, so a truthful response fills one buffer. The byte of
        // headroom is how an overlong body shows itself: filling it means the body is over.
        var buffer = new byte[Math.Min((declared ?? 4096) + 1, maxBytes + 1L)];
        var read = 0;

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            if (read == buffer.Length)
            {
                if (read > maxBytes)
                    return null;

                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, maxBytes + 1L));
            }

            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                return buffer.AsMemory(0, read);

            read += n;
        }
    }
}
