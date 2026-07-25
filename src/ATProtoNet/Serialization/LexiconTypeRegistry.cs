using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Serialization;

/// <summary>
/// Runtime registry for Lexicon types, supporting plugin-registered record types
/// and union variants. Augments the static <c>[JsonDerivedType]</c> attributes
/// with dynamically-registered types.
/// </summary>
public sealed class LexiconTypeRegistry : ILexiconTypeRegistrar
{
    private readonly ConcurrentDictionary<string, Type> _recordTypes = new();
    private readonly ConcurrentDictionary<Type, List<(string Discriminator, Type DerivedType)>> _unionVariants = new();
    private readonly List<ILexiconPlugin> _plugins = [];
    private JsonSerializerOptions? _cachedOptions;

    /// <summary>
    /// Gets the singleton instance of the type registry.
    /// </summary>
    public static LexiconTypeRegistry Instance { get; } = new();

    /// <inheritdoc />
    public void RegisterRecordType<T>(string nsid) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(nsid);
        _recordTypes[nsid] = typeof(T);
        _cachedOptions = null; // Invalidate cache
    }

    /// <inheritdoc />
    public void RegisterUnionVariant<TBase, TDerived>(string typeDiscriminator)
        where TBase : class
        where TDerived : class, TBase
    {
        ArgumentException.ThrowIfNullOrEmpty(typeDiscriminator);

        var variants = _unionVariants.GetOrAdd(typeof(TBase), _ => []);
        lock (variants)
        {
            variants.Add((typeDiscriminator, typeof(TDerived)));
        }
        _cachedOptions = null; // Invalidate cache
    }

    /// <summary>
    /// Loads a Lexicon plugin, invoking its <see cref="ILexiconPlugin.Register"/> method.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type.</typeparam>
    public void LoadPlugin<TPlugin>() where TPlugin : ILexiconPlugin, new()
    {
        var plugin = new TPlugin();
        plugin.Register(this);
        _plugins.Add(plugin);
    }

    /// <summary>
    /// Scans an assembly for <see cref="LexiconPluginAttribute"/> and loads all found plugins.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    public void LoadPluginsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var attrs = assembly.GetCustomAttributes<LexiconPluginAttribute>();
        foreach (var attr in attrs)
        {
            if (Activator.CreateInstance(attr.PluginType) is ILexiconPlugin plugin)
            {
                plugin.Register(this);
                _plugins.Add(plugin);
            }
        }
    }

    /// <summary>
    /// Gets a registered record type by its NSID.
    /// </summary>
    /// <param name="nsid">The Lexicon NSID.</param>
    /// <returns>The registered type, or null if not found.</returns>
    public Type? GetRecordType(string nsid)
    {
        return _recordTypes.GetValueOrDefault(nsid);
    }

    /// <summary>
    /// Gets all registered record types.
    /// </summary>
    public IReadOnlyDictionary<string, Type> RecordTypes => _recordTypes;

    /// <summary>
    /// Gets all registered union variants for a base type.
    /// </summary>
    /// <param name="baseType">The union base type.</param>
    /// <returns>List of (discriminator, type) pairs.</returns>
    public IReadOnlyList<(string Discriminator, Type DerivedType)> GetUnionVariants(Type baseType)
    {
        return _unionVariants.TryGetValue(baseType, out var variants) ? variants : [];
    }

    /// <summary>
    /// Creates <see cref="JsonSerializerOptions"/> that includes runtime-registered union types
    /// alongside the built-in type hierarchy.
    /// </summary>
    public JsonSerializerOptions CreateOptions()
    {
        if (_cachedOptions is not null)
            return _cachedOptions;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            // Match AtProtoJsonDefaults: "$type" may appear anywhere in the object (#50).
            AllowOutOfOrderMetadataProperties = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ApplyUnionVariants },
            },
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        _cachedOptions = options;
        return options;
    }

    private void ApplyUnionVariants(JsonTypeInfo typeInfo)
    {
        if (typeInfo.PolymorphismOptions is null)
            return;

        if (!_unionVariants.TryGetValue(typeInfo.Type, out var variants))
            return;

        lock (variants)
        {
            foreach (var (discriminator, derivedType) in variants)
            {
                // Check if already registered (from [JsonDerivedType] attributes)
                var alreadyRegistered = false;
                foreach (var existing in typeInfo.PolymorphismOptions.DerivedTypes)
                {
                    if (existing.DerivedType == derivedType)
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (!alreadyRegistered)
                {
                    typeInfo.PolymorphismOptions.DerivedTypes.Add(
                        new JsonDerivedType(derivedType, discriminator));
                }
            }
        }
    }
}
