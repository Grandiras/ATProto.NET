using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Serialization;

namespace ATProtoNet.Conformance.Legacy;

/// <summary>
/// Finds the hand-written 0.8 client method behind an NSID (<c>chat.bsky.convo.listConvos</c> is
/// <c>client.Chat.Convo.ListConvosAsync</c>) and calls it with the fixture's parameters bound by
/// name, deserialised into the method's own parameter types.
/// </summary>
internal static class LegacyXrpc
{
    private static readonly JsonSerializerOptions Json = AtProtoJsonDefaults.Options;

    private static readonly Dictionary<string, string> Roots = new()
    {
        ["app.bsky"] = "Bsky", ["chat.bsky"] = "Chat", ["tools.ozone"] = "Ozone", ["com.atproto"] = "",
    };

    public static async Task<object?> CallAsync(AtProtoClient client, XrpcFixtureCall call, CancellationToken cancellationToken)
    {
        if (LegacyXrpcMap.Custom.TryGetValue(call.Nsid, out var custom))
            return await custom(client, call.Params, cancellationToken);

        var (target, method, parameters) = Route(client, call);
        var (candidate, args) = target.GetType().GetMethods()
            .Where(m => m.Name == method && !m.IsGenericMethodDefinition)
            .Select(m => (Method: m, Args: TryBind(m, call.Nsid, parameters, cancellationToken)))
            .FirstOrDefault(c => c.Args is not null);
        if (candidate is null)
            throw new MissingMethodException($"No 0.8 method {target.GetType().Name}.{method} takes {parameters.ToJsonString()}.");

        var invoked = candidate.Invoke(target, args);
        if (invoked is not Task task) return invoked;
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        return result is bool || result?.GetType().Name is "VoidTaskResult" ? null : result;
    }

    public static JsonNode? ToLexiconJson(string nsid, object result)
    {
        var node = JsonSerializer.SerializeToNode(result, result.GetType(), Json);
        return LegacyXrpcMap.Unwrapped.TryGetValue(nsid, out var property) ? new JsonObject { [property] = node } : node;
    }

    private static (object Target, string Method, JsonObject Parameters) Route(AtProtoClient client, XrpcFixtureCall call)
    {
        var parts = call.Nsid.Split('.');
        var verb = parts[^1];
        if (call.Params["collection"]?.GetValue<string>() is { } collection && LegacyXrpcMap.SiteRecords.TryGetValue(collection, out var site))
        {
            var list = verb.EndsWith("Records", StringComparison.Ordinal);
            var parameters = (JsonObject)call.Params.DeepClone();
            parameters.Remove("collection");
            return (client.Site, Pascal(verb[..^(list ? 7 : 6)]) + (list ? site.Plural : site.Singular) + "Async", parameters);
        }

        object target = client;
        foreach (var segment in new[] { Roots[string.Join('.', parts[..2])], LegacyXrpcMap.Group(parts[2]) }.Where(s => s.Length > 0))
            target = target.GetType().GetProperty(segment)!.GetValue(target)!;
        return (target, Pascal(verb) + "Async", call.Params);
    }

    private static object?[]? TryBind(MethodInfo method, string nsid, JsonObject json, CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        var given = json.ToDictionary(p => LegacyXrpcMap.Rename(nsid, p.Key), p => p.Value);
        var data = parameters.Where(p => p.ParameterType != typeof(CancellationToken)).ToList();
        var used = 0;

        // A single request object takes the whole input.
        if (data.Count == 1 && !given.ContainsKey(data[0].Name!) && IsObject(data[0].ParameterType))
            (args[data[0].Position], used) = (json.Deserialize(data[0].ParameterType, Json), given.Count);
        else
            foreach (var p in parameters)
                if (p.ParameterType == typeof(CancellationToken)) args[p.Position] = cancellationToken;
                else if (given.TryGetValue(p.Name!, out var value)) (args[p.Position], used) = (Convert(value, p.ParameterType), used + 1);
                else if (p.HasDefaultValue) args[p.Position] = p.DefaultValue is DBNull ? null : p.DefaultValue;
                else return null;

        return used == given.Count ? args : null;
    }

    private static object? Convert(JsonNode? node, Type type) =>
        type == typeof(string) && node is JsonValue value && !value.TryGetValue<string>(out _)
            ? node.ToJsonString()
            : node.Deserialize(type, Json);

    private static bool IsObject(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsArray && !typeof(IEnumerable).IsAssignableFrom(type);

    internal static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
