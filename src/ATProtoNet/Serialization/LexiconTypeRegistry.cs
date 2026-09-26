using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Serialization;

/// <summary>
/// The runtime registry of Lexicon union variants that <see cref="AtProtoJsonDefaults.Options"/>, and
/// therefore every SDK client, consults alongside the <c>[JsonDerivedType]</c> attributes on a union base.
/// </summary>
/// <remarks>
/// <para>Use it to add a variant to a union you do not own, such as a custom embed on the SDK's
/// <c>EmbedBase</c>. Register at startup, before the first (de)serialization that involves the variant:</para>
/// <list type="bullet">
/// <item><description>For an <see cref="AtProtoUnionAttribute"/> base, reads consult the registry on
/// every discriminator the base does not declare, so a variant registered late is still picked up.
/// Writing a variant emits its <c>$type</c> from a contract built on first use, so register before
/// the variant is first serialized.</description></item>
/// <item><description>For a plain <see cref="JsonPolymorphicAttribute"/> base, the registered variants
/// are added to the base's contract when it is first built, and later registrations are not
/// seen.</description></item>
/// </list>
/// </remarks>
public sealed class LexiconTypeRegistry : ILexiconTypeRegistrar
{
    // base type → discriminator → variant type. Writers only add; readers never lock.
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Type>> _unionVariants = new();

    private LexiconTypeRegistry()
    {
    }

    /// <summary>
    /// Gets the registry that <see cref="AtProtoJsonDefaults.Options"/> consults.
    /// </summary>
    public static LexiconTypeRegistry Instance { get; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TBase"/> is not a union base, or is a closed one, or
    /// <paramref name="typeDiscriminator"/> is already declared or registered for a different
    /// variant.
    /// </exception>
    /// <remarks>
    /// A closed union (<c>[AtProtoUnion(Closed = true)]</c>) takes no registrations. The Lexicon
    /// that marks a union closed promises its readers that the declared variants are all there
    /// will ever be, so a variant added at runtime would write data that every other
    /// implementation rejects.
    /// </remarks>
    public void RegisterUnionVariant<TBase, TDerived>(string typeDiscriminator)
        where TBase : class
        where TDerived : class, TBase
    {
        ArgumentException.ThrowIfNullOrEmpty(typeDiscriminator);

        var baseType = typeof(TBase);
        var shape = AtProtoUnionShape.Get(baseType);
        if (shape is null && !IsPolymorphicBase(baseType))
        {
            throw new ArgumentException(
                $"'{baseType.Name}' is not a union base. Mark it [AtProtoUnion] (or [JsonPolymorphic]) "
                + "so its variants are resolved by $type.", nameof(TBase));
        }

        if (shape is { Closed: true })
        {
            throw new ArgumentException(
                $"'{baseType.Name}' is a closed union: its Lexicon allows no variants beyond the ones it declares.",
                nameof(TBase));
        }

        if (typeof(TDerived).IsAbstract)
            throw new ArgumentException($"'{typeof(TDerived).Name}' is abstract and cannot be a union variant.", nameof(TDerived));

        var declared = shape is not null
            ? shape.Variants.GetValueOrDefault(typeDiscriminator)
            : DeclaredPolymorphicVariant(baseType, typeDiscriminator);

        if (declared is not null && declared != typeof(TDerived))
        {
            throw new ArgumentException(
                $"'{typeDiscriminator}' is already declared on '{baseType.Name}' for '{declared.Name}'.",
                nameof(typeDiscriminator));
        }

        var variants = _unionVariants.GetOrAdd(baseType, static _ => new(StringComparer.Ordinal));
        var registered = variants.GetOrAdd(typeDiscriminator, typeof(TDerived));
        if (registered != typeof(TDerived))
        {
            throw new ArgumentException(
                $"'{typeDiscriminator}' is already registered on '{baseType.Name}' for '{registered.Name}'.",
                nameof(typeDiscriminator));
        }
    }

    /// <summary>
    /// Loads a Lexicon plugin, invoking its <see cref="ILexiconPlugin.Register"/> method.
    /// </summary>
    /// <typeparam name="TPlugin">The plugin type.</typeparam>
    public void LoadPlugin<TPlugin>() where TPlugin : ILexiconPlugin, new()
        => new TPlugin().Register(this);

    /// <summary>
    /// Gets the variants registered at runtime for a union base. Variants declared with
    /// <c>[JsonDerivedType]</c> on the base are not included.
    /// </summary>
    /// <param name="baseType">The union base type.</param>
    /// <returns>The registered (discriminator, variant type) pairs, ordered by discriminator.</returns>
    public IReadOnlyList<(string Discriminator, Type DerivedType)> GetUnionVariants(Type baseType)
    {
        ArgumentNullException.ThrowIfNull(baseType);

        return _unionVariants.TryGetValue(baseType, out var variants)
            ? [.. variants.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => (v.Key, v.Value))]
            : [];
    }

    internal bool TryGetVariant(Type baseType, string discriminator, [NotNullWhen(true)] out Type? variant)
    {
        variant = null;
        return _unionVariants.TryGetValue(baseType, out var variants)
            && variants.TryGetValue(discriminator, out variant);
    }

    internal bool TryGetDiscriminator(Type baseType, Type variant, [NotNullWhen(true)] out string? discriminator)
    {
        if (_unionVariants.TryGetValue(baseType, out var variants))
        {
            foreach (var (key, value) in variants)
            {
                if (value == variant)
                {
                    discriminator = key;
                    return true;
                }
            }
        }

        discriminator = null;
        return false;
    }

    /// <summary>
    /// Contract modifier behind <see cref="AtProtoJsonDefaults.Options"/>'s union handling.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>An <see cref="AtProtoUnionAttribute"/> base is converted by
    /// <see cref="AtProtoUnionConverterFactory"/>. Its <c>[JsonDerivedType]</c> attributes stay so that
    /// plain options still read the known variants, but <c>System.Text.Json</c> refuses polymorphism
    /// metadata on a type with a custom converter, so it is removed here.</description></item>
    /// <item><description>A variant of such a base gets a leading <c>$type</c> property that is
    /// written but never read. Writing emits the discriminator whether the variant is serialized
    /// through its base or on its own; reading matches the incoming <c>$type</c> to it and discards
    /// it, so it never lands in <see cref="Models.LexObject.ExtensionData"/>.</description></item>
    /// <item><description>A plain <see cref="JsonPolymorphicAttribute"/> base gets its registered
    /// variants added to its polymorphism options.</description></item>
    /// </list>
    /// </remarks>
    internal void ApplyUnionContracts(JsonTypeInfo typeInfo)
    {
        var type = typeInfo.Type;

        if (AtProtoUnionShape.Get(type) is not null)
        {
            typeInfo.PolymorphismOptions = null;
            return;
        }

        if (typeInfo.PolymorphismOptions is { } polymorphism && _unionVariants.TryGetValue(type, out var registered))
        {
            foreach (var (discriminator, variant) in registered.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                if (!polymorphism.DerivedTypes.Any(d => d.DerivedType == variant))
                    polymorphism.DerivedTypes.Add(new JsonDerivedType(variant, discriminator));
            }
        }

        if (typeInfo.Kind == JsonTypeInfoKind.Object && TryGetVariantDiscriminator(type, out var typeDiscriminator))
            AddTypeDiscriminatorProperty(typeInfo, typeDiscriminator);
    }

    private bool TryGetVariantDiscriminator(Type type, [NotNullWhen(true)] out string? discriminator)
    {
        discriminator = null;
        if (type.IsAbstract)
            return false;

        var owner = AtProtoUnionShape.FindOwner(type);
        if (owner is null)
            return false;

        return owner.Discriminators.TryGetValue(type, out discriminator)
            || TryGetDiscriminator(owner.BaseType, type, out discriminator);
    }

    private static void AddTypeDiscriminatorProperty(JsonTypeInfo typeInfo, string discriminator)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.Name == "$type")
                return;
        }

        var typeProperty = typeInfo.CreateJsonPropertyInfo(typeof(string), "$type");
        typeProperty.Get = _ => discriminator;
        typeInfo.Properties.Insert(0, typeProperty);
    }

    private static bool IsPolymorphicBase(Type type)
        => type.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false)
            || type.IsDefined(typeof(JsonDerivedTypeAttribute), inherit: false);

    private static Type? DeclaredPolymorphicVariant(Type baseType, string discriminator)
    {
        foreach (var derived in baseType.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false))
        {
            if (derived.TypeDiscriminator is string declared && declared == discriminator)
                return derived.DerivedType;
        }

        return null;
    }
}
