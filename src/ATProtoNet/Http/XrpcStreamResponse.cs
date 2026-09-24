namespace ATProtoNet.Http;

/// <summary>
/// A binary XRPC response — a blob or a repository CAR — read as a stream.
/// </summary>
/// <remarks>
/// The response owns the underlying HTTP response and its connection. Dispose it once the
/// <see cref="Content"/> has been read, or the connection stays checked out of the pool.
/// </remarks>
/// <example>
/// <code>
/// await using var blob = await client.Sync.GetBlobAsync(did, cid);
/// await using var file = File.Create(path);
/// await blob.Content.CopyToAsync(file);
/// Console.WriteLine($"{blob.ContentType}, {blob.ContentLength} bytes");
/// </code>
/// </example>
public sealed class XrpcStreamResponse : IAsyncDisposable, IDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    internal XrpcStreamResponse(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
        ContentType = response.Content.Headers.ContentType?.MediaType;
        ContentLength = response.Content.Headers.ContentLength;
    }

    /// <summary>The response body. Read it before disposing this response.</summary>
    public Stream Content { get; }

    /// <summary>The media type the service declared, such as <c>image/jpeg</c> or <c>application/vnd.ipld.car</c>.</summary>
    public string? ContentType { get; }

    /// <summary>The body length the service declared, when it sent one.</summary>
    public long? ContentLength { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Content.Dispose();
        _response.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await Content.DisposeAsync();
        _response.Dispose();
    }
}
