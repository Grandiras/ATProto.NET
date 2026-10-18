using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using ATProtoNet.Streaming;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public JsonObject ParseJetstreamFrame(string json, int protocol)
    {
        var frame = ParseJetstream(null, Encoding.UTF8.GetBytes(json), protocol == 2 ? JetstreamProtocol.V2 : JetstreamProtocol.V1, out var dropped);
        if (frame.Event is { } evt)
            return Project(evt);
        if (frame.Info is { } info)
            return new() { ["kind"] = "info", ["name"] = info.Name, ["message"] = info.Message };
        if (frame.Error is { } error)
            return new() { ["kind"] = "error", ["error"] = error.Error, ["message"] = error.Message };
        return new() { ["kind"] = "dropped", ["reason"] = dropped == StreamDropReason.UnknownType ? "unknownType" : "malformed" };
    }

    /// <inheritdoc />
    public JsonArray? DecodeJetstreamBlock(byte[] block)
    {
        IReadOnlyList<JetstreamArchiveRow> rows;
        try
        {
            rows = DecodeBlock(null, block);
        }
        catch (JetstreamException)
        {
            return null;
        }

        return new JsonArray([.. rows.Select(row => new JsonObject
        {
            ["seq"] = row.Seq,
            ["witnessedAt"] = row.WitnessedAt,
            ["indexedAt"] = row.IndexedAt,
            ["timeUs"] = row.TimeUs,
            ["kind"] = Camel(row.Kind.ToString()),
            ["did"] = row.Did,
            ["collection"] = row.Collection,
            ["rkey"] = row.Rkey,
            ["rev"] = row.Rev,
            ["payload"] = Convert.ToHexStringLower(row.Payload.Span),
            ["event"] = row.ToEvent() is { } evt ? Project(evt) : null,
        })]);
    }

    /// <inheritdoc />
    public JsonObject? ReadJetstreamSegmentHeader(byte[] header)
    {
        JetstreamSegmentHeader parsed;
        try
        {
            parsed = JetstreamSegmentReader.ReadHeader(header);
        }
        catch (JetstreamException)
        {
            return null;
        }

        return new()
        {
            ["checksum"] = parsed.Checksum.ToString("x16"),
            ["version"] = parsed.Version,
            ["blockCount"] = parsed.BlockCount,
            ["eventCount"] = parsed.EventCount,
            ["uniqueDidCount"] = parsed.UniqueDidCount,
            ["minSeq"] = parsed.MinSeq,
            ["maxSeq"] = parsed.MaxSeq,
            ["minWitnessedAt"] = parsed.MinWitnessedAt,
            ["maxWitnessedAt"] = parsed.MaxWitnessedAt,
            ["footerOffset"] = parsed.FooterOffset,
            ["blockIndexOffset"] = parsed.BlockIndexOffset,
            ["didBloomOffset"] = parsed.DidBloomOffset,
            ["blockDidBloomOffset"] = parsed.BlockDidBloomOffset,
            ["collectionIndexOffset"] = parsed.CollectionIndexOffset,
        };
    }

    private static JsonObject Project(JetstreamEvent evt)
    {
        var result = new JsonObject { ["kind"] = "event", ["did"] = evt.Did.Value, ["timeUs"] = evt.TimeUs, ["cursor"] = evt.Cursor };
        switch (evt)
        {
            case JetstreamCommitEvent commit:
                result["type"] = "commit";
                result["operation"] = commit.Operation.ToString().ToLowerInvariant();
                result["collection"] = commit.Collection.Value;
                result["rkey"] = commit.Rkey.Value;
                result["rev"] = commit.Rev?.Value;
                result["cid"] = commit.Cid?.Value;
                result["uri"] = commit.Uri.ToString();
                result["record"] = commit.Record is { } record ? JsonNode.Parse(record.GetRawText()) : null;
                break;
            case JetstreamIdentityEvent identity:
                result["type"] = "identity";
                result["handle"] = identity.Handle?.Value;
                result["seq"] = identity.Seq;
                result["time"] = identity.Time?.ToString();
                break;
            case JetstreamAccountEvent account:
                result["type"] = "account";
                result["active"] = account.Active;
                result["status"] = account.Status;
                result["seq"] = account.Seq;
                result["time"] = account.Time?.ToString();
                break;
            case JetstreamSyncEvent sync:
                result["type"] = "sync";
                result["rev"] = sync.Rev?.Value;
                result["seq"] = sync.Seq;
                result["time"] = sync.Time?.ToString();
                result["blocks"] = sync.Blocks is { } blocks ? Convert.ToBase64String(blocks) : null;
                break;
        }

        return result;
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Parse")]
    private static extern JetstreamFrame ParseJetstream(
        [UnsafeAccessorType("ATProtoNet.Streaming.JetstreamEventParser, ATProtoNet")] object? _, ReadOnlyMemory<byte> json, JetstreamProtocol protocol, out StreamDropReason? dropped);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "DecodeBlock")]
    private static extern IReadOnlyList<JetstreamArchiveRow> DecodeBlock(
        [UnsafeAccessorType("ATProtoNet.Streaming.JetstreamSegmentReader, ATProtoNet")] object? _, ReadOnlySpan<byte> block);
}
