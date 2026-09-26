using System.Net.WebSockets;
using ATProtoNet.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// <see cref="StreamSocket"/> against a real WebSocket server on the loopback interface, since the
/// point of the helper is how it drives <see cref="ClientWebSocket"/>.
/// </summary>
public class StreamSocketTests
{
    private static async Task<(WebApplication App, Uri Endpoint)> ServeAsync(Func<HttpContext, Task> handle)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(context => handle(context));
        await app.StartAsync(TestContext.Current.CancellationToken);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new Uri(address.Replace("http://", "ws://") + "/xrpc/stream?cursor=1"));
    }

    [Fact]
    public async Task ReceiveAsync_ReassemblesFragmentsBeyondTheBufferAndSkipsEmptyMessages()
    {
        var large = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        var (app, endpoint) = await ServeAsync(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await socket.SendAsync(large.AsMemory(0, 70_000), WebSocketMessageType.Binary, endOfMessage: false, default);
            await socket.SendAsync(large.AsMemory(70_000, 70_000), WebSocketMessageType.Binary, endOfMessage: false, default);
            await socket.SendAsync(large.AsMemory(140_000), WebSocketMessageType.Binary, endOfMessage: true, default);
            await socket.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, endOfMessage: true, default);
            await socket.SendAsync("{}"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, default);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
        });
        await using var _ = app;

        var messages = new List<(byte[] Data, bool IsBinary)>();
        await foreach (var message in StreamSocket.Connector(endpoint, default, TestContext.Current.CancellationToken))
            messages.Add((message.Data.ToArray(), message.IsBinary));

        Assert.Equal(2, messages.Count);
        Assert.Equal(large, messages[0].Data);
        Assert.True(messages[0].IsBinary);
        Assert.Equal("{}"u8.ToArray(), messages[1].Data);
        Assert.False(messages[1].IsBinary);
    }

    [Fact]
    public async Task ConnectAsync_SendsTheSubprotocolAndAuthorization()
    {
        string? authorization = null;
        string? protocol = null;
        var (app, endpoint) = await ServeAsync(async context =>
        {
            authorization = context.Request.Headers.Authorization;
            protocol = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
            using var socket = await context.WebSockets.AcceptWebSocketAsync(protocol);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
        });
        await using var _ = app;

        await using var socket = await StreamSocket.ConnectAsync(
            endpoint, new StreamSocketOptions("xrpc.v1.json", "Bearer token"), TestContext.Current.CancellationToken);

        Assert.Null(await socket.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Bearer token", authorization);
        Assert.Equal("xrpc.v1.json", protocol);
    }

    [Fact]
    public async Task ConnectAsync_RefusedUpgrade_ThrowsWithTheStatus()
    {
        var (app, endpoint) = await ServeAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsync("""{"error":"CursorTooOld"}""");
        });
        await using var _ = app;

        var ex = await Assert.ThrowsAsync<EventStreamException>(
            () => StreamSocket.ConnectAsync(endpoint, default, TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
        // The endpoint in the message leaves out the query.
        Assert.DoesNotContain("cursor=", ex.Message);
    }

    [Fact]
    public async Task DisposeAsync_ClosesAnOpenConnection()
    {
        var closed = new TaskCompletionSource<WebSocketCloseStatus?>();
        var (app, endpoint) = await ServeAsync(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var result = await socket.ReceiveAsync(new byte[16], default);
            closed.TrySetResult(result.MessageType == WebSocketMessageType.Close ? result.CloseStatus : null);
        });
        await using var _ = app;

        var socket = await StreamSocket.ConnectAsync(endpoint, default, TestContext.Current.CancellationToken);
        await socket.DisposeAsync();

        Assert.Equal(WebSocketCloseStatus.NormalClosure, await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}
