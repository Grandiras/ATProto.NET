namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>The CIDv1 text (<c>b…</c>) of SHA-256 over <paramref name="data"/> with <paramref name="codec"/>.</summary>
    string ComputeCid(ReadOnlySpan<byte> data, CidCodec codec);

    /// <summary>
    /// Reads a CARv1 file, or returns <see langword="null"/> when it is rejected (also when
    /// <paramref name="verifyBlockCids"/> is set and a block does not match its CID).
    /// </summary>
    CarContents? ReadCar(ReadOnlySpan<byte> car, bool verifyBlockCids);

    /// <summary>Writes a CARv1 file with these roots and blocks, in order.</summary>
    byte[] WriteCar(IReadOnlyList<string> roots, IReadOnlyList<CarBlockData> blocks);
}

/// <summary>The CID codecs of the atproto data model.</summary>
public enum CidCodec
{
    /// <summary>DRISL (<c>0x71</c>).</summary>
    DagCbor,
    /// <summary>Raw bytes (<c>0x55</c>).</summary>
    Raw,
}

/// <summary>A CAR file's roots and blocks, in file order.</summary>
public sealed record CarContents(IReadOnlyList<string> Roots, IReadOnlyList<CarBlockData> Blocks);

/// <summary>One CAR block: its CID text and its bytes.</summary>
public sealed record CarBlockData(string Cid, byte[] Data);
