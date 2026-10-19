namespace ATProtoNet.Crypto;

/// <summary>The BLAKE3 hash: plain and keyed, one-shot and incremental, with any output length (extendable output).</summary>
/// <remarks>
/// A 32-byte output is the standard digest; longer outputs extend it. One-shot hashing allocates nothing, and long
/// outputs are computed 4 or 8 blocks at a time where the runtime has hardware vectors. LtHash is its first user.
/// See <see href="https://github.com/BLAKE3-team/BLAKE3-specs"/>.
/// </remarks>
public static partial class Blake3
{
    /// <summary>The length of the standard digest.</summary>
    public const int HashLength = 32;

    /// <summary>The length of a key for keyed hashing.</summary>
    public const int KeyLength = 32;

    private const int BlockLength = 64;

    /// <summary>Hashes <paramref name="input"/>, filling <paramref name="output"/> with as many bytes of output as it holds.</summary>
    /// <param name="input">The bytes to hash.</param>
    /// <param name="output">The output; <see cref="HashLength"/> bytes for the standard digest, any length for extended output.</param>
    public static void Hash(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var state = new State([]);
        state.Append(input);
        state.Squeeze(output);
    }

    /// <summary>Hashes <paramref name="input"/> in keyed mode, filling <paramref name="output"/>.</summary>
    /// <param name="key">The <see cref="KeyLength"/>-byte key.</param>
    /// <param name="input">The bytes to hash.</param>
    /// <param name="output">The output, of any length.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not <see cref="KeyLength"/> bytes.</exception>
    public static void HashKeyed(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        var state = new State(CheckKey(key));
        state.Append(input);
        state.Squeeze(output);
    }

    /// <summary>Starts an incremental hash.</summary>
    /// <returns>A hasher to append input to.</returns>
    public static Blake3Hasher CreateHasher() => new([]);

    /// <summary>Starts an incremental keyed hash.</summary>
    /// <param name="key">The <see cref="KeyLength"/>-byte key.</param>
    /// <returns>A hasher to append input to.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not <see cref="KeyLength"/> bytes.</exception>
    public static Blake3Hasher CreateKeyedHasher(ReadOnlySpan<byte> key) => new(CheckKey(key));

    private static ReadOnlySpan<byte> CheckKey(ReadOnlySpan<byte> key) =>
        key.Length == KeyLength ? key : throw new ArgumentException($"A BLAKE3 key is {KeyLength} bytes.", nameof(key));
}

/// <summary>An incremental BLAKE3 hash; not safe for concurrent use.</summary>
/// <remarks>Appending input in any split gives the same output as hashing it in one piece.</remarks>
public sealed class Blake3Hasher
{
    private Blake3.State _state;

    internal Blake3Hasher(ReadOnlySpan<byte> key) => _state = new(key);

    /// <summary>Appends <paramref name="data"/> to the input.</summary>
    /// <param name="data">The next bytes of input.</param>
    public void AppendData(ReadOnlySpan<byte> data) => _state.Append(data);

    /// <summary>Fills <paramref name="destination"/> with the output for the input so far, which more input may follow.</summary>
    /// <param name="destination">The output, of any length.</param>
    public void GetCurrentHash(Span<byte> destination) => _state.Squeeze(destination);
}
