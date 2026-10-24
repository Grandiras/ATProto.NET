using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ATProtoNet.Data;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>Record types by collection, for reading records whose type is known only at run time. An instance, never a global (D-10).</summary>
/// <remarks>Each generation unit's <c>{Name}Lexicons.Register</c> adds its records. Not thread-safe while being filled.</remarks>
public sealed class LexiconTypeRegistry
{
    private readonly Dictionary<string, (JsonTypeInfo TypeInfo, Func<JsonElement, object?> Read)> _records = new(StringComparer.Ordinal);

    /// <summary>Adds a record type; a later type for the same collection replaces the earlier one.</summary>
    /// <typeparam name="TRecord">The record type.</typeparam>
    public void AddRecord<TRecord>() where TRecord : class, IAtProtoRecord<TRecord> =>
        _records[TRecord.Collection.Value] = (TRecord.JsonTypeInfo, element => element.Deserialize(TRecord.JsonTypeInfo));

    /// <summary>Finds the contract of a collection's record type.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="typeInfo">The contract.</param>
    /// <returns>Whether a type is registered.</returns>
    public bool TryGetRecordType(Nsid collection, [NotNullWhen(true)] out JsonTypeInfo? typeInfo)
    {
        ArgumentNullException.ThrowIfNull(collection);
        typeInfo = _records.TryGetValue(collection.Value, out var entry) ? entry.TypeInfo : null;
        return typeInfo is not null;
    }

    internal IAtProtoRecord? Read(string collection, JsonElement element) => _records.TryGetValue(collection, out var entry) ? (IAtProtoRecord?)entry.Read(element) : null;
}

/// <summary>Reads <c>unknown</c> data-model values as generated records (LEX-59).</summary>
public static class LexiconRecordExtensions
{
    extension(DataModelValue value)
    {
        /// <summary>The value as a record of type <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The record type.</typeparam>
        /// <returns>The record.</returns>
        /// <exception cref="InvalidOperationException">The value's <c>$type</c> is not <typeparamref name="T"/>'s collection.</exception>
        /// <exception cref="JsonException">The value does not fit <typeparamref name="T"/>.</exception>
        public T As<T>() where T : class, IAtProtoRecord<T> =>
            value.TryAs<T>(out var record) ? record : throw new InvalidOperationException($"The value is not a {T.Collection} record.");

        /// <summary>Reads the value as a record of type <typeparamref name="T"/> when its <c>$type</c> is that collection.</summary>
        /// <typeparam name="T">The record type.</typeparam>
        /// <param name="record">The record.</param>
        /// <returns>Whether the <c>$type</c> matches.</returns>
        /// <exception cref="JsonException">The type matches but the value does not fit <typeparamref name="T"/>.</exception>
        public bool TryAs<T>([NotNullWhen(true)] out T? record) where T : class, IAtProtoRecord<T>
        {
            record = TypeOf(value) == T.Collection.Value ? ToElement(value).Deserialize(T.JsonTypeInfo) : null;
            return record is not null;
        }

        /// <summary>The value as the record type its <c>$type</c> names in <paramref name="registry"/>.</summary>
        /// <param name="registry">The record types.</param>
        /// <returns>The record, or <see langword="null"/> when the value has no registered <c>$type</c>.</returns>
        /// <exception cref="JsonException">The value does not fit its type.</exception>
        public IAtProtoRecord? AsKnownRecord(LexiconTypeRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return TypeOf(value) is { } type ? registry.Read(type, ToElement(value)) : null;
        }
    }

    private static string? TypeOf(DataModelValue value) =>
        value is DataModelMap map && map.TryGetValue("$type", out var type) && type is DataModelString text ? text.Value : null;

    private static JsonElement ToElement(DataModelValue value)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) value.WriteJson(writer);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
