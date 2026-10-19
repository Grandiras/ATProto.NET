using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Data;

// The JSON side of every data-model conversion: data-model violations surface as JsonException, as
// System.Text.Json reports its own.
internal static class DataModelJson
{
    public static DataModelValue Read(ref Utf8JsonReader reader)
    {
        var sink = new CheckedSink<DataModelTreeBuilder>(new());
        Read(ref reader, ref sink);
        return sink.Inner.Result;
    }

    public static void Read<TSink>(ref Utf8JsonReader reader, ref TSink sink) where TSink : IDataModelSink
    {
        try
        {
            if (reader.TokenType == JsonTokenType.None && !reader.Read()) throw new FormatException("The JSON is empty.");
            DataModelSources.Json(ref reader, ref sink);
        }
        // From the data-model rules, a duplicate key (DrislWriter) or an escaped lone surrogate (Utf8JsonReader).
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new JsonException(e.Message, e);
        }
    }
}

/// <summary>Reads and writes a <see cref="DataModelValue"/> in the data model's JSON form.</summary>
public sealed class DataModelValueJsonConverter : JsonConverter<DataModelValue>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override DataModelValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DataModelJson.Read(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DataModelValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        (value ?? DataModelNull.Instance).WriteJson(writer);
    }
}
