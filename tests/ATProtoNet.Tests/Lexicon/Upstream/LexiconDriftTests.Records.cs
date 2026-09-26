using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Lexicon.Upstream;

public partial class LexiconDriftTests
{
    /// <summary>
    /// Every record model the SDK ships, so <c>client.GetCollection&lt;T&gt;()</c> works for each:
    /// an <see cref="AtProtoRecord"/>, or a model whose <c>$type</c> is an upstream record.
    /// </summary>
    private static IReadOnlyList<(Type Type, string WrittenType)> RecordModels() =>
        typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsGenericTypeDefinition)
            .Select(t => (Type: t, WrittenType: WrittenType(t)))
            .Where(r => typeof(AtProtoRecord).IsAssignableFrom(r.Type)
                || (r.WrittenType is { } written && Upstream.KindOf(written) == "record"))
            .Select(r => (r.Type, r.WrittenType ?? ""))
            .ToList();

    [Fact]
    public void RecordModels_ImplementIAtProtoRecord_WithTheCollectionTheyWrite()
    {
        var records = RecordModels();
        Assert.True(records.Count >= 25, $"Only {records.Count} record models found; is the scan still matching?");

        var failures = new List<string>();
        foreach (var (type, written) in records)
        {
            if (!typeof(IAtProtoRecord).IsAssignableFrom(type))
                failures.Add($"{Name(type)} writes $type {written} but does not implement IAtProtoRecord");
            else if (DeclaredCollection(type) is var collection && collection.Value != written)
                failures.Add($"{Name(type)} declares collection {collection} but writes $type {written}");
        }

        Assert.True(failures.Count == 0, "Record models without a matching collection:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void IAtProtoRecordImplementations_NameUpstreamRecords()
    {
        var failures = typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAtProtoRecord).IsAssignableFrom(t))
            .Select(t => (Type: t, Collection: DeclaredCollection(t)))
            .Where(r => !IsNotUpstream(r.Collection.Value + ".") && Upstream.KindOf(r.Collection.Value) != "record")
            .Select(r => $"{Name(r.Type)}: {r.Collection} is not an upstream record")
            .ToList();

        Assert.True(failures.Count == 0, "Collections that are not records:\n  " + string.Join("\n  ", failures));
    }

    private static string? WrittenType(Type type) =>
        type.GetProperty("Type") is { } property
        && property.PropertyType == typeof(string)
        && property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == "$type"
            ? property.GetValue(RuntimeHelpers.GetUninitializedObject(type)) as string
            : null;

    /// <summary>Reads <see cref="IAtProtoRecord.Collection"/>, implicit or explicit, from a type.</summary>
    private static Nsid DeclaredCollection(Type type)
    {
        var map = type.GetInterfaceMap(typeof(IAtProtoRecord));
        var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == $"get_{nameof(IAtProtoRecord.Collection)}");
        return (Nsid)map.TargetMethods[index].Invoke(null, null)!;
    }
}
