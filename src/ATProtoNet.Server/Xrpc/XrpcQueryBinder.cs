using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ATProtoNet.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ATProtoNet.Server.Xrpc;

// Binds an XRPC query string to TParams by the type of each property.
//
// XRPC carries an array parameter as a repeated key, so whether ?uris=a is one string or a one-element
// list depends only on what the parameter is declared as. The plan below reads that from the
// serializer's own contract for TParams — names, types, required members, custom converters — once per
// type.
//
// Each request writes the query straight into a UTF-8 buffer as the JSON object the contract expects
// (a collection always as an array, a boolean as a boolean, everything else as a string the property's
// converter parses) and deserializes it from there, so identifier types are validated by their own
// parsers and constructor-bound types bind as they do from a body.
//
// TParams: The query parameters type.
internal static class XrpcQueryBinder<TParams>
    where TParams : class
{
    private static readonly Parameter[] Parameters = Plan(XrpcJson<TParams>.TypeInfo);

    // Binds query, answering InvalidRequest for a value that does not.
    //
    // query: The request's query string.
    //
    // Returns: The bound parameters.
    //
    // Throws XrpcException: A value is missing, repeated, or malformed.
    public static TParams Bind(IQueryCollection query)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var parameter in Parameters)
                parameter.Write(writer, query);
            writer.WriteEndObject();
        }

        try
        {
            return JsonSerializer.Deserialize(buffer.WrittenSpan, XrpcJson<TParams>.TypeInfo)
                   ?? throw new XrpcException(XrpcErrors.InvalidRequest, "Could not bind query parameters.");
        }
        catch (JsonException ex)
        {
            throw InvalidValue(ex);
        }
    }

    private static Parameter[] Plan(JsonTypeInfo typeInfo)
    {
        var options = typeInfo.Options;
        var parameters = new List<Parameter>(typeInfo.Properties.Count);

        foreach (var property in typeInfo.Properties)
        {
            if (property.IsExtensionData)
                continue;

            var propertyInfo = options.GetTypeInfo(property.PropertyType);
            var isCollection = propertyInfo.Kind == JsonTypeInfoKind.Enumerable;
            var valueType = isCollection ? propertyInfo.ElementType! : property.PropertyType;

            // A converter on the property decides for itself how to read text, as it did when
            // every value arrived as a string.
            var isBoolean = property.CustomConverter is null
                            && (Nullable.GetUnderlyingType(valueType) ?? valueType) == typeof(bool);

            parameters.Add(new Parameter(property.Name, isCollection, isBoolean, property.IsRequired));
        }

        return [.. parameters];
    }

    // The serializer's own message names CLR types and JSON positions, which mean nothing to a
    // caller who sent a query string; the parameter name is what it needs.
    private static XrpcException InvalidValue(JsonException exception)
    {
        var name = ParameterName(exception.Path);

        return new XrpcException(
            XrpcErrors.InvalidRequest,
            name is null ? "Could not bind query parameters." : $"Invalid value for query parameter '{name}'.",
            exception);
    }

    // The serializer reports "$.name" or "$.name[2]"; the parameter is the first segment.
    private static string? ParameterName(string? path)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
            return null;

        var name = path.AsSpan(2);
        var end = name.IndexOfAny('.', '[');
        return (end < 0 ? name : name[..end]).ToString();
    }

    private sealed class Parameter(string name, bool isCollection, bool isBoolean, bool isRequired)
    {
        // Accepted leniently for a collection, as the reference xrpc-server does.
        private readonly string _bracketedName = name + "[]";

        public void Write(Utf8JsonWriter writer, IQueryCollection query)
        {
            var values = query.TryGetValue(name, out var plain) ? plain : StringValues.Empty;

            if (isCollection && query.TryGetValue(_bracketedName, out var bracketed))
                values = StringValues.Concat(values, bracketed);

            if (values.Count == 0)
            {
                if (isRequired)
                    throw new XrpcException(XrpcErrors.InvalidRequest, $"Missing required query parameter '{name}'.");

                return;
            }

            writer.WritePropertyName(name);

            if (isCollection)
            {
                writer.WriteStartArray();
                foreach (var value in values)
                    WriteValue(writer, value);
                writer.WriteEndArray();
                return;
            }

            // Which of several values a scalar takes is exactly where two parsers in front of
            // each other disagree, so a repeated scalar is refused rather than resolved.
            if (values.Count > 1)
                throw new XrpcException(XrpcErrors.InvalidRequest, $"Query parameter '{name}' takes a single value.");

            WriteValue(writer, values[0]);
        }

        private void WriteValue(Utf8JsonWriter writer, string? value)
        {
            if (isBoolean && bool.TryParse(value, out var flag))
                writer.WriteBooleanValue(flag);
            else
                writer.WriteStringValue(value);
        }
    }
}
