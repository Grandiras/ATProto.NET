using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.HttpSignatures;

namespace ATProtoNet.Spaces;

/// <summary>What a space MAC authenticates.</summary>
public enum SpaceMacType
{
    /// <summary>A repository commit (<c>atproto-space-repo</c>): <c>getLatestCommit</c>, <c>getRepo</c>, <c>listRepoOps</c>.</summary>
    Repo,
    /// <summary>One record (<c>atproto-space-record</c>): <c>getRecord</c>.</summary>
    Record,
}

/// <summary>
/// The MACs a repository host authenticates space data with, keyed by a secret shared with one requester
/// (<see cref="SpaceMacRequest"/>), and their response header. A MAC convinces the requester only: space
/// data is deniable and cannot be rebroadcast with proof.
/// </summary>
/// <remarks>
/// Each MAC is HMAC-SHA256 over a context, every string in it prefixed with its UTF-8 length as a big-endian
/// <c>uint16</c>, then the data. See proposal 0016, <i>Commit MAC</i> and <i>Record MAC</i>.
/// </remarks>
public static class SpaceMac
{
    /// <summary>The request header that asks for a MAC.</summary>
    public const string WantMacHeader = "Atproto-Space-Want-Mac";

    /// <summary>The response header that carries one.</summary>
    public const string MacHeader = "Atproto-Space-Mac";

    /// <summary>The protocol name of a MAC type.</summary>
    /// <param name="type">The type.</param>
    /// <returns><c>atproto-space-repo</c> or <c>atproto-space-record</c>.</returns>
    public static string TypeName(SpaceMacType type) => type is SpaceMacType.Repo ? "atproto-space-repo" : "atproto-space-record";

    /// <summary>The version a MAC type is on: commits share theirs with the commit format.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The version.</returns>
    public static int Version(SpaceMacType type) => type is SpaceMacType.Repo ? SpaceCommit.CurrentVersion : 1;

    /// <summary>The MAC of a commit: over <c>space</c>, <c>repo</c> and <c>rev</c>, then <c>data</c>.</summary>
    /// <param name="secret">The secret shared with the requester.</param>
    /// <param name="commit">The commit.</param>
    /// <returns>The 32-byte MAC.</returns>
    public static byte[] ComputeCommit(SharedSecret secret, SpaceCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return Compute(secret, SpaceMacType.Repo, [commit.Space.Value, commit.Repo.Value, commit.Rev], commit.Data.ToArray());
    }

    /// <summary>The MAC of a record: over its URI, then its binary CID.</summary>
    /// <param name="secret">The secret shared with the requester.</param>
    /// <param name="uri">The record's address.</param>
    /// <param name="cid">The record's CID.</param>
    /// <returns>The 32-byte MAC.</returns>
    public static byte[] ComputeRecord(SharedSecret secret, SpaceRecordUri uri, Cid cid)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cid);
        return Compute(secret, SpaceMacType.Record, [uri.Value], cid.ToByteArray());
    }

    /// <summary>Whether <paramref name="mac"/> is the commit's, compared in constant time.</summary>
    /// <param name="secret">The secret shared with the host.</param>
    /// <param name="commit">The commit as received.</param>
    /// <param name="mac">The MAC from the response header.</param>
    /// <returns>Whether it verifies; never for a commit of another version.</returns>
    public static bool VerifyCommit(SharedSecret secret, SpaceCommit commit, ReadOnlySpan<byte> mac) =>
        commit?.Ver == SpaceCommit.CurrentVersion && CryptographicOperations.FixedTimeEquals(ComputeCommit(secret, commit), mac);

    /// <summary>Whether <paramref name="mac"/> is the record's, compared in constant time.</summary>
    /// <param name="secret">The secret shared with the host.</param>
    /// <param name="uri">The record's address, as requested.</param>
    /// <param name="cid">The record's CID, as received.</param>
    /// <param name="mac">The MAC from the response header.</param>
    /// <returns>Whether it verifies. The caller still checks the record's bytes against <paramref name="cid"/>.</returns>
    public static bool VerifyRecord(SharedSecret secret, SpaceRecordUri uri, Cid cid, ReadOnlySpan<byte> mac) =>
        CryptographicOperations.FixedTimeEquals(ComputeRecord(secret, uri, cid), mac);

    /// <summary>The <see cref="MacHeader"/> value: <c>type="…", ver=…, mac=:…:</c>.</summary>
    /// <param name="type">What the MAC authenticates.</param>
    /// <param name="mac">The MAC.</param>
    /// <returns>The header value.</returns>
    public static string FormatHeader(SpaceMacType type, ReadOnlySpan<byte> mac) =>
        Header(type, new("mac", new StructuredItem((ReadOnlyMemory<byte>)mac.ToArray())));

    /// <summary>Reads a <see cref="MacHeader"/> value of the expected type and version.</summary>
    /// <param name="value">The header value.</param>
    /// <param name="type">The type the request asked for.</param>
    /// <returns>The MAC.</returns>
    /// <exception cref="FormatException">The header is malformed, of another type or version, or has no byte-sequence <c>mac</c>.</exception>
    public static byte[] ParseHeader(string value, SpaceMacType type) =>
        ParseFields(value, type).TryGetValue("mac", out var mac) && mac.Value is ReadOnlyMemory<byte> bytes
            ? bytes.ToArray()
            : throw new FormatException($"The {MacHeader} header has no byte-sequence mac.");

    internal static string Header(SpaceMacType type, KeyValuePair<string, StructuredMember> last) => StructuredFields.Serialize(new StructuredDictionary(
    [
        new("type", new StructuredItem(TypeName(type))),
        new("ver", new StructuredItem((long)Version(type))),
        last,
    ]));

    // Either header, its type and version checked, as bare items.
    internal static Dictionary<string, StructuredItem> ParseFields(string value, SpaceMacType type)
    {
        ArgumentNullException.ThrowIfNull(value);
        Dictionary<string, StructuredItem> fields = [];
        foreach (var (key, member) in StructuredFields.ParseDictionary(value).Members)
            fields[key] = member as StructuredItem ?? throw new FormatException($"The MAC header field {key} is an inner list.");
        if (!fields.TryGetValue("type", out var t) || t.Value is not string name || name != TypeName(type))
            throw new FormatException($"The MAC header is not of type {TypeName(type)}.");
        if (!fields.TryGetValue("ver", out var v) || v.Value is not long ver || ver != Version(type))
            throw new FormatException($"The MAC header is not of version {Version(type)}.");
        return fields;
    }

    // A string longer than its uint16 prefix cannot be framed; typed fields never are.
    internal static byte[] Compute(SharedSecret secret, SpaceMacType type, string[] context, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var message = new List<byte>(Encoding.UTF8.GetBytes($"{TypeName(type)}-v{Version(type)}"));
        Span<byte> length = stackalloc byte[2];
        foreach (var field in context)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
            message.AddRange(length);
            message.AddRange(bytes);
        }
        message.AddRange(data);
        var mac = new byte[SharedSecret.MacLength];
        secret.ComputeMac([.. message], mac);
        return mac;
    }
}
