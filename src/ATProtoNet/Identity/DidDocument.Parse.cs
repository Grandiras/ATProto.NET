using System.Text.Json;

namespace ATProtoNet.Identity;

public sealed partial class DidDocument
{
    /// <summary>Parses a DID document.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>The document.</returns>
    /// <exception cref="FormatException">
    /// The JSON is not a DID document: no valid <c>id</c>, a <c>null</c> or misshapen list entry. Shapes real documents use are
    /// tolerated: missing or <c>null</c> lists read as empty, <c>@context</c> in any form is ignored, and a structured service
    /// endpoint reads as absent.
    /// </exception>
    public static DidDocument Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            Expect(ref reader, JsonTokenType.StartObject);
            Did? id = null;
            IReadOnlyList<string> aka = [];
            IReadOnlyList<VerificationMethod> methods = [];
            IReadOnlyList<DidService> services = [];
            while (Next(ref reader) is { } name)
                switch (name)
                {
                    case "id": id = Did.TryParse(String(ref reader), out var did) ? did : throw new FormatException("The document's id is not a valid DID."); break;
                    case "alsoKnownAs": aka = List(ref reader, static (ref r) => String(ref r)); break;
                    case "verificationMethod": methods = List(ref reader, ReadMethod); break;
                    case "service": services = List(ref reader, ReadService); break;
                    default: reader.Skip(); break;
                }
            Expect(ref reader, null);
            return new(id ?? throw new FormatException("The document has no id."), aka, methods, services, json.ToArray());
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The DID document is not valid JSON: {ex.Message}", ex);
        }
    }

    private static VerificationMethod ReadMethod(ref Utf8JsonReader reader)
    {
        string? id = null, type = null, controller = null, key = null;
        Expect(ref reader, JsonTokenType.StartObject, false);
        while (Next(ref reader) is { } name)
            switch (name)
            {
                case "id": id = String(ref reader); break;
                case "type": type = String(ref reader); break;
                case "controller": controller = String(ref reader); break;
                case "publicKeyMultibase": key = String(ref reader); break;
                default: reader.Skip(); break;
            }
        return new(id ?? throw Missing("verification method", "id"), type ?? throw Missing("verification method", "type"), controller, key);
    }

    private static DidService ReadService(ref Utf8JsonReader reader)
    {
        string? id = null, type = null, endpoint = null;
        Expect(ref reader, JsonTokenType.StartObject, false);
        while (Next(ref reader) is { } name)
            switch (name)
            {
                case "id": id = String(ref reader); break;
                case "type": type = String(ref reader); break;
                case "serviceEndpoint":
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String) endpoint = reader.GetString();
                    else reader.Skip();
                    break;
                default: reader.Skip(); break;
            }
        return new(id ?? throw Missing("service", "id"), type ?? throw Missing("service", "type"), endpoint);
    }

    private delegate T Item<T>(ref Utf8JsonReader reader);

    private static IReadOnlyList<T> List<T>(ref Utf8JsonReader reader, Item<T> item)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null) return [];
        if (reader.TokenType != JsonTokenType.StartArray) throw new FormatException("A DID document list is not an array.");
        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(item(ref reader));
        return items;
    }

    // Reads the next property name of the current object, or null at its end.
    private static string? Next(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.EndObject ? null : reader.GetString();
    }

    // A property's value, or the current token of a list item.
    private static string String(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.PropertyName) reader.Read();
        return reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new FormatException($"Expected a string in the DID document, not {reader.TokenType}.");
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType? token, bool read = true)
    {
        if (read && !reader.Read() && token is null) return;
        if (reader.TokenType != token) throw new FormatException($"Expected {token?.ToString() ?? "the end"} in the DID document, not {reader.TokenType}.");
    }

    private static FormatException Missing(string entry, string field) => new($"A DID document {entry} has no {field}.");
}
