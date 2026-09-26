using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// One record created, updated or deleted, as the firehose and Jetstream both report it: a
/// <see cref="JetstreamCommitEvent"/>, or a <see cref="FirehoseRecordEvent"/> from
/// <see cref="RecordEventExtensions.GetRecordEvents"/>. Code that indexes records can take either
/// stream through this.
/// </summary>
public interface IRecordEvent
{
    /// <summary>The account whose repository holds the record.</summary>
    Did Did { get; }

    /// <summary>The record's collection.</summary>
    Nsid Collection { get; }

    /// <summary>The record's key.</summary>
    RecordKey Rkey { get; }

    /// <summary>What happened to the record.</summary>
    RepoOpAction Operation { get; }

    /// <summary>The revision of the commit that changed the record, if known.</summary>
    Tid? Rev { get; }

    /// <summary>The record's CID after the change; null for a delete.</summary>
    Cid? Cid { get; }

    /// <summary>The record as JSON in the AT Protocol data model; null for a delete, or when it is not available.</summary>
    JsonElement? Record { get; }

    /// <summary>The record's <c>at://</c> URI.</summary>
    AtUri Uri { get; }

    /// <summary>
    /// Deserializes <see cref="Record"/> as <typeparamref name="T"/> with
    /// <see cref="AtProtoJsonDefaults.Options"/>, or returns null when there is no record.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    T? GetRecord<T>() where T : class;
}

/// <summary>
/// One operation of a firehose <see cref="CommitEvent"/>, with its record read from the commit's
/// blocks.
/// </summary>
public sealed class FirehoseRecordEvent : IRecordEvent
{
    private AtUri? _uri;

    /// <inheritdoc/>
    public required Did Did { get; init; }

    /// <inheritdoc/>
    public required Nsid Collection { get; init; }

    /// <inheritdoc/>
    public required RecordKey Rkey { get; init; }

    /// <inheritdoc/>
    public required RepoOpAction Operation { get; init; }

    /// <inheritdoc/>
    public Tid? Rev { get; init; }

    /// <inheritdoc/>
    public Cid? Cid { get; init; }

    /// <summary>The record's CID before the change, for an update or delete, when the relay sent it.</summary>
    public Cid? Prev { get; init; }

    /// <inheritdoc/>
    public JsonElement? Record { get; init; }

    /// <summary>The firehose sequence number of the commit.</summary>
    public long Seq { get; init; }

    /// <inheritdoc/>
    public AtUri Uri => _uri ??= AtUri.Create(AtIdentifier.FromDid(Did), Collection, Rkey);

    /// <inheritdoc/>
    public T? GetRecord<T>() where T : class
        => Record is { } record ? record.Deserialize<T>(AtProtoJsonDefaults.Options) : null;
}

/// <summary>Per-record views of firehose commits.</summary>
public static class RecordEventExtensions
{
    /// <summary>
    /// Splits a commit into one <see cref="FirehoseRecordEvent"/> per operation, decoding each
    /// created or updated record from the commit's blocks.
    /// </summary>
    /// <remarks>
    /// The blocks are read, not verified: verify the commit first
    /// (<see cref="TypedFirehoseConsumerOptions.Verifier"/>) if the records must be authentic. An
    /// operation whose path is not a valid <c>collection/rkey</c> is skipped, and one whose record
    /// block is missing or does not decode has a null <see cref="FirehoseRecordEvent.Record"/>.
    /// </remarks>
    /// <param name="commit">The commit.</param>
    /// <returns>The commit's operations, in order.</returns>
    public static IReadOnlyList<FirehoseRecordEvent> GetRecordEvents(this CommitEvent commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Ops is not { Count: > 0 } ops)
            return [];

        CarReader? car = null;
        if (commit.Blocks is { Length: > 0 } blocks)
        {
            try
            {
                car = CarReader.FromBytes(blocks);
            }
            catch (FormatException)
            {
                // Records stay null; the operations themselves are still meaningful.
            }
        }

        var events = new List<FirehoseRecordEvent>(ops.Count);
        foreach (var op in ops)
        {
            var slash = op.Path.IndexOf('/');
            if (slash < 0
                || !Nsid.TryParse(op.Path[..slash], out var collection)
                || !RecordKey.TryParse(op.Path[(slash + 1)..], out var rkey))
            {
                continue;
            }

            events.Add(new FirehoseRecordEvent
            {
                Did = commit.Repo,
                Collection = collection,
                Rkey = rkey,
                Operation = op.Action,
                Rev = commit.Rev,
                Cid = op.Cid,
                Prev = op.Prev,
                Record = op.Action == RepoOpAction.Delete ? null : ReadRecord(car, op.Cid),
                Seq = commit.Seq,
            });
        }

        return events;
    }

    private static JsonElement? ReadRecord(CarReader? car, Cid? cid)
    {
        if (car is null || cid is null || car.FindBlock(cid.ToBytes()) is not { } block)
            return null;

        try
        {
            return DagCborDecoder.Decode(block.Data);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
