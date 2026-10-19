using System.Reflection;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
using ATProtoNet.Data.Star;

namespace ATProtoNet.Tests.Data;

public class DataApiTests
{
    [Theory]
    [Trait("Spec", "DAT-27")]
    [Trait("Spec", "DAT-28")]
    [InlineData(typeof(CarReader))]
    [InlineData(typeof(StarReader))]
    [InlineData(typeof(SignedDrisl))]
    public void ContentAddressedReaders_TakeNoReadingMode_SoAlwaysReadStrictly(Type type) =>
        Assert.DoesNotContain(Methods(type).SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(DrislReaderOptions) || p.ParameterType == typeof(DrislMode));

    [Fact]
    [Trait("Spec", "DAT-43")]
    public void DataApi_NoEncoderOfArbitraryObjects()
    {
        // Typed objects reach DRISL through their generated contracts and DataModelValue, never through a
        // catch-all encoder with default serializer options.
        var offenders = typeof(DrislWriter).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("ATProtoNet.Data", StringComparison.Ordinal) == true)
            .SelectMany(Methods)
            .Where(m => m.Name != nameof(Equals) && (m.IsGenericMethod || m.GetParameters().Any(p => p.ParameterType == typeof(object))))
            .Select(m => $"{m.DeclaringType}.{m.Name}");
        Assert.Empty(offenders);
    }

    private static IEnumerable<MethodInfo> Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
}
