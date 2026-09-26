using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using System.Web;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// Builds AT Protocol event-stream frames: a DAG-CBOR header <c>{op, t}</c> followed by a body.
/// </summary>
internal static class EventStreamFrames
{
    public const string RepoDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    public const string CommitCid = "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta";
    public const string RecordCid = "bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry";

    public static byte[] Message(string type, Action<CborWriter> body) => Frame(1, type, body);

    public static byte[] Error(string error, string? message = null) => Frame(-1, null, writer =>
    {
        writer.WriteStartMap(message is null ? 1 : 2);
        writer.WriteTextString("error");
        writer.WriteTextString(error);
        if (message is not null)
        {
            writer.WriteTextString("message");
            writer.WriteTextString(message);
        }

        writer.WriteEndMap();
    });

    /// <summary>A <c>#commit</c> with one create per path.</summary>
    public static byte[] Commit(long seq, string repo = RepoDid, byte[]? blocks = null, params string[] paths)
        => Message("#commit", writer =>
        {
            writer.WriteStartMap(7);
            writer.WriteTextString("ops");
            writer.WriteStartArray(paths.Length);
            foreach (var path in paths)
            {
                writer.WriteStartMap(3);
                writer.WriteTextString("cid");
                writer.WriteTextString(RecordCid);
                writer.WriteTextString("path");
                writer.WriteTextString(path);
                writer.WriteTextString("action");
                writer.WriteTextString("create");
                writer.WriteEndMap();
            }

            writer.WriteEndArray();
            writer.WriteTextString("rev");
            writer.WriteTextString("3jzfcijpj2z2a");
            writer.WriteTextString("seq");
            writer.WriteInt64(seq);
            writer.WriteTextString("repo");
            writer.WriteTextString(repo);
            writer.WriteTextString("time");
            writer.WriteTextString("2024-01-15T12:00:00.000Z");
            writer.WriteTextString("blocks");
            writer.WriteByteString(blocks ?? []);
            writer.WriteTextString("commit");
            writer.WriteTextString(CommitCid);
            writer.WriteEndMap();
        });

    public static byte[] IdentityFrame(long seq, string did = RepoDid) => Message("#identity", writer =>
    {
        writer.WriteStartMap(3);
        writer.WriteTextString("did");
        writer.WriteTextString(did);
        writer.WriteTextString("seq");
        writer.WriteInt64(seq);
        writer.WriteTextString("time");
        writer.WriteTextString("2024-01-15T12:00:00.000Z");
        writer.WriteEndMap();
    });

    public static byte[] Info(string name) => Message("#info", writer =>
    {
        writer.WriteStartMap(1);
        writer.WriteTextString("name");
        writer.WriteTextString(name);
        writer.WriteEndMap();
    });

    /// <summary>A message of a type no SDK version models yet, carrying only a <c>seq</c>.</summary>
    public static byte[] Unknown(string type, long seq) => Message(type, writer =>
    {
        writer.WriteStartMap(1);
        writer.WriteTextString("seq");
        writer.WriteInt64(seq);
        writer.WriteEndMap();
    });

    private static byte[] Frame(int op, string? type, Action<CborWriter> body)
    {
        var header = new CborWriter(CborConformanceMode.Lax);
        header.WriteStartMap(type is null ? 1 : 2);
        header.WriteTextString("op");
        header.WriteInt32(op);
        if (type is not null)
        {
            header.WriteTextString("t");
            header.WriteTextString(type);
        }

        header.WriteEndMap();

        var writer = new CborWriter(CborConformanceMode.Lax);
        body(writer);
        return [.. header.Encode(), .. writer.Encode()];
    }
}

/// <summary>
/// A scripted <see cref="StreamConnector"/>: each connection replays the next scripted list of
/// frames (or fails), and every endpoint asked for is recorded.
/// </summary>
internal sealed class ScriptedConnector
{
    private readonly Queue<Func<IAsyncEnumerable<StreamSocketMessage>>> _connections = new();

    public List<Uri> Endpoints { get; } = [];

    public List<StreamSocketOptions> Options { get; } = [];

    /// <summary>The <c>cursor</c> query parameter of each connection, or null when there was none.</summary>
    public IEnumerable<string?> Cursors => Endpoints.Select(e => HttpUtility.ParseQueryString(e.Query)["cursor"]);

    public ScriptedConnector Connection(params byte[][] frames)
    {
        _connections.Enqueue(() => Yield(frames));
        return this;
    }

    public ScriptedConnector Failing(Exception exception)
    {
        _connections.Enqueue(() => Throw(exception));
        return this;
    }

    public IAsyncEnumerable<StreamSocketMessage> Connect(Uri endpoint, StreamSocketOptions options, CancellationToken ct)
    {
        Endpoints.Add(endpoint);
        Options.Add(options);
        return _connections.Count > 0 ? _connections.Dequeue()() : Yield([]);
    }

    private static async IAsyncEnumerable<StreamSocketMessage> Yield(byte[][] frames)
    {
        foreach (var frame in frames)
        {
            await Task.Yield();
            yield return new StreamSocketMessage(frame, IsBinary: true);
        }
    }

    private static async IAsyncEnumerable<StreamSocketMessage> Throw(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // Unreachable, but required to make this an iterator.
        yield break;
#pragma warning restore CS0162
    }
}

internal static class StreamTestExtensions
{
    /// <summary>A policy that retries <paramref name="maxAttempts"/> times without waiting.</summary>
    public static StreamReconnectPolicy Immediate(int? maxAttempts) => new()
    {
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        MaxAttempts = maxAttempts,
    };

    /// <summary>
    /// Collects every message until the scripted connections run out and the reconnect policy
    /// gives up, which ends the enumeration with a plain <see cref="EventStreamException"/>.
    /// </summary>
    public static async Task<List<T>> DrainAsync<T>(this IAsyncEnumerable<T> stream)
    {
        var messages = new List<T>();
        try
        {
            await foreach (var message in stream)
                messages.Add(message);
        }
        catch (EventStreamException ex) when (ex.GetType() == typeof(EventStreamException) && ex.Error is null && ex.InnerException is null)
        {
        }

        return messages;
    }
}
