using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace ATProtoNet.Streaming;

/// <summary>One whole WebSocket message.</summary>
/// <param name="Data">
/// The message bytes. They live in the socket's receive buffer and are valid only until the next
/// message is read; parse or copy them before moving on.
/// </param>
/// <param name="IsBinary">Whether the message was a binary frame rather than text.</param>
internal readonly record struct StreamSocketMessage(ReadOnlyMemory<byte> Data, bool IsBinary);

/// <summary>What a connection sends with its WebSocket upgrade.</summary>
/// <param name="SubProtocol">The subprotocol to request, if any.</param>
/// <param name="Authorization">The <c>Authorization</c> header value, if any.</param>
internal readonly record struct StreamSocketOptions(string? SubProtocol = null, string? Authorization = null);

/// <summary>
/// Opens a connection and reads its messages until the server closes it. The seam the stream
/// consumers take, so tests can script connections.
/// </summary>
internal delegate IAsyncEnumerable<StreamSocketMessage> StreamConnector(
    Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken);

/// <summary>
/// The one WebSocket client behind every event stream: connects, reassembles fragmented messages
/// into a reused buffer, and closes the socket when the reader is done with it.
/// </summary>
internal sealed class StreamSocket : IAsyncDisposable
{
    /// <summary>
    /// The largest message accepted. A firehose commit carries at most 2 MB of blocks, so this is
    /// generous; it only stops a misbehaving server from growing the buffer without bound.
    /// </summary>
    internal const int MaxMessageBytes = 64 * 1024 * 1024;

    private const int InitialBufferBytes = 64 * 1024;

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly ClientWebSocket _socket;
    private byte[] _buffer = new byte[InitialBufferBytes];

    private StreamSocket(ClientWebSocket socket) => _socket = socket;

    /// <summary>The default <see cref="StreamConnector"/>: a real WebSocket connection.</summary>
    public static StreamConnector Connector { get; } = ReadAllAsync;

    /// <summary>
    /// Connects to <paramref name="endpoint"/>.
    /// </summary>
    /// <exception cref="EventStreamException">The server refused the upgrade; carries the HTTP status
    /// when the transport reported one.</exception>
    public static async Task<StreamSocket> ConnectAsync(
        Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        if (options.SubProtocol is { } subProtocol)
            socket.Options.AddSubProtocol(subProtocol);
        if (options.Authorization is { } authorization)
            socket.Options.SetRequestHeader("Authorization", authorization);

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new StreamSocket(socket);
        }
        catch (WebSocketException ex)
        {
            int? status = socket.HttpStatusCode != 0 ? (int)socket.HttpStatusCode : null;
            socket.Dispose();
            throw new EventStreamException(
                $"Could not connect to {Redact(endpoint)}" + (status is null ? $": {ex.Message}" : $": HTTP {status}."),
                statusCode: status,
                innerException: ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the next whole message, or returns <see langword="null"/> once the server has closed
    /// the connection. The returned bytes are valid until the next call.
    /// </summary>
    public async ValueTask<StreamSocketMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var length = 0;
            while (true)
            {
                if (length == _buffer.Length)
                {
                    if (length >= MaxMessageBytes)
                        throw new EventStreamException($"A message exceeded {MaxMessageBytes} bytes.");
                    Array.Resize(ref _buffer, Math.Min(MaxMessageBytes, length * 2));
                }

                var result = await _socket.ReceiveAsync(_buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                length += result.Count;
                if (!result.EndOfMessage)
                    continue;

                // An empty message carries nothing to parse.
                if (length == 0)
                    break;

                return new StreamSocketMessage(
                    _buffer.AsMemory(0, length), result.MessageType == WebSocketMessageType.Binary);
            }
        }
    }

    /// <summary>Closes the connection, if it is open, and releases the socket.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            // Send our close frame without waiting for the server's: the socket is disposed next.
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Already gone; nothing to close.
            }
        }

        _socket.Dispose();
    }

    /// <summary>Connects, then yields every message until the server closes the connection.</summary>
    private static async IAsyncEnumerable<StreamSocketMessage> ReadAllAsync(
        Uri endpoint, StreamSocketOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var socket = await ConnectAsync(endpoint, options, cancellationToken).ConfigureAwait(false);
        await using (socket.ConfigureAwait(false))
        {
            while (await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } message)
                yield return message;
        }
    }

    /// <summary>The endpoint for a log or exception message, without its query (cursor, filters).</summary>
    private static string Redact(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Path);
}
