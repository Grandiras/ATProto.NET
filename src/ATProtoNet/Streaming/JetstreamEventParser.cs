using System.Text.Json;
using ATProtoNet.Identity;

namespace ATProtoNet.Streaming;

/// <summary>
/// Parses Jetstream JSON frames into typed <see cref="JetstreamEvent"/> objects.
/// </summary>
/// <remarks>
/// The parser is forward-tolerant: unknown event kinds, unknown fields, and malformed
/// frames yield <c>null</c> rather than throwing, so consumers keep working when the
/// Jetstream protocol evolves.
/// </remarks>
public static class JetstreamEventParser
{
    /// <summary>
    /// Parse a single Jetstream JSON frame.
    /// </summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message.</param>
    /// <returns>The parsed event, or <c>null</c> if the frame is malformed or of an unknown kind.</returns>
    public static JetstreamEvent? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            return ParseCore(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse a single Jetstream JSON frame from a string.
    /// </summary>
    /// <param name="json">The JSON payload of one WebSocket message.</param>
    /// <returns>The parsed event, or <c>null</c> if the frame is malformed or of an unknown kind.</returns>
    public static JetstreamEvent? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseCore(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JetstreamEvent? ParseCore(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (!root.TryGetProperty("did", out var didProp) || didProp.ValueKind != JsonValueKind.String)
            return null;
        if (!root.TryGetProperty("time_us", out var timeProp) || !timeProp.TryGetInt64(out var timeUs))
            return null;
        if (!root.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
            return null;

        Did did;
        try
        {
            did = Did.Parse(didProp.GetString()!);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }

        return kindProp.GetString() switch
        {
            "commit" => ParseCommit(root, did, timeUs),
            "identity" => ParseIdentity(root, did, timeUs),
            "account" => ParseAccount(root, did, timeUs),
            _ => null, // Unknown kind — tolerate for forward compatibility
        };
    }

    private static JetstreamCommitEvent? ParseCommit(JsonElement root, Did did, long timeUs)
    {
        if (!root.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object)
            return null;

        if (!commit.TryGetProperty("collection", out var collectionProp)
            || collectionProp.ValueKind != JsonValueKind.String)
            return null;
        if (!commit.TryGetProperty("rkey", out var rkeyProp) || rkeyProp.ValueKind != JsonValueKind.String)
            return null;
        if (!commit.TryGetProperty("operation", out var opProp) || opProp.ValueKind != JsonValueKind.String)
            return null;

        JetstreamOperation operation;
        switch (opProp.GetString())
        {
            case "create": operation = JetstreamOperation.Create; break;
            case "update": operation = JetstreamOperation.Update; break;
            case "delete": operation = JetstreamOperation.Delete; break;
            default: return null; // Unknown operation — tolerate for forward compatibility
        }

        Cid? cid = null;
        if (commit.TryGetProperty("cid", out var cidProp) && cidProp.ValueKind == JsonValueKind.String)
        {
            try
            {
                cid = Cid.Parse(cidProp.GetString()!);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // Tolerate unparseable CIDs — the record data is still usable
            }
        }

        return new JetstreamCommitEvent
        {
            Did = did,
            TimeUs = timeUs,
            Collection = collectionProp.GetString()!,
            RKey = rkeyProp.GetString()!,
            Operation = operation,
            Rev = commit.TryGetProperty("rev", out var rev) && rev.ValueKind == JsonValueKind.String
                ? rev.GetString()
                : null,
            Cid = cid,
            Record = commit.TryGetProperty("record", out var record) && record.ValueKind == JsonValueKind.Object
                ? record.Clone()
                : null,
        };
    }

    private static JetstreamIdentityEvent ParseIdentity(JsonElement root, Did did, long timeUs)
    {
        string? handle = null;
        long? seq = null;
        string? time = null;

        if (root.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.Object)
        {
            if (identity.TryGetProperty("handle", out var handleProp)
                && handleProp.ValueKind == JsonValueKind.String)
                handle = handleProp.GetString();
            if (identity.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seqValue))
                seq = seqValue;
            if (identity.TryGetProperty("time", out var timeProp) && timeProp.ValueKind == JsonValueKind.String)
                time = timeProp.GetString();
        }

        return new JetstreamIdentityEvent
        {
            Did = did,
            TimeUs = timeUs,
            Handle = handle,
            Seq = seq,
            Time = time,
        };
    }

    private static JetstreamAccountEvent? ParseAccount(JsonElement root, Did did, long timeUs)
    {
        if (!root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return null;

        if (!account.TryGetProperty("active", out var activeProp)
            || activeProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new JetstreamAccountEvent
        {
            Did = did,
            TimeUs = timeUs,
            Active = activeProp.GetBoolean(),
            Status = account.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
                ? status.GetString()
                : null,
            Seq = account.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seqValue)
                ? seqValue
                : null,
            Time = account.TryGetProperty("time", out var timeProp) && timeProp.ValueKind == JsonValueKind.String
                ? timeProp.GetString()
                : null,
        };
    }
}
