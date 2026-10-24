using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;

namespace ATProtoNet.Spaces;

/// <summary>
/// The homomorphic set hash a space repository commits to: each record expands to 1,024 little-endian
/// <c>u16</c> lanes, summed into the state modulo 2¹⁶, so the state depends only on the set of records.
/// </summary>
/// <remarks>
/// A mutable accumulator: it has no value equality, and <see cref="Clone"/> copies it. The element of a
/// record is <c>{collection}/{rkey}/{cid}</c>, expanded with BLAKE3; the digest is SHA-256 of the state.
/// See proposal 0016, <i>Commit digest</i>.
/// </remarks>
public sealed class LtHash
{
    /// <summary>The length of the state, in bytes.</summary>
    public const int StateLength = 2048;

    private readonly ushort[] _lanes = new ushort[StateLength / 2];

    private LtHash() { }

    /// <summary>The state of an empty repository.</summary>
    /// <returns>A new accumulator.</returns>
    public static LtHash Empty() => new();

    /// <summary>An accumulator resumed from a persisted state.</summary>
    /// <param name="state">The <see cref="StateLength"/>-byte state, as <see cref="GetState"/> returned it.</param>
    /// <returns>A new accumulator.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not <see cref="StateLength"/> bytes.</exception>
    public static LtHash FromState(ReadOnlySpan<byte> state)
    {
        if (state.Length != StateLength) throw new ArgumentException($"An LtHash state is {StateLength} bytes.", nameof(state));
        var hash = new LtHash();
        for (var i = 0; i < hash._lanes.Length; i++) hash._lanes[i] = BinaryPrimitives.ReadUInt16LittleEndian(state[(2 * i)..]);
        return hash;
    }

    /// <summary>Whether the state is that of an empty repository.</summary>
    public bool IsEmpty => !_lanes.AsSpan().ContainsAnyExcept((ushort)0);

    /// <summary>Adds a record.</summary>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">The record's CID.</param>
    public void Add(Nsid collection, RecordKey rkey, Cid cid) => Apply(Element(collection, rkey, cid), add: true);

    /// <summary>Removes a record; removing what was added restores the prior state exactly.</summary>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">The record's CID.</param>
    public void Remove(Nsid collection, RecordKey rkey, Cid cid) => Apply(Element(collection, rkey, cid), add: false);

    /// <summary>The digest a commit carries as its <c>data</c>: SHA-256 of the state.</summary>
    /// <returns>The 32-byte digest.</returns>
    public byte[] Digest() => SHA256.HashData(GetState());

    /// <summary>The state, for persistence.</summary>
    /// <returns>The <see cref="StateLength"/> bytes.</returns>
    public byte[] GetState()
    {
        var state = new byte[StateLength];
        for (var i = 0; i < _lanes.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(2 * i), _lanes[i]);
        return state;
    }

    /// <summary>A copy of the accumulator.</summary>
    /// <returns>An accumulator with the same state.</returns>
    public LtHash Clone()
    {
        var copy = new LtHash();
        _lanes.CopyTo(copy._lanes, 0);
        return copy;
    }

    // An element of any text: the interop vectors include ones that are not record elements.
    internal void Apply(ReadOnlySpan<byte> element, bool add)
    {
        Span<byte> expanded = stackalloc byte[StateLength];
        Blake3.Hash(element, expanded);
        for (var i = 0; i < _lanes.Length; i++)
        {
            var lane = BinaryPrimitives.ReadUInt16LittleEndian(expanded[(2 * i)..]);
            _lanes[i] = unchecked((ushort)(add ? _lanes[i] + lane : _lanes[i] - lane));
        }
    }

    private static byte[] Element(Nsid collection, RecordKey rkey, Cid cid)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        ArgumentNullException.ThrowIfNull(cid);
        return Encoding.UTF8.GetBytes($"{collection.Value}/{rkey.Value}/{cid.Value}");
    }
}
