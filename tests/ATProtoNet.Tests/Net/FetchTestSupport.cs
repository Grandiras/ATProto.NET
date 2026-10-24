using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Net;

/// <summary>A raw HTTP/1.1 server on a loopback port answering every connection with one canned response, for tests of the real socket handler.</summary>
internal sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;

    /// <param name="respond">The raw response, given the server's port.</param>
    public LoopbackServer(Func<int, string> respond)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync(Encoding.Latin1.GetBytes(respond(Port)));
    }

    public int Port { get; }

    /// <summary>How many connections were accepted.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>A complete response with a correct <c>Content-Length</c>.</summary>
    public static string Response(string status, string headers, string body) =>
        $"HTTP/1.1 {status}\r\n{headers}Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync(byte[] response)
    {
        while (true)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return;
            }
            Interlocked.Increment(ref _connections);
            _ = ServeAsync(socket, response);
        }
    }

    private static async Task ServeAsync(Socket socket, byte[] response)
    {
        using (socket)
            try
            {
                var buffer = new byte[8192];
                var read = 0;
                while (buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) < 0)
                {
                    var n = await socket.ReceiveAsync(buffer.AsMemory(read));
                    if (n == 0) return;
                    read += n;
                }
                await socket.SendAsync(response);
                socket.Shutdown(SocketShutdown.Send);
                // Drained until the client closes, so closing never resets a response it has not read.
                while (await socket.ReceiveAsync(buffer) > 0) { }
            }
            catch (SocketException) { }
    }
}

/// <summary>A body that fails the way a connection closed mid-response does.</summary>
internal sealed class BrokenStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw Broken();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw Broken();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static HttpIOException Broken() => new(HttpRequestError.ResponseEnded, "The response ended prematurely.");
}

/// <summary>An endless body that counts the bytes read from it, or one that never sends a byte until cancelled.</summary>
internal sealed class EndlessStream(bool hang = false) : Stream
{
    private long _read;

    public long BytesRead => Interlocked.Read(ref _read);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (hang) await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
        buffer.Span.Fill((byte)'x');
        Interlocked.Add(ref _read, buffer.Length);
        return buffer.Length;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A logger that keeps what is logged.</summary>
internal sealed class ListLogger : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate) _entries.Add((logLevel, formatter(state, exception)));
    }
}

internal static class Bodies
{
    /// <summary>A 200 (or <paramref name="status"/>) response over <paramref name="stream"/>, with or without a declared length.</summary>
    public static HttpResponseMessage Streamed(Stream stream, long? length, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentLength = length;
        return new(status) { Content = content };
    }

    /// <summary>A response of <paramref name="bytes"/>, with or without a declared length.</summary>
    public static HttpResponseMessage Of(byte[] bytes, bool declareLength, HttpStatusCode status = HttpStatusCode.OK) =>
        Streamed(new MemoryStream(bytes), declareLength ? bytes.Length : null, status);

    /// <summary>A redirect to <paramref name="location"/>.</summary>
    public static HttpResponseMessage Redirect(string? location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
        if (location is not null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>A response a caller's handler produced after following a redirect to <paramref name="finalUrl"/> itself.</summary>
    public static HttpResponseMessage FollowedTo(string finalUrl, string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body), RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl) };

    /// <summary>A task that completes only when <paramref name="cancellationToken"/> is cancelled.</summary>
    public static Task<T> Hang<T>(CancellationToken cancellationToken)
    {
        var never = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => never.TrySetCanceled(cancellationToken));
        return never.Task;
    }
}
