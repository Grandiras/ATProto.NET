using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Serialization;

internal sealed class DidJsonConverter : JsonConverter<Did>
{
    public override Did? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : Did.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Did value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class HandleJsonConverter : JsonConverter<Handle>
{
    public override Handle? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : Handle.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Handle value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class AtIdentifierJsonConverter : JsonConverter<AtIdentifier>
{
    public override AtIdentifier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : AtIdentifier.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, AtIdentifier value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class NsidJsonConverter : JsonConverter<Nsid>
{
    public override Nsid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : Nsid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Nsid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class AtUriJsonConverter : JsonConverter<AtUri>
{
    public override AtUri? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : AtUri.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, AtUri value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class CidJsonConverter : JsonConverter<Cid>
{
    public override Cid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : Cid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Cid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class TidJsonConverter : JsonConverter<Tid>
{
    public override Tid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : Tid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Tid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class RecordKeyJsonConverter : JsonConverter<RecordKey>
{
    public override RecordKey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : RecordKey.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, RecordKey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
