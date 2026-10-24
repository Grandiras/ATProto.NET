// Stand-ins for the XRPC, client and Auth types phase 5 and 6 build (design/specs/xrpc-client.md, auth.md), with the shapes
// generated code uses: descriptor constructors with named arguments, the client's invocation methods, pagination and
// permission-set scopes. They exist so phase 3 can compile every generated family; the real types replace them.
using System.Text.Json.Serialization.Metadata;
using ATProtoNet.Lexicon.Runtime;

namespace ATProtoNet.Xrpc
{
    public abstract class XrpcMethod(string nsid, string? inputEncoding, string? outputEncoding, IReadOnlyList<string>? errors, string? defaultProxy)
    {
        public Nsid Nsid { get; } = Nsid.Parse(nsid);
        public string? InputEncoding { get; } = inputEncoding;
        public string? OutputEncoding { get; } = outputEncoding;
        public IReadOnlyList<string> Errors { get; } = errors ?? [];
        public string? DefaultProxy { get; } = defaultProxy;
    }

    public sealed class XrpcQuery<TParams, TOutput>(string nsid, string? outputEncoding = null, JsonTypeInfo<TOutput>? output = null, IReadOnlyList<string>? errors = null, string? defaultProxy = null)
        : XrpcMethod(nsid, null, outputEncoding, errors, defaultProxy) where TParams : IXrpcParameters<TParams>
    {
        public JsonTypeInfo<TOutput>? Output { get; } = output;
    }

    public sealed class XrpcProcedure<TInput, TOutput>(string nsid, string? inputEncoding = null, JsonTypeInfo<TInput>? input = null, string? outputEncoding = null,
        JsonTypeInfo<TOutput>? output = null, IReadOnlyList<string>? errors = null, string? defaultProxy = null)
        : XrpcMethod(nsid, inputEncoding, outputEncoding, errors, defaultProxy)
    {
        public JsonTypeInfo<TInput>? Input { get; } = input;
        public JsonTypeInfo<TOutput>? Output { get; } = output;
    }

    public sealed class XrpcProcedure<TInput>(string nsid, string? inputEncoding = null, JsonTypeInfo<TInput>? input = null, IReadOnlyList<string>? errors = null, string? defaultProxy = null)
        : XrpcMethod(nsid, inputEncoding, null, errors, defaultProxy)
    {
        public JsonTypeInfo<TInput>? Input { get; } = input;
    }

    public sealed class XrpcProcedure<TParams, TInput, TOutput>(string nsid, string? inputEncoding = null, JsonTypeInfo<TInput>? input = null, string? outputEncoding = null,
        JsonTypeInfo<TOutput>? output = null, IReadOnlyList<string>? errors = null, string? defaultProxy = null)
        : XrpcMethod(nsid, inputEncoding, outputEncoding, errors, defaultProxy) where TParams : IXrpcParameters<TParams>
    {
        public JsonTypeInfo<TInput>? Input { get; } = input;
        public JsonTypeInfo<TOutput>? Output { get; } = output;
    }

    public sealed class XrpcSubscription<TParams, TMessage>(string nsid, JsonTypeInfo<TMessage>? message = null, IReadOnlyList<string>? errors = null)
        : XrpcMethod(nsid, null, null, errors, null) where TParams : IXrpcParameters<TParams>
    {
        public JsonTypeInfo<TMessage>? Message { get; } = message;
    }

    public readonly struct XrpcEmpty : IXrpcParameters<XrpcEmpty>
    {
        public void WriteTo(XrpcParameterWriter writer) { }
        public static XrpcEmpty ReadFrom(XrpcParameterReader reader) => default;
    }

    public sealed record XrpcBinaryContent(Stream Content, string ContentType, long? ContentLength = null);

    public sealed class XrpcBinaryResponse;

    public sealed record XrpcServiceRef(Did Did, string ServiceId);

    public static class Pagination
    {
        public static async IAsyncEnumerable<T> EnumerateAsync<TPage, T>(Func<string?, CancellationToken, Task<TPage>> fetchPage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where TPage : ICursorPage<T>
        {
            string? cursor = null;
            do
            {
                var page = await fetchPage(cursor, cancellationToken);
                foreach (var item in page.Items) yield return item;
                cursor = page.Cursor;
            }
            while (cursor is not null);
        }
    }
}

namespace ATProtoNet
{
    using ATProtoNet.Xrpc;

    // Records each call, so tests can check what generated namespace clients send.
    public sealed partial class AtProtoClient(Func<XrpcMethod, object?, object?, object?>? respond = null)
    {
        public List<(XrpcMethod Method, object? Parameters, object? Input)> Calls { get; } = [];

        public Task<TOutput> QueryAsync<TParams, TOutput>(XrpcQuery<TParams, TOutput> method, TParams parameters, CancellationToken cancellationToken = default)
            where TParams : IXrpcParameters<TParams> => Task.FromResult((TOutput)Call(method, parameters, null)!);

        public Task<TOutput> ProcedureAsync<TInput, TOutput>(XrpcProcedure<TInput, TOutput> method, TInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult((TOutput)Call(method, null, input)!);

        public Task ProcedureAsync<TInput>(XrpcProcedure<TInput> method, TInput input, CancellationToken cancellationToken = default)
        {
            Call(method, null, input);
            return Task.CompletedTask;
        }

        public Task<TOutput> ProcedureAsync<TParams, TInput, TOutput>(XrpcProcedure<TParams, TInput, TOutput> method, TParams parameters, TInput input, CancellationToken cancellationToken = default)
            where TParams : IXrpcParameters<TParams> => Task.FromResult((TOutput)Call(method, parameters, input)!);

        private object? Call(XrpcMethod method, object? parameters, object? input)
        {
            Calls.Add((method, parameters, input));
            return respond?.Invoke(method, parameters, input);
        }
    }
}

namespace ATProtoNet.Auth
{
    using ATProtoNet.Xrpc;

    public sealed record Scope(string Value);

    public sealed record PermissionSetRef(Nsid Nsid)
    {
        public Scope Include(XrpcServiceRef? audience = null) =>
            new($"include:{Nsid}{(audience is null ? "" : $"?aud={audience.Did}%23{audience.ServiceId}")}");
    }
}
