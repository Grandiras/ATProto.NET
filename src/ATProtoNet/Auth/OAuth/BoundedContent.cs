namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Reads response bodies whose size is chosen by whoever answers: documents fetched from a URL
/// that came from untrusted input.
/// </summary>
internal static class BoundedContent
{
    /// <summary>
    /// Reads a response body into memory, or returns <see langword="null"/> once it is known to
    /// exceed <paramref name="maxBytes"/>. At most <paramref name="maxBytes"/> + 1 bytes are read.
    /// </summary>
    /// <param name="content">The response content.</param>
    /// <param name="maxBytes">The largest body accepted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// A declared <c>Content-Length</c> over the ceiling is refused before anything is read. The
    /// read enforces the ceiling as well, because a chunked body declares no length and a
    /// declared one can be a lie.
    /// </remarks>
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

        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        while (true)
        {
            if (read == buffer.Length)
            {
                if (read > maxBytes)
                    return null;

                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, maxBytes + 1L));
            }

            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0)
                return buffer.AsMemory(0, read);

            read += n;
        }
    }
}
