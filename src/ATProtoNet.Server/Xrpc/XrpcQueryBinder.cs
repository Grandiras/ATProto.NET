using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ATProtoNet.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Binds an XRPC query string to <typeparamref name="TParams"/> by the type of each property.
/// </summary>
/// <remarks>
/// <para>XRPC carries an array parameter as a repeated key, so whether <c>?uris=a</c> is one
/// string or a one-element list depends only on what the parameter is declared as. The plan below
/// reads that from the serializer's own contract for <typeparamref name="TParams"/> — names,
/// types, required members, custom converters — once per type.</para>
/// <para>Each request writes the query straight into a reused UTF-8 buffer as the JSON object the
/// contract expects (a collection always as an array, a boolean as a boolean, everything else as a
/// string the property's converter parses) and deserializes it from there, so identifier types
/// are validated by their own parsers and constructor-bound types bind as they do from a body.</para>
/// </remarks>
/// <typeparam name="TParams">The query parameters type.</typeparam>
internal static class XrpcQueryBinder<TParams>
    where TParams : class
{
    private static readonly Parameter[] Parameters = Plan(XrpcJson<TParams>.TypeInfo);

    /// <summary>
    /// Binds <paramref name="query"/>, answering <c>InvalidRequest</c> for a value that does not.
    /// </summary>
    /// <param name="query">The request's query string.</param>
    /// <returns>The bound parameters.</returns>
    /// <exception cref="XrpcException">A value is missing, repeated, or malformed.</exception>
    public static TParams Bind(IQueryCollection query)
    {
        var scratch = XrpcQueryScratch.Rent();
        try
        {
            var writer = scratch.Writer;
            writer.WriteStartObject();
            foreach (var parameter in Parameters)
                parameter.Write(writer, query);
            writer.WriteEndObject();
            writer.Flush();

            return JsonSerializer.Deserialize(scratch.Buffer.WrittenSpan, XrpcJson<TParams>.TypeInfo)
                   ?? throw new XrpcException(XrpcErrors.InvalidRequest, "Could not bind query parameters.");
        }
        catch (JsonException ex)
        {
            throw InvalidValue(ex);
        }
        finally
        {
            XrpcQueryScratch.Return(scratch);
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

/// <summary>
/// The buffer and writer a query binding writes into, reused per thread. Binding is synchronous,
/// so an instance is never shared across an await.
/// </summary>
internal sealed class XrpcQueryScratch
{
    // A query string past this leaves its buffer to the GC rather than pinning it to the thread.
    private const int MaxRetainedBytes = 16 * 1024;

    [ThreadStatic]
    private static XrpcQueryScratch? t_cached;

    private XrpcQueryScratch()
    {
        Buffer = new ArrayBufferWriter<byte>(256);
        Writer = new Utf8JsonWriter(Buffer);
    }

    public ArrayBufferWriter<byte> Buffer { get; }

    public Utf8JsonWriter Writer { get; }

    public static XrpcQueryScratch Rent()
    {
        var scratch = t_cached ?? new XrpcQueryScratch();
        t_cached = null;
        return scratch;
    }

    public static void Return(XrpcQueryScratch scratch)
    {
        if (scratch.Buffer.Capacity > MaxRetainedBytes)
            return;

        scratch.Buffer.ResetWrittenCount();
        scratch.Writer.Reset(scratch.Buffer);
        t_cached = scratch;
    }
}
