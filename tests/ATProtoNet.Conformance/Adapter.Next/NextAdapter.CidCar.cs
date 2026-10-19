using System.Buffers;
using ATProtoNet.Data.Car;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> CidCarKnownFailures = new()
    {
    };

    /// <inheritdoc />
    public string ComputeCid(ReadOnlySpan<byte> data, CidCodec codec) =>
        Cid.Compute(codec is CidCodec.Raw ? ATProtoNet.CidCodec.Raw : ATProtoNet.CidCodec.DagCbor, data).Value;

    /// <inheritdoc />
    public CarContents? ReadCar(ReadOnlySpan<byte> car, bool verifyBlockCids)
    {
        CarFile file;
        try
        {
            file = CarReader.Read(car.ToArray(), new() { VerifyBlockCids = verifyBlockCids });
        }
        catch (FormatException)
        {
            return null;
        }
        return new([.. file.Header.Roots.Select(r => r.Value)], [.. file.Blocks.Select(b => new CarBlockData(b.Cid.Value, b.Data.ToArray()))]);
    }

    /// <inheritdoc />
    public byte[] WriteCar(IReadOnlyList<string> roots, IReadOnlyList<CarBlockData> blocks)
    {
        var car = new ArrayBufferWriter<byte>();
        CarWriter.Write(car, roots.Select(Cid.Parse), blocks.Select(b => new CarBlock(Cid.Parse(b.Cid), b.Data)));
        return car.WrittenSpan.ToArray();
    }
}
