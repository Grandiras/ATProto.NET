using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace ATProtoNet.Repo;

/// <summary>A Merkle Search Tree key: <c>collection/rkey</c>, the path of a repository entry.</summary>
/// <remarks>
/// <para>Any key the reference implementation accepts is valid, including ones whose segments are
/// not an NSID or a record key (<see cref="TryGetRecordPath"/> tells them apart). Keys order by
/// their ASCII bytes. See <see href="https://atproto.com/specs/repository#mst-structure"/>.</para>
/// </remarks>
public readonly record struct MstKey : IComparable<MstKey>
{
    /// <summary>The longest key, in characters.</summary>
    public const int MaxLength = 1024;

    private readonly string? _value;

    private MstKey(string value, int layer) => (_value, Layer) = (value, layer);

    /// <summary>The key as text.</summary>
    public string Value => _value ?? throw new InvalidOperationException("A default MstKey has no value.");

    /// <summary>The tree layer the key sits on: the leading zero 2-bit pairs of SHA-256 over its bytes.</summary>
    public int Layer { get; }

    /// <summary>Parses a key.</summary>
    /// <param name="value">The key.</param>
    /// <returns>The key.</returns>
    /// <exception cref="FormatException">The value is not a valid MST key.</exception>
    public static MstKey Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var key) ? key : throw new FormatException($"'{value}' is not a valid MST key.");
    }

    /// <summary>Parses a key without throwing.</summary>
    /// <param name="value">The key.</param>
    /// <param name="key">The key, when valid.</param>
    /// <returns>Whether the value is a valid MST key.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out MstKey key)
    {
        key = default;
        if (!IsValid(value)) return false;
        Span<byte> bytes = stackalloc byte[value.Length];
        Encoding.ASCII.GetBytes(value, bytes);
        key = new(value.ToString(), LayerOf(bytes));
        return true;
    }

    /// <summary>The key of a record.</summary>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <returns>The key <c>collection/rkey</c>.</returns>
    public static MstKey For(Nsid collection, RecordKey rkey)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        return Parse($"{collection.Value}/{rkey.Value}");
    }

    /// <summary>Splits the key into a collection and a record key.</summary>
    /// <param name="collection">The collection, when the first segment is an NSID.</param>
    /// <param name="rkey">The record key, when the second segment is one.</param>
    /// <returns><see langword="false"/> for a valid MST key that is not a record path.</returns>
    public bool TryGetRecordPath([NotNullWhen(true)] out Nsid? collection, [NotNullWhen(true)] out RecordKey? rkey)
    {
        var slash = Value.IndexOf('/');
        rkey = null;
        return Nsid.TryParse(Value.AsSpan(0, slash), out collection) && RecordKey.TryParse(Value.AsSpan(slash + 1), out rkey);
    }

    /// <inheritdoc />
    public int CompareTo(MstKey other) => string.CompareOrdinal(_value, other._value);

    /// <inheritdoc />
    public override string ToString() => _value ?? "";

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    public static bool operator <(MstKey left, MstKey right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    public static bool operator >(MstKey left, MstKey right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> sorts before or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(MstKey left, MstKey right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> sorts after or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(MstKey left, MstKey right) => left.CompareTo(right) >= 0;

    // Keys as tree nodes hold them: ASCII bytes. The caller has validated them.
    internal static MstKey FromValidBytes(ReadOnlySpan<byte> key) => new(Encoding.ASCII.GetString(key), LayerOf(key));

    internal byte[] ToBytes() => Encoding.ASCII.GetBytes(Value);

    // Mirrors isValidMstKey of the reference implementation, over characters or node bytes.
    internal static bool IsValid<T>(ReadOnlySpan<T> key) where T : System.Numerics.IBinaryInteger<T>
    {
        if (key.Length is 0 or > MaxLength) return false;
        var separator = -1;
        for (var i = 0; i < key.Length; i++)
        {
            var c = int.CreateTruncating(key[i]);
            if (c == '/')
            {
                if (separator >= 0) return false;
                separator = i;
            }
            else if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '~' or '-' or ':' or '.'))
                return false;
        }
        return separator > 0 && separator < key.Length - 1;
    }

    internal static int LayerOf(ReadOnlySpan<byte> key)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(key, hash);
        var layer = 0;
        foreach (var b in hash)
        {
            if (b != 0) return layer + ((System.Numerics.BitOperations.LeadingZeroCount((uint)b) - 24) >> 1);
            layer += 4;
        }
        return layer;
    }
}
