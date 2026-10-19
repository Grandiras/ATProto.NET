using System.Text.Json;

namespace ATProtoNet.Data;

/// <summary>Converts between the JSON form of the data model and DRISL directly, without building a <see cref="DataModelValue"/>.</summary>
/// <remarks>
/// The same rules apply as for <see cref="DataModelValue"/>: <c>{"$link"}</c> and <c>{"$bytes"}</c>
/// objects, integers only, at most <see cref="DrislLimits.MaxDepth"/> levels. See
/// <see href="https://atproto.com/specs/data-model"/>.
/// </remarks>
public static class DrislJson
{
    /// <summary>Encodes the JSON form of a value as canonical DRISL.</summary>
    /// <param name="utf8Json">One JSON value, as UTF-8.</param>
    /// <returns>The DRISL bytes.</returns>
    /// <exception cref="JsonException">The JSON is malformed or not a data-model value.</exception>
    public static byte[] ToDrisl(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        var sink = new CheckedSink<DrislWriter>(new());
        DataModelJson.Read(ref reader, ref sink);
        return reader.Read() ? throw new JsonException("Data follows the JSON value.") : sink.Inner.ToArray();
    }

    /// <summary>Writes one DRISL value in its JSON form.</summary>
    /// <param name="drisl">The DRISL bytes, nothing after the value.</param>
    /// <param name="writer">Receives the JSON; on failure it may hold part of the value.</param>
    /// <param name="options">The reading mode; strict when omitted.</param>
    /// <exception cref="FormatException">The bytes are not a data-model value under <paramref name="options"/>.</exception>
    public static void WriteJson(ReadOnlyMemory<byte> drisl, Utf8JsonWriter writer, DrislReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var reader = new DrislReader(drisl, options);
        var sink = new CheckedSink<JsonSink>(new(writer));
        DataModelSources.Drisl(reader, ref sink);
        reader.ReadEnd();
    }
}
