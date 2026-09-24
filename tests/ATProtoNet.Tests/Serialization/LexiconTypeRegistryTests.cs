using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

// ── Test Types ────────────────────────────────────────────────
// The registry is process-wide, so every base here is private to these tests.

[AtProtoUnion(typeof(UnknownRegistryTestUnion))]
[JsonDerivedType(typeof(BuiltInVariant), "com.example.builtIn")]
public abstract class RegistryTestUnion : LexObject;

public sealed class BuiltInVariant : RegistryTestUnion
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

public sealed class PluginVariant : RegistryTestUnion
{
    [JsonPropertyName("custom")]
    public string Custom { get; init; } = "";
}

public sealed class ConflictingVariant : RegistryTestUnion;

public sealed class UnknownRegistryTestUnion(string type, JsonElement raw) : RegistryTestUnion, IUnknownUnionVariant
{
    public string Type { get; } = type;

    public JsonElement Raw { get; } = raw;
}

[AtProtoUnion(typeof(UnknownPluginTestUnion))]
public abstract class PluginTestUnion : LexObject;

public sealed class PluginTestVariant : PluginTestUnion;

public sealed class UnknownPluginTestUnion(string type, JsonElement raw) : PluginTestUnion, IUnknownUnionVariant
{
    public string Type { get; } = type;

    public JsonElement Raw { get; } = raw;
}

public abstract class NotAUnion;

public sealed class NotAUnionVariant : NotAUnion;

// Open without naming its unknown variant: a declaration error.
[AtProtoUnion]
public abstract class MisdeclaredUnion;

// ── Test Plugin ───────────────────────────────────────────────

public sealed class TestLexiconPlugin : ILexiconPlugin
{
    public void Register(ILexiconTypeRegistrar registrar)
        => registrar.RegisterUnionVariant<PluginTestUnion, PluginTestVariant>("com.example.pluginVariant");
}

// ── Tests ─────────────────────────────────────────────────────

public class LexiconTypeRegistryTests
{
    private static LexiconTypeRegistry Registry => LexiconTypeRegistry.Instance;

    [Fact]
    public void RegisterUnionVariant_AddsVariant()
    {
        Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>("com.example.plugin");

        var variant = Assert.Single(Registry.GetUnionVariants(typeof(RegistryTestUnion)));
        Assert.Equal("com.example.plugin", variant.Discriminator);
        Assert.Equal(typeof(PluginVariant), variant.DerivedType);
    }

    [Fact]
    public void RegisterUnionVariant_SameRegistrationTwice_IsIdempotent()
    {
        Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>("com.example.plugin");
        Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>("com.example.plugin");

        Assert.Single(Registry.GetUnionVariants(typeof(RegistryTestUnion)));
    }

    [Fact]
    public void RegisterUnionVariant_DiscriminatorTakenByAnotherVariant_Throws()
    {
        Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>("com.example.plugin");

        Assert.Throws<ArgumentException>(
            () => Registry.RegisterUnionVariant<RegistryTestUnion, ConflictingVariant>("com.example.plugin"));
        Assert.Throws<ArgumentException>(
            () => Registry.RegisterUnionVariant<RegistryTestUnion, ConflictingVariant>("com.example.builtIn"));
    }

    [Fact]
    public void RegisterUnionVariant_BaseThatIsNotAUnion_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Registry.RegisterUnionVariant<NotAUnion, NotAUnionVariant>("com.example.notAUnion"));
    }

    [Fact]
    public void RegisterUnionVariant_EmptyDiscriminator_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>(""));
    }

    [Fact]
    public void LoadPlugin_InvokesRegister()
    {
        Registry.LoadPlugin<TestLexiconPlugin>();

        var variant = Assert.Single(Registry.GetUnionVariants(typeof(PluginTestUnion)));
        Assert.Equal(typeof(PluginTestVariant), variant.DerivedType);
        Assert.IsType<PluginTestVariant>(JsonSerializer.Deserialize<PluginTestUnion>(
            """{"$type":"com.example.pluginVariant"}""", AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void GetUnionVariants_DoesNotListDeclaredVariants()
    {
        Assert.DoesNotContain(Registry.GetUnionVariants(typeof(RegistryTestUnion)),
            v => v.DerivedType == typeof(BuiltInVariant));
        Assert.Empty(Registry.GetUnionVariants(typeof(NotAUnion)));
    }

    [Fact]
    public void DefaultOptions_DeserializeDeclaredVariant()
    {
        var result = JsonSerializer.Deserialize<RegistryTestUnion>(
            """{"$type":"com.example.builtIn","value":"test"}""", AtProtoJsonDefaults.Options);

        Assert.Equal("test", Assert.IsType<BuiltInVariant>(result).Value);
    }

    [Fact]
    public void DefaultOptions_RoundTripRegisteredVariant()
    {
        Registry.RegisterUnionVariant<RegistryTestUnion, PluginVariant>("com.example.plugin");

        var json = JsonSerializer.Serialize<RegistryTestUnion>(new PluginVariant { Custom = "world" }, AtProtoJsonDefaults.Options);

        Assert.Equal("""{"$type":"com.example.plugin","custom":"world"}""", json);
        Assert.Equal("world", Assert.IsType<PluginVariant>(
            JsonSerializer.Deserialize<RegistryTestUnion>(json, AtProtoJsonDefaults.Options)).Custom);
    }

    [Fact]
    public void OpenUnionWithoutUnknownVariant_IsRejectedWhenFirstUsed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JsonSerializer.Deserialize<MisdeclaredUnion>("""{"$type":"x"}""", AtProtoJsonDefaults.Options));

        Assert.Contains("Closed = true", ex.Message);
    }
}
