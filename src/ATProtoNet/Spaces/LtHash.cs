using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;

namespace ATProtoNet.Spaces;

/// <summary>
/// A homomorphic set hash over the records in a permissioned repo.
/// </summary>
/// <remarks>
/// <para>Each element expands to 1024 little-endian <see cref="ushort"/> lanes with BLAKE3 in
/// extendable-output mode, and those lanes are added into (or subtracted from) a fixed
/// 2048-byte state modulo 2^16. Both operations commute, so the state depends only on the set
/// of records a repo currently holds and not on the order they were written — two repos with
/// the same records always produce the same digest.</para>
/// <para>That is what makes a permissioned repo cheap to commit to. Adding or removing a
/// record is one pass over 1024 lanes rather than a recomputation over the whole repo, and a
/// syncer holding its own copy can compare digests to learn whether it is up to date without
/// transferring anything.</para>
/// <para>The construction is <see href="https://eprint.iacr.org/2019/227">LtHash</see>, a
/// lattice-based hash and therefore quantum-secure. This is the permissioned-data counterpart
/// to the Merkle Search Tree root that commits a public repository.</para>
/// </remarks>
public sealed class LtHash : IEquatable<LtHash>
{
    /// <summary>Number of 16-bit lanes in the state.</summary>
    public const int Lanes = 1024;

    /// <summary>Size of the state in bytes (<see cref="Lanes"/> × 2).</summary>
    public const int StateBytes = Lanes * 2;

    private readonly ushort[] _lanes;

    /// <summary>Creates an empty set hash, whose state is all zeroes.</summary>
    public LtHash()
    {
        _lanes = new ushort[Lanes];
    }

    /// <summary>
    /// Creates a set hash from a previously persisted state.
    /// </summary>
    /// <param name="state">
    /// A <see cref="StateBytes"/>-byte state as returned by <see cref="GetState"/>, or an empty
    /// span for an empty repo.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="state"/> is neither empty nor exactly <see cref="StateBytes"/> bytes.
    /// </exception>
    public LtHash(ReadOnlySpan<byte> state) : this()
    {
        if (state.IsEmpty)
            return;

        if (state.Length != StateBytes)
        {
            throw new ArgumentException(
                $"LtHash state must be {StateBytes} bytes, got {state.Length}.", nameof(state));
        }

        for (var i = 0; i < Lanes; i++)
            _lanes[i] = BinaryPrimitives.ReadUInt16LittleEndian(state[(i * 2)..]);
    }

    /// <summary>Whether the state is all zeroes, i.e. the repo holds no records.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var lane in _lanes)
            {
                if (lane != 0)
                    return false;
            }
            return true;
        }
    }

    /// <summary>Adds an element to the set.</summary>
    /// <param name="element">The element, typically <c>{collection}/{rkey}/{cid}</c>.</param>
    /// <returns>This instance, for chaining.</returns>
    public LtHash Add(string element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Span<ushort> expanded = stackalloc ushort[Lanes];
        Expand(element, expanded);
        for (var i = 0; i < Lanes; i++)
            _lanes[i] = unchecked((ushort)(_lanes[i] + expanded[i]));
        return this;
    }

    /// <summary>Removes an element from the set.</summary>
    /// <param name="element">The element, typically <c>{collection}/{rkey}/{cid}</c>.</param>
    /// <returns>This instance, for chaining.</returns>
    public LtHash Remove(string element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Span<ushort> expanded = stackalloc ushort[Lanes];
        Expand(element, expanded);
        for (var i = 0; i < Lanes; i++)
            _lanes[i] = unchecked((ushort)(_lanes[i] - expanded[i]));
        return this;
    }

    /// <summary>
    /// Returns the full <see cref="StateBytes"/>-byte state, for persistence. A repo host keeps
    /// this so it can update the hash incrementally; only <see cref="Digest"/> travels on the wire.
    /// </summary>
    public byte[] GetState()
    {
        var state = new byte[StateBytes];
        WriteState(state);
        return state;
    }

    /// <summary>Writes the full state into <paramref name="destination"/>.</summary>
    /// <param name="destination">A span of at least <see cref="StateBytes"/> bytes.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is too small.</exception>
    public void WriteState(Span<byte> destination)
    {
        if (destination.Length < StateBytes)
            throw new ArgumentException($"Destination must be at least {StateBytes} bytes.", nameof(destination));

        for (var i = 0; i < Lanes; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], _lanes[i]);
    }

    /// <summary>
    /// Returns <c>sha256(state)</c>, the 32-byte digest carried in a commit's
    /// <see cref="SignedSpaceCommit.Hash"/>.
    /// </summary>
    public byte[] Digest()
    {
        Span<byte> state = stackalloc byte[StateBytes];
        WriteState(state);
        return SHA256.HashData(state);
    }

    /// <summary>Creates an independent copy of this set hash.</summary>
    public LtHash Clone()
    {
        var clone = new LtHash();
        _lanes.CopyTo(clone._lanes, 0);
        return clone;
    }

    /// <inheritdoc/>
    public bool Equals(LtHash? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return CryptographicOperations.FixedTimeEquals(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(_lanes.AsSpan()),
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(other._lanes.AsSpan()));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LtHash other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // The digest is the canonical fingerprint; its first four bytes are as good a bucket
        // as any and avoid hashing 2 KB on every lookup.
        var digest = Digest();
        return BinaryPrimitives.ReadInt32LittleEndian(digest);
    }

    // Expand to 1024 u16 lanes with BLAKE3 in XOF mode.
    private static void Expand(string element, Span<ushort> lanes)
    {
        var byteCount = Encoding.UTF8.GetByteCount(element);
        byte[]? rented = null;
        var utf8 = byteCount <= 512
            ? stackalloc byte[512]
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            var written = Encoding.UTF8.GetBytes(element, utf8);

            Span<byte> expanded = stackalloc byte[StateBytes];
            Blake3.HashExtended(utf8[..written], expanded);

            for (var i = 0; i < Lanes; i++)
                lanes[i] = BinaryPrimitives.ReadUInt16LittleEndian(expanded[(i * 2)..]);
        }
        finally
        {
            if (rented is not null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
