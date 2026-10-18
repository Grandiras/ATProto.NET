using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public string ComputeCid(ReadOnlySpan<byte> data, CidCodec codec) =>
        (codec is CidCodec.Raw ? CidComputation.ComputeForRaw(data) : CidComputation.ComputeForDagCbor(data)).Value;

    /// <inheritdoc />
    public CarContents? ReadCar(ReadOnlySpan<byte> car, bool verifyBlockCids)
    {
        CarReader reader;
        try
        {
            reader = CarReader.FromBytes(car, verifyBlockCids);
        }
        catch (FormatException)
        {
            return null;
        }

        return new([.. reader.Roots.Select(r => CidComputation.EncodeCidToString(r))], [.. reader.Blocks.Select(b => new CarBlockData(CidComputation.EncodeCidToString(b.Cid), b.Data))]);
    }

    /// <inheritdoc />
    public byte[] WriteCar(IReadOnlyList<string> roots, IReadOnlyList<CarBlockData> blocks) =>
        CarWriter.Write([.. roots.Select(CidComputation.DecodeCidString)], blocks.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Cid), b.Data)));
}
