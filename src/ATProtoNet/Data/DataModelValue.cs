using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Data;

/// <summary>Any atproto data-model value, without a schema: the bridge between DRISL, JSON and generated types.</summary>
/// <remarks>
/// The kinds are exactly those of the data model: <see cref="DataModelNull"/>, <see cref="DataModelBoolean"/>,
/// <see cref="DataModelInteger"/>, <see cref="DataModelString"/>, <see cref="DataModelBytes"/>,
/// <see cref="DataModelLink"/>, <see cref="DataModelArray"/> and <see cref="DataModelMap"/>; there is no
/// float. Conversions apply the data-model rules in both directions: at most
/// <see cref="DrislLimits.MaxDepth"/> levels, <c>$type</c> a non-empty string, and a
/// <c>{"$type": "blob"}</c> map exactly a blob. See <see href="https://atproto.com/specs/data-model"/>.
/// </remarks>
[JsonConverter(typeof(DataModelValueJsonConverter))]
public closed record DataModelValue
{
    /// <summary>Reads one DRISL value.</summary>
    /// <param name="cbor">The DRISL bytes, nothing after the value.</param>
    /// <param name="options">The reading mode; strict when omitted.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The bytes are not a data-model value under <paramref name="options"/>.</exception>
    public static DataModelValue FromDrisl(ReadOnlyMemory<byte> cbor, DrislReaderOptions? options = null)
    {
        var reader = new DrislReader(cbor, options);
        var value = Read(reader);
        reader.ReadEnd();
        return value;
    }

    // Reads the value at the reader's position.
    internal static DataModelValue Read(DrislReader reader)
    {
        var sink = new CheckedSink<DataModelTreeBuilder>(new());
        DataModelSources.Drisl(reader, ref sink);
        return sink.Inner.Result;
    }

    /// <summary>Reads the JSON form of a value (<c>$link</c>, <c>$bytes</c>).</summary>
    /// <param name="utf8Json">One JSON value, as UTF-8.</param>
    /// <returns>The value.</returns>
    /// <exception cref="JsonException">The JSON is malformed or not a data-model value (a float, a malformed <c>$link</c>, a duplicate key).</exception>
    public static DataModelValue FromJson(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        var value = DataModelJson.Read(ref reader);
        return reader.Read() ? throw new JsonException("Data follows the JSON value.") : value;
    }

    /// <summary>The canonical DRISL encoding.</summary>
    /// <returns>New bytes.</returns>
    /// <exception cref="FormatException">The value breaks a data-model rule, such as the depth limit.</exception>
    public byte[] ToDrisl()
    {
        var sink = new CheckedSink<DrislWriter>(new());
        DataModelSources.Tree(this, ref sink);
        return sink.Inner.ToArray();
    }

    /// <summary>Writes the JSON form: maps as objects, links as <c>{"$link"}</c>, bytes as unpadded <c>{"$bytes"}</c>.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <exception cref="FormatException">The value breaks a data-model rule, such as the depth limit.</exception>
    public void WriteJson(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new CheckedSink<JsonSink>(new(writer));
        DataModelSources.Tree(this, ref sink);
    }

    /// <summary>The compact JSON form, or the kind's name for a value no encoding accepts.</summary>
    /// <returns>The JSON text.</returns>
    public sealed override string ToString()
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            WriteJson(writer);
        }
        // A lone surrogate or nesting past the limit: ToString must not throw.
        catch (FormatException)
        {
            return GetType().Name;
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

/// <summary>The data-model null.</summary>
public sealed record DataModelNull : DataModelValue
{
    private DataModelNull() { }

    /// <summary>The null value.</summary>
    public static DataModelNull Instance { get; } = new();
}

/// <summary>A data-model boolean.</summary>
/// <param name="Value">The value.</param>
public sealed record DataModelBoolean(bool Value) : DataModelValue;

/// <summary>A data-model integer: signed 64 bits.</summary>
/// <param name="Value">The value.</param>
public sealed record DataModelInteger(long Value) : DataModelValue;

/// <summary>A data-model string.</summary>
public sealed record DataModelString : DataModelValue
{
    /// <summary>Creates the string.</summary>
    /// <param name="value">The text.</param>
    public DataModelString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The text.</summary>
    public string Value { get; }
}

/// <summary>A data-model byte string; its JSON form is <c>{"$bytes": "&lt;base64&gt;"}</c>.</summary>
/// <param name="Value">The bytes. Equality compares them by content.</param>
public sealed record DataModelBytes(ReadOnlyMemory<byte> Value) : DataModelValue
{
    /// <inheritdoc />
    public bool Equals(DataModelBytes? other) => other is not null && Value.Span.SequenceEqual(other.Value.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Value.Span);
        return hash.ToHashCode();
    }
}

/// <summary>A data-model link; its JSON form is <c>{"$link": "b…"}</c>.</summary>
public sealed record DataModelLink : DataModelValue
{
    /// <summary>Creates the link.</summary>
    /// <param name="cid">The linked CID.</param>
    public DataModelLink(Cid cid)
    {
        ArgumentNullException.ThrowIfNull(cid);
        Cid = cid;
    }

    /// <summary>The linked CID.</summary>
    public Cid Cid { get; }
}
