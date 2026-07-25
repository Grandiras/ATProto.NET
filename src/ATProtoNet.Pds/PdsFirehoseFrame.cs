using System.Formats.Cbor;
using ATProtoNet.Repo;

namespace ATProtoNet.Pds;

/// <summary>
/// A sequenced, fully-encoded firehose frame ready to be written to a
/// <c>com.atproto.sync.subscribeRepos</c> WebSocket.
/// </summary>
/// <param name="Seq">The sequence number assigned by <see cref="PdsSequencer"/>.</param>
/// <param name="Type">The event type discriminator, e.g. <c>#commit</c>.</param>
/// <param name="Time">When the event was produced.</param>
/// <param name="Frame">The concatenated CBOR header and body bytes.</param>
public sealed record PdsFirehoseEvent(long Seq, string Type, DateTimeOffset Time, byte[] Frame);

/// <summary>
/// A single repository operation carried in a firehose <c>#commit</c> event.
/// </summary>
/// <param name="Action">One of <c>create</c>, <c>update</c>, <c>delete</c>.</param>
/// <param name="Path">The record path, <c>collection/rkey</c>.</param>
/// <param name="Cid">Binary CID of the record after the operation; <c>null</c> for deletes.</param>
/// <param name="Prev">Binary CID of the record before the operation; <c>null</c> for creates.</param>
public sealed record PdsRepoOp(string Action, string Path, byte[]? Cid, byte[]? Prev)
{
    /// <summary>Creates a <c>create</c> operation.</summary>
    public static PdsRepoOp Create(string path, byte[] cid) => new("create", path, cid, null);

    /// <summary>Creates an <c>update</c> operation.</summary>
    public static PdsRepoOp Update(string path, byte[] cid, byte[]? prev) => new("update", path, cid, prev);

    /// <summary>Creates a <c>delete</c> operation.</summary>
    public static PdsRepoOp Delete(string path, byte[]? prev) => new("delete", path, null, prev);
}

/// <summary>
/// Encodes AT Protocol firehose frames. A frame is two concatenated DAG-CBOR values: a header
/// map (<c>{"op": 1, "t": "#commit"}</c>) followed by the event body.
/// <para>
/// This is the producer counterpart to
/// <see cref="ATProtoNet.Streaming.FirehoseEventParser"/>; frames written here round-trip
/// through it, which is how the unit tests assert conformance.
/// </para>
/// </summary>
public static class PdsFirehoseFrame
{
    private const CborTag CidTag = (CborTag)42;

    /// <summary>Encodes a <c>#commit</c> frame.</summary>
    /// <param name="seq">Sequence number.</param>
    /// <param name="time">Event timestamp.</param>
    /// <param name="did">The repository DID.</param>
    /// <param name="commitCid">Binary CID of the signed commit block.</param>
    /// <param name="rev">The commit revision.</param>
    /// <param name="since">The previous revision, or <c>null</c> for the first commit.</param>
    /// <param name="carBlocks">CAR-encoded blocks covering the commit, MST and touched records.</param>
    /// <param name="ops">The operations in this commit.</param>
    /// <param name="prevData">Binary CID of the previous MST root (Sync v1.1 inductive firehose).</param>
    /// <param name="tooBig">
    /// When <c>true</c>, <paramref name="carBlocks"/> is omitted and consumers are expected to
    /// fetch the repo out of band.
    /// </param>
    public static byte[] Commit(
        long seq,
        DateTimeOffset time,
        string did,
        byte[] commitCid,
        string rev,
        string? since,
        byte[] carBlocks,
        IReadOnlyList<PdsRepoOp> ops,
        byte[]? prevData,
        bool tooBig = false)
    {
        ArgumentNullException.ThrowIfNull(commitCid);
        ArgumentNullException.ThrowIfNull(carBlocks);
        ArgumentNullException.ThrowIfNull(ops);

        var writer = NewFrame("#commit");

        writer.WriteStartMap(prevData is not null ? 12 : 11);

        writer.WriteTextString("seq");
        writer.WriteInt64(seq);

        writer.WriteTextString("rebase");
        writer.WriteBoolean(false);

        writer.WriteTextString("tooBig");
        writer.WriteBoolean(tooBig);

        writer.WriteTextString("repo");
        writer.WriteTextString(did);

        writer.WriteTextString("commit");
        WriteCidLink(writer, commitCid);

        writer.WriteTextString("rev");
        writer.WriteTextString(rev);

        writer.WriteTextString("since");
        if (since is null) writer.WriteNull();
        else writer.WriteTextString(since);

        writer.WriteTextString("blocks");
        writer.WriteByteString(tooBig ? [] : carBlocks);

        writer.WriteTextString("ops");
        writer.WriteStartArray(ops.Count);
        foreach (var op in ops)
        {
            writer.WriteStartMap(op.Prev is not null ? 4 : 3);

            writer.WriteTextString("action");
            writer.WriteTextString(op.Action);

            writer.WriteTextString("path");
            writer.WriteTextString(op.Path);

            writer.WriteTextString("cid");
            if (op.Cid is null) writer.WriteNull();
            else WriteCidLink(writer, op.Cid);

            if (op.Prev is not null)
            {
                writer.WriteTextString("prev");
                WriteCidLink(writer, op.Prev);
            }

            writer.WriteEndMap();
        }
        writer.WriteEndArray();

        writer.WriteTextString("blobs");
        writer.WriteStartArray(0);
        writer.WriteEndArray();

        writer.WriteTextString("time");
        writer.WriteTextString(FormatTime(time));

        if (prevData is not null)
        {
            writer.WriteTextString("prevData");
            WriteCidLink(writer, prevData);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>Encodes a <c>#sync</c> frame, carrying the current commit without a diff.</summary>
    /// <param name="seq">Sequence number.</param>
    /// <param name="time">Event timestamp.</param>
    /// <param name="did">The repository DID.</param>
    /// <param name="carBlocks">A CAR whose first root is the commit block.</param>
    /// <param name="rev">The commit revision.</param>
    public static byte[] Sync(long seq, DateTimeOffset time, string did, byte[] carBlocks, string rev)
    {
        ArgumentNullException.ThrowIfNull(carBlocks);

        var writer = NewFrame("#sync");
        writer.WriteStartMap(5);

        writer.WriteTextString("seq");
        writer.WriteInt64(seq);

        writer.WriteTextString("did");
        writer.WriteTextString(did);

        writer.WriteTextString("blocks");
        writer.WriteByteString(carBlocks);

        writer.WriteTextString("rev");
        writer.WriteTextString(rev);

        writer.WriteTextString("time");
        writer.WriteTextString(FormatTime(time));

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>Encodes an <c>#identity</c> frame, signalling a DID document change.</summary>
    /// <param name="seq">Sequence number.</param>
    /// <param name="time">Event timestamp.</param>
    /// <param name="did">The affected DID.</param>
    /// <param name="handle">The new handle, when known.</param>
    public static byte[] Identity(long seq, DateTimeOffset time, string did, string? handle)
    {
        var writer = NewFrame("#identity");
        writer.WriteStartMap(handle is null ? 3 : 4);

        writer.WriteTextString("seq");
        writer.WriteInt64(seq);

        writer.WriteTextString("did");
        writer.WriteTextString(did);

        writer.WriteTextString("time");
        writer.WriteTextString(FormatTime(time));

        if (handle is not null)
        {
            writer.WriteTextString("handle");
            writer.WriteTextString(handle);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>Encodes an <c>#account</c> frame, signalling a hosting-status change.</summary>
    /// <param name="seq">Sequence number.</param>
    /// <param name="time">Event timestamp.</param>
    /// <param name="did">The affected DID.</param>
    /// <param name="active">Whether the account is currently active on this host.</param>
    /// <param name="status">
    /// The inactive reason (<c>takendown</c>, <c>suspended</c>, <c>deleted</c>, <c>deactivated</c>);
    /// must be omitted when <paramref name="active"/> is <c>true</c>.
    /// </param>
    public static byte[] Account(long seq, DateTimeOffset time, string did, bool active, string? status)
    {
        var writer = NewFrame("#account");
        writer.WriteStartMap(status is null ? 4 : 5);

        writer.WriteTextString("seq");
        writer.WriteInt64(seq);

        writer.WriteTextString("did");
        writer.WriteTextString(did);

        writer.WriteTextString("time");
        writer.WriteTextString(FormatTime(time));

        writer.WriteTextString("active");
        writer.WriteBoolean(active);

        if (status is not null)
        {
            writer.WriteTextString("status");
            writer.WriteTextString(status);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>Encodes an <c>#info</c> frame.</summary>
    /// <param name="name">The info code, e.g. <c>OutdatedCursor</c>.</param>
    /// <param name="message">An optional human-readable message.</param>
    public static byte[] Info(string name, string? message)
    {
        var writer = NewFrame("#info");
        writer.WriteStartMap(message is null ? 1 : 2);

        writer.WriteTextString("name");
        writer.WriteTextString(name);

        if (message is not null)
        {
            writer.WriteTextString("message");
            writer.WriteTextString(message);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>
    /// Encodes an error frame (<c>op: -1</c>), which terminates the subscription.
    /// </summary>
    /// <param name="error">The error name, e.g. <c>FutureCursor</c>.</param>
    /// <param name="message">An optional human-readable message.</param>
    public static byte[] Error(string error, string? message)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical, allowMultipleRootLevelValues: true);
        writer.WriteStartMap(1);
        writer.WriteTextString("op");
        writer.WriteInt32(-1);
        writer.WriteEndMap();

        writer.WriteStartMap(message is null ? 1 : 2);
        writer.WriteTextString("error");
        writer.WriteTextString(error);
        if (message is not null)
        {
            writer.WriteTextString("message");
            writer.WriteTextString(message);
        }
        writer.WriteEndMap();

        return writer.Encode();
    }

    private static CborWriter NewFrame(string type)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical, allowMultipleRootLevelValues: true);
        writer.WriteStartMap(2);
        writer.WriteTextString("op");
        writer.WriteInt32(1);
        writer.WriteTextString("t");
        writer.WriteTextString(type);
        writer.WriteEndMap();
        return writer;
    }

    private static void WriteCidLink(CborWriter writer, byte[] cidBytes)
    {
        writer.WriteTag(CidTag);
        var tagged = new byte[cidBytes.Length + 1];
        cidBytes.CopyTo(tagged.AsSpan(1));
        writer.WriteByteString(tagged);
    }

    /// <summary>
    /// Formats a timestamp the way the AT Protocol datetime rules require: UTC, with a
    /// <c>Z</c> suffix and sub-second precision.
    /// </summary>
    internal static string FormatTime(DateTimeOffset time)
        => time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the CAR payload for a commit event: the signed commit block first (as the CAR
    /// root), followed by the supporting MST and record blocks.
    /// </summary>
    internal static byte[] BuildCommitCar(
        byte[] commitCid, byte[] commitBlock, IEnumerable<CarBlock> supporting)
    {
        var blocks = new List<CarBlock> { new(commitCid, commitBlock) };
        blocks.AddRange(supporting);
        return CarWriter.Write([commitCid], blocks);
    }
}
