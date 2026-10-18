namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The hostile-input corpus (design/04-testing.md, "Regression corpus"): inputs built to exhaust
/// the stack, the heap or a limit, which every reader rejects cleanly. <c>hostile/*.json</c>.
/// </summary>
public abstract class HostileInputConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>Rows of a <c>hostile/*.json</c> file.</summary>
    public static TheoryData<string> Cases(string file) => Vectors.Ids($"hostile/{file}");

    /// <summary>Hostile DRISL read leniently is rejected, or read within the limits.</summary>
    [Theory]
    [MemberData(nameof(Cases), "drisl.json")]
    [Trait("Spec", "DAT-26"), Trait("Spec", "DAT-30"), Trait("Spec", "DAT-32"), Trait("Spec", "DAT-33")]
    public void DecodeDataModel_HostileLenient_RejectedOrBounded(string vectorId) => AssertDecode(vectorId, DrislMode.Lenient);

    /// <summary>Hostile DRISL read strictly is rejected, or read within the limits.</summary>
    [Theory]
    [MemberData(nameof(Cases), "drisl-strict.json")]
    [Trait("Spec", "DAT-23"), Trait("Spec", "DAT-30"), Trait("Spec", "DAT-32"), Trait("Spec", "DAT-33")]
    public void DecodeDataModel_HostileStrict_RejectedOrBounded(string vectorId) => AssertDecode(vectorId, DrislMode.Strict);

    /// <summary>A hostile CAR is rejected, or read within the limits.</summary>
    [Theory]
    [MemberData(nameof(Cases), "car.json")]
    [Trait("Spec", "DAT-12"), Trait("Spec", "DAT-31"), Trait("Spec", "DAT-32"), Trait("Spec", "DAT-33"), Trait("Spec", "DAT-55")]
    public void ReadCar_Hostile_RejectedOrBounded(string vectorId)
    {
        Vector(vectorId);
        CidCarConformance<TAdapter>.AssertRead(Adapter, Vectors.Case(vectorId));
    }

    /// <summary>A hostile MST is rejected before it can exhaust anything.</summary>
    [Theory]
    [MemberData(nameof(Cases), "mst.json")]
    [Trait("Spec", "REP-6"), Trait("Spec", "REP-7"), Trait("Spec", "REP-11"), Trait("Spec", "REP-14"), Trait("Spec", "REP-15")]
    public void MstLoad_Hostile_RejectedOrBounded(string vectorId)
    {
        Vector(vectorId);
        MstConformance<TAdapter>.AssertLoad(Adapter, Vectors.Case(vectorId));
    }

    private void AssertDecode(string vectorId, DrislMode mode)
    {
        Vector(vectorId);
        DataModelConformance<TAdapter>.AssertDecode(Adapter, Vectors.Case(vectorId), mode);
    }
}
