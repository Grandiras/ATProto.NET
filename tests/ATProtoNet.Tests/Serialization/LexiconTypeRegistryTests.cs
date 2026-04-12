using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

// ── Test Types ────────────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(BuiltInVariant), "com.example.builtIn")]
public abstract class TestUnionBase;

public sealed class BuiltInVariant : TestUnionBase
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

public sealed class PluginVariant : TestUnionBase
{
    [JsonPropertyName("custom")]
    public string Custom { get; init; } = "";
}

public sealed class TestRecord
{
    [JsonPropertyName("$type")]
    public string Type => "com.example.test.record";

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed class AnotherRecord
{
    [JsonPropertyName("$type")]
    public string Type => "com.example.test.another";

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

// ── Test Plugin ───────────────────────────────────────────────

public sealed class TestLexiconPlugin : ILexiconPlugin
{
    public void Register(ILexiconTypeRegistrar registrar)
    {
        registrar.RegisterRecordType<TestRecord>("com.example.test.record");
        registrar.RegisterRecordType<AnotherRecord>("com.example.test.another");
        registrar.RegisterUnionVariant<TestUnionBase, PluginVariant>("com.example.plugin");
    }
}

// ── Tests ─────────────────────────────────────────────────────

public class LexiconTypeRegistryTests
{
    [Fact]
    public void RegisterRecordType_StoresType()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterRecordType<TestRecord>("com.example.test.record");

        Assert.Equal(typeof(TestRecord), registry.GetRecordType("com.example.test.record"));
    }

    [Fact]
    public void GetRecordType_UnknownNsid_ReturnsNull()
    {
        var registry = new LexiconTypeRegistry();
        Assert.Null(registry.GetRecordType("com.example.nonexistent"));
    }

    [Fact]
    public void RegisterUnionVariant_AddsVariant()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterUnionVariant<TestUnionBase, PluginVariant>("com.example.plugin");

        var variants = registry.GetUnionVariants(typeof(TestUnionBase));
        Assert.Single(variants);
        Assert.Equal("com.example.plugin", variants[0].Discriminator);
        Assert.Equal(typeof(PluginVariant), variants[0].DerivedType);
    }

    [Fact]
    public void LoadPlugin_InvokesRegister()
    {
        var registry = new LexiconTypeRegistry();
        registry.LoadPlugin<TestLexiconPlugin>();

        Assert.Equal(typeof(TestRecord), registry.GetRecordType("com.example.test.record"));
        Assert.Equal(typeof(AnotherRecord), registry.GetRecordType("com.example.test.another"));
        Assert.Single(registry.GetUnionVariants(typeof(TestUnionBase)));
    }

    [Fact]
    public void CreateOptions_DeserializesBuiltInVariant()
    {
        var registry = new LexiconTypeRegistry();
        var options = registry.CreateOptions();

        var json = """{"$type":"com.example.builtIn","value":"test"}""";
        var result = JsonSerializer.Deserialize<TestUnionBase>(json, options);

        Assert.IsType<BuiltInVariant>(result);
        Assert.Equal("test", ((BuiltInVariant)result).Value);
    }

    [Fact]
    public void CreateOptions_DeserializesPluginVariant()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterUnionVariant<TestUnionBase, PluginVariant>("com.example.plugin");
        var options = registry.CreateOptions();

        var json = """{"$type":"com.example.plugin","custom":"hello"}""";
        var result = JsonSerializer.Deserialize<TestUnionBase>(json, options);

        Assert.IsType<PluginVariant>(result);
        Assert.Equal("hello", ((PluginVariant)result).Custom);
    }

    [Fact]
    public void CreateOptions_SerializesPluginVariant()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterUnionVariant<TestUnionBase, PluginVariant>("com.example.plugin");
        var options = registry.CreateOptions();

        TestUnionBase obj = new PluginVariant { Custom = "world" };
        var json = JsonSerializer.Serialize(obj, options);

        Assert.Contains("\"$type\":\"com.example.plugin\"", json);
        Assert.Contains("\"custom\":\"world\"", json);
    }

    [Fact]
    public void RecordTypes_ReturnsAllRegistered()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterRecordType<TestRecord>("com.example.test.record");
        registry.RegisterRecordType<AnotherRecord>("com.example.test.another");

        Assert.Equal(2, registry.RecordTypes.Count);
    }

    [Fact]
    public void LoadPluginsFromAssembly_FindsAttributedPlugins()
    {
        // This test verifies the scanning works — even if no plugins, no error
        var registry = new LexiconTypeRegistry();
        registry.LoadPluginsFromAssembly(typeof(LexiconTypeRegistryTests).Assembly);

        // The test assembly doesn't have [assembly: LexiconPlugin] so nothing is registered
        Assert.Empty(registry.RecordTypes);
    }

    [Fact]
    public void RegisterRecordType_OverwritesPrevious()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterRecordType<TestRecord>("com.example.test.record");
        registry.RegisterRecordType<AnotherRecord>("com.example.test.record");

        Assert.Equal(typeof(AnotherRecord), registry.GetRecordType("com.example.test.record"));
    }
}
