using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace ATProtoNet.Streaming;

// One whole WebSocket message.
//
// Data: The message bytes. They live in the socket's receive buffer and are valid only until the next
// message is read; parse or copy them before moving on.
//
// IsBinary: Whether the message was a binary frame rather than text.
internal readonly record struct StreamSocketMessage(ReadOnlyMemory<byte> Data, bool IsBinary);

// How a connection is opened, and the largest message it accepts.
//
// SubProtocol: The subprotocol to request, if any.
//
// Authorization: The Authorization header value, if any.
//
// Invoker: The HTTP stack to upgrade through, such as one that enforces an address policy; by default
// the socket's own.
//
// MaxMessageBytes: The largest message accepted; by default StreamSocket.MaxMessageBytes.
internal readonly record struct StreamSocketOptions(
    string? SubProtocol = null, string? Authorization = null, HttpMessageInvoker? Invoker = null, int? MaxMessageBytes = null);

// Opens a connection and reads its messages until the server closes it. The seam the stream consumers
// take, so tests can script connections.
internal delegate IAsyncEnumerable<StreamSocketMessage> StreamConnector(
    Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken);

// A connection the client also writes to: the Tap channel acknowledges events over it.
internal interface IDuplexStreamSocket : IAsyncDisposable
{
    // Reads the next whole message, or returns null once the server has closed the connection. The
    // returned bytes are valid until the next call.
    ValueTask<StreamSocketMessage?> ReceiveAsync(CancellationToken cancellationToken);

    // Sends one text message. At most one send may run at a time.
    ValueTask SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken);
}

// Opens a connection the client can write to. The seam the Tap channel takes, so tests can script it.
internal delegate ValueTask<IDuplexStreamSocket> DuplexStreamConnector(
    Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken);

// The one WebSocket client behind every event stream: connects, reassembles fragmented messages into a
// reused buffer, and closes the socket when the reader is done with it.
internal sealed class StreamSocket(ClientWebSocket socket, int maxMessageBytes) : IDuplexStreamSocket
{
    // The largest message accepted. A firehose commit carries at most 2 MB of blocks, so this is generous;
    // it only stops a misbehaving server from growing the buffer without bound.
    internal const int MaxMessageBytes = 64 * 1024 * 1024;

    private const int InitialBufferBytes = 64 * 1024;

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private byte[] _buffer = new byte[InitialBufferBytes];

    // The default StreamConnector: a real WebSocket connection.
    public static StreamConnector Connector { get; } = ReadAllAsync;

    // The default DuplexStreamConnector: a real WebSocket connection.
    public static DuplexStreamConnector DuplexConnector { get; } =
        async (endpoint, options, cancellationToken) => await ConnectAsync(endpoint, options, cancellationToken).ConfigureAwait(false);

    // Connects to endpoint.
    //
    // Throws EventStreamException: The server refused the upgrade; carries the HTTP status when the
    // transport reported one.
    public static async Task<StreamSocket> ConnectAsync(
        Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("User-Agent", Http.AtProtoHttp.DefaultUserAgent);
        if (options.SubProtocol is { } subProtocol)
            socket.Options.AddSubProtocol(subProtocol);
        if (options.Authorization is { } authorization)
            socket.Options.SetRequestHeader("Authorization", authorization);

        try
        {
            await socket.ConnectAsync(endpoint, options.Invoker, cancellationToken).ConfigureAwait(false);
            return new StreamSocket(socket, options.MaxMessageBytes ?? MaxMessageBytes);
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

    // Reads the next whole message, or returns null once the server has closed the connection. The
    // returned bytes are valid until the next call.
    //
    // Throws EventStreamException: The server closed the connection abnormally and gave a reason, which is
    // the EventStreamException.Error; or a message was too large.
    public async ValueTask<StreamSocketMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var length = 0;
            while (true)
            {
                if (length == _buffer.Length)
                {
                    if (length >= maxMessageBytes)
                        throw new EventStreamException($"A message exceeded {maxMessageBytes} bytes.");
                    Array.Resize(ref _buffer, Math.Min(maxMessageBytes, length * 2));
                }

                var result = await socket.ReceiveAsync(_buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // A reason is how a service without error frames (the PLC export) says why.
                    if (socket.CloseStatus is not WebSocketCloseStatus.NormalClosure && socket.CloseStatusDescription is { Length: > 0 } reason)
                        throw new EventStreamException($"The server closed the connection: {reason}.", error: reason);
                    return null;
                }

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

    // Sends one text message. At most one send may run at a time.
    public ValueTask SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken) =>
        socket.SendAsync(utf8, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    // Closes the connection, if it is open, and releases the socket.
    public async ValueTask DisposeAsync()
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            // Send our close frame without waiting for the server's: the socket is disposed next.
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Already gone; nothing to close.
            }
        }

        socket.Dispose();
    }

    // Connects, then yields every message until the server closes the connection.
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

    // The endpoint for a log or exception message, without its query (cursor, filters).
    private static string Redact(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Path);
}
