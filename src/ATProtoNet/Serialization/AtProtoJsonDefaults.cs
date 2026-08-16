using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Serialization;

/// <summary>
/// Provides configured JSON serializer options and helpers for AT Protocol data.
/// </summary>
public static class AtProtoJsonDefaults
{
    /// <summary>
    /// Gets the default JSON serializer options configured for AT Protocol data.
    /// </summary>
    /// <remarks>
    /// <para>Initialized by the runtime's type initializer rather than a <c>??=</c> on first read:
    /// concurrent first calls could otherwise each build their own instance, and every
    /// <see cref="JsonSerializerOptions"/> carries its own reflection-derived contract cache.</para>
    /// <para>The instance is deliberately <em>not</em> frozen here.
    /// <see cref="JsonSerializer"/> calls <see cref="JsonSerializerOptions.MakeReadOnly()"/>
    /// itself on first use, so pre-freezing would buy nothing but would break the startup-time
    /// <c>AtProtoJsonDefaults.Options.Converters.Add(...)</c> that consumers can do today. Adding
    /// to it after the SDK has serialized anything still throws, as it always has — prefer
    /// passing your own <see cref="JsonSerializerOptions"/> to the client constructors.</para>
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Formats a <see cref="DateTime"/> as an AT Protocol-compliant ISO 8601 timestamp
    /// with millisecond precision and UTC "Z" suffix (e.g. "2024-01-15T12:30:45.123Z").
    /// </summary>
    /// <param name="dateTime">The date/time value. Will be treated as UTC.</param>
    /// <returns>An AT Protocol-compliant timestamp string.</returns>
    public static string FormatTimestamp(DateTime dateTime)
        => dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>
    /// Gets the current UTC time formatted as an AT Protocol-compliant timestamp.
    /// </summary>
    public static string NowTimestamp() => FormatTimestamp(DateTime.UtcNow);

    /// <summary>
    /// Contract modifier that guarantees every <see cref="AtProtoRecord"/>-derived type
    /// serializes its Lexicon type identifier as exactly one <c>$type</c> property.
    /// </summary>
    /// <param name="typeInfo">The type contract being built.</param>
    /// <remarks>
    /// <para><see cref="System.Text.Json"/> does not inherit <see cref="JsonPropertyNameAttribute"/>
    /// through property overrides, and it surfaces the abstract base member and the derived
    /// <c>override</c> as two independent JSON properties. Without this modifier a record declared
    /// the documented way (<c>public override string Type => "com.example.todo.item";</c>) writes
    /// both <c>"$type"</c> (from the base member) and a stray <c>"type"</c> (from the override,
    /// renamed by the camelCase policy) — polluting records that other AT Protocol apps read.</para>
    /// <para>This modifier is applied automatically by <see cref="Options"/> and by
    /// <see cref="LexiconTypeRegistry.CreateOptions"/>. Add it to your own
    /// <see cref="JsonSerializerOptions"/> if you build them from scratch:</para>
    /// <code>
    /// var options = new JsonSerializerOptions
    /// {
    ///     TypeInfoResolver = new DefaultJsonTypeInfoResolver
    ///     {
    ///         Modifiers = { AtProtoJsonDefaults.ApplyRecordTypeDiscriminator },
    ///     },
    /// };
    /// </code>
    /// </remarks>
    public static void ApplyRecordTypeDiscriminator(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        if (!typeof(AtProtoRecord).IsAssignableFrom(typeInfo.Type))
            return;

        // The base-declared member and every override share one virtual slot and so always
        // report the same value — collapse them to the first (most-derived) occurrence, which
        // also keeps "$type" at the front of the object.
        JsonPropertyInfo? discriminator = null;
        for (var i = 0; i < typeInfo.Properties.Count;)
        {
            if (IsTypeDiscriminator(typeInfo.Properties[i]))
            {
                if (discriminator is null)
                {
                    discriminator = typeInfo.Properties[i];
                }
                else
                {
                    typeInfo.Properties.RemoveAt(i);
                    continue;
                }
            }

            i++;
        }

        if (discriminator is not null)
            discriminator.Name = "$type";
    }

    /// <summary>
    /// Determines whether a contract property is <see cref="AtProtoRecord.Type"/> or an override
    /// of it — matching on the virtual slot rather than the name, so an unrelated <c>Type</c>
    /// member declared by a consumer record is left alone.
    /// </summary>
    private static bool IsTypeDiscriminator(JsonPropertyInfo property)
    {
        if (property.AttributeProvider is not PropertyInfo member
            || member.Name != nameof(AtProtoRecord.Type))
            return false;

        // Resolving the virtual slot excludes a `new`-shadowing member of the same name.
        var getter = member.GetMethod;
        return getter is not null
            && getter.GetBaseDefinition().DeclaringType == typeof(AtProtoRecord);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            // The Bluesky appview (and real-world record writers) put "$type" anywhere in
            // the object, not necessarily first — without this, every polymorphic union
            // payload (embed views, record-internal unions) fails to deserialize (#50).
            AllowOutOfOrderMetadataProperties = true,
            // AtProtoRecord subclasses would otherwise emit both "$type" (base member) and a
            // stray "type" (the override, which does not inherit [JsonPropertyName]) (#49).
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ApplyRecordTypeDiscriminator },
            },
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}

/// <summary>
/// Marker interface for AT Protocol union types.
/// </summary>
/// <remarks>
/// This is a documentation marker only — it carries no serialization behaviour and is not
/// required for a union to round-trip. Discriminated (de)serialization comes from
/// <see cref="JsonPolymorphicAttribute"/> / <see cref="JsonDerivedTypeAttribute"/> on the union
/// base, optionally extended at runtime through
/// <see cref="LexiconTypeRegistry.RegisterUnionVariant{TBase, TDerived}(string)"/>.
/// </remarks>
public interface IAtProtoUnion;
