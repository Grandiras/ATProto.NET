using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Crypto;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Server;

// ── Test Endpoint Handlers ────────────────────────────────────
// Every non-generic handler here is also picked up by XrpcAssemblyScanTests, so each has its own
// NSID. The deliberately invalid ones are generic type definitions, which scanning skips.

public sealed class HealthQueryParams
{
    [JsonPropertyName("verbose")]
    public string? Verbose { get; init; }
}

public sealed class HealthQueryOutput
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed class HealthCheckQuery : IXrpcQuery<HealthQueryParams, HealthQueryOutput>
{
    // An auto-property initializer: the form the old uninitialized-object lookup read as null.
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.healthCheck");

    public Task<HealthQueryOutput> HandleAsync(HealthQueryParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        var output = new HealthQueryOutput
        {
            Status = "ok",
            Version = parameters.Verbose == "true" ? "1.0.0" : null,
        };
        return Task.FromResult(output);
    }
}

public sealed class SimpleQueryOutput
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public sealed class SimpleQuery : IXrpcQuery<SimpleQueryOutput>
{
    public static Nsid Nsid => Nsid.Parse("com.example.simpleQuery");

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SimpleQueryOutput { Message = "hello" });
    }
}

public sealed class EchoInput
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed class EchoOutput
{
    [JsonPropertyName("echo")]
    public required string Echo { get; init; }
}

public sealed class EchoProcedure : IXrpcProcedure<EchoInput, EchoOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.echo");

    public Task<EchoOutput> HandleAsync(EchoInput input, HttpContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new EchoOutput { Echo = input.Text });
    }
}

public sealed class PingInput
{
    [JsonPropertyName("target")]
    public required string Target { get; init; }
}

public sealed class PingProcedure : IXrpcProcedureVoid<PingInput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.ping");

    public Task HandleAsync(PingInput input, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers["X-Ping-Target"] = input.Target;
        return Task.CompletedTask;
    }
}

public sealed class NoInputProcedure : IXrpcProcedure<EchoOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.noInput");

    public Task<EchoOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoOutput { Echo = "no input" });
}

public sealed class NoInputVoidProcedure : IXrpcProcedureVoid
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.noInputVoid");

    public Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers["X-Handled"] = "yes";
        return Task.CompletedTask;
    }
}

public sealed class UploadOutput
{
    [JsonPropertyName("size")]
    public required long Size { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("contentType")]
    public required string ContentType { get; init; }

    [JsonPropertyName("contentLength")]
    public long? ContentLength { get; init; }
}

public sealed class UploadProcedure : IXrpcBlobProcedure<UploadOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.upload");

    public async Task<UploadOutput> HandleAsync(XrpcBlobInput input, HttpContext context, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await input.Content.CopyToAsync(buffer, cancellationToken);

        return new UploadOutput
        {
            Size = buffer.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())),
            ContentType = input.ContentType,
            ContentLength = input.ContentLength,
        };
    }
}

public sealed class DownloadParams
{
    [JsonPropertyName("size")]
    public int Size { get; init; }
}

public sealed class DownloadQuery : IXrpcBlobQuery<DownloadParams>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.download");

    public Task<XrpcBlobResult> HandleAsync(DownloadParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        if (parameters.Size < 0)
            throw new XrpcException(XrpcErrors.InvalidRequest, "size must not be negative");

        var bytes = Enumerable.Range(0, parameters.Size).Select(i => (byte)i).ToArray();
        return Task.FromResult(new XrpcBlobResult(new MemoryStream(bytes), "application/octet-stream", bytes.Length));
    }
}

public sealed class FailParams
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

public sealed class FailingQuery : IXrpcQuery<FailParams, SimpleQueryOutput>
{
    public const string SecretMessage = "Server=db.internal;Password=hunter2";

    public static Nsid Nsid { get; } = Nsid.Parse("com.example.fail");

    public Task<SimpleQueryOutput> HandleAsync(FailParams parameters, HttpContext context, CancellationToken cancellationToken) =>
        parameters.Kind switch
        {
            "xrpc" => throw new XrpcException(XrpcErrors.RecordNotFound, "No such record.", HttpStatusCode.NotFound)
            {
                Headers = { ["X-Error-Detail"] = "detail" },
            },
            "tooLarge" => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            "null" => Task.FromResult<SimpleQueryOutput>(null!),
            _ => throw new InvalidOperationException(SecretMessage),
        };
}

/// <summary>Implements two endpoint interfaces, which a handler for one method never needs.</summary>
public sealed class TwoShapeHandler<T> : IXrpcQuery<SimpleQueryOutput>, IXrpcProcedure<SimpleQueryOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.twoShapes");

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new SimpleQueryOutput { Message = typeof(T).Name });
}

public sealed class NullNsidHandler<T> : IXrpcQuery<SimpleQueryOutput>
{
    public static Nsid Nsid => null!;

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new SimpleQueryOutput { Message = typeof(T).Name });
}

/// <summary>Claims <see cref="SimpleQuery"/>'s NSID.</summary>
public sealed class ConflictingHandler<T> : IXrpcProcedure<SimpleQueryOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.simpleQuery");

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new SimpleQueryOutput { Message = typeof(T).Name });
}

/// <summary>Claims <see cref="SimpleQuery"/>'s NSID in another case, which routing treats as the same path.</summary>
public sealed class CaseVariantHandler<T> : IXrpcProcedure<SimpleQueryOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.SimpleQuery");

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new SimpleQueryOutput { Message = typeof(T).Name });
}

// ── Test infrastructure ───────────────────────────────────────

/// <summary>A test server hosting whatever XRPC endpoints the test registers.</summary>
internal sealed class XrpcTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private XrpcTestHost(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public static async Task<XrpcTestHost> StartAsync(
        Action<IServiceCollection> configureServices,
        Action<RouteGroupBuilder>? configureGroup = null,
        Action<IApplicationBuilder>? configureMiddleware = null,
        Action<IEndpointRouteBuilder>? configureEndpoints = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    configureServices(services);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    configureMiddleware?.Invoke(app);
                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapXrpcEndpoints();
                        configureGroup?.Invoke(group);
                        configureEndpoints?.Invoke(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new XrpcTestHost(host);
    }

    /// <summary>Reads an XRPC error envelope, asserting that the response is one.</summary>
    public static async Task<(string Error, string Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            document.RootElement.GetProperty("error").GetString()!,
            document.RootElement.GetProperty("message").GetString()!);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((category, logLevel, formatter(state, exception), exception));
    }
}

// ── Tests ─────────────────────────────────────────────────────

public class XrpcEndpointTests : IAsyncLifetime
{
    private readonly CapturingLoggerProvider _logs = new();
    private XrpcTestHost? _host;

    private HttpClient Client => _host!.Client;

    public async ValueTask InitializeAsync()
    {
        _host = await XrpcTestHost.StartAsync(services =>
        {
            services.AddLogging(logging => logging.AddProvider(_logs));
            services.AddXrpcEndpoint<HealthCheckQuery>();
            services.AddXrpcEndpoint<SimpleQuery>();
            services.AddXrpcEndpoint<EchoProcedure>();
            services.AddXrpcEndpoint<PingProcedure>();
            services.AddXrpcEndpoint<NoInputProcedure>();
            services.AddXrpcEndpoint<NoInputVoidProcedure>();
            services.AddXrpcEndpoint<UploadProcedure>();
            services.AddXrpcEndpoint<DownloadQuery>();
            services.AddXrpcEndpoint<FailingQuery>();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task QueryWithParams_ReturnsCorrectResponse()
    {
        var response = await Client.GetAsync("/xrpc/com.example.healthCheck?verbose=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HealthQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public async Task QueryWithParams_NoParams_StillWorks()
    {
        var response = await Client.GetAsync("/xrpc/com.example.healthCheck");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HealthQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task SimpleQuery_ReturnsOutput()
    {
        var response = await Client.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var result = await response.Content.ReadFromJsonAsync<SimpleQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("hello", result.Message);
    }

    [Fact]
    public async Task Procedure_EchoesInput()
    {
        var response = await Client.PostAsJsonAsync("/xrpc/com.example.echo", new EchoInput { Text = "test123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EchoOutput>();
        Assert.NotNull(result);
        Assert.Equal("test123", result.Echo);
    }

    [Fact]
    public async Task ProcedureVoid_Returns200WithNoBody()
    {
        var response = await Client.PostAsJsonAsync("/xrpc/com.example.ping", new PingInput { Target = "bsky.social" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bsky.social", Assert.Single(response.Headers.GetValues("X-Ping-Target")));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // ── Unmatched routes ──────────────────────────────────────

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task UnknownNsid_ReturnsMethodNotImplemented(string method)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/xrpc/com.example.doesNotExist"));

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.MethodNotImplemented, error);
        Assert.Equal("Method Not Implemented", message);
    }

    [Fact]
    public async Task QueryEndpoint_PostMethod_Returns405WithAllowAndEnvelope()
    {
        var response = await Client.PostAsync("/xrpc/com.example.simpleQuery", null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET"], response.Content.Headers.Allow);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("Incorrect HTTP method (POST), expected GET.", message);
    }

    [Fact]
    public async Task ProcedureEndpoint_GetMethod_Returns405WithAllowAndEnvelope()
    {
        var response = await Client.GetAsync("/xrpc/com.example.echo");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("Incorrect HTTP method (GET), expected POST.", message);
    }

    [Fact]
    public async Task PathSegmentThatIsNotAnNsid_ReturnsInvalidRequest()
    {
        var response = await Client.GetAsync("/xrpc/notAnNsid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, _) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
    }

    [Theory]
    [InlineData("/other/com.example.doesNotExist")]
    [InlineData("/com.example.doesNotExist")]
    [InlineData("/xrpc/com.example.doesNotExist/extra")]
    public async Task PathOutsideXrpcNsidRoutes_IsLeftTo404(string path)
    {
        var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Procedure bodies ──────────────────────────────────────

    [Fact]
    public async Task Procedure_NoBody_ReturnsInvalidRequest()
    {
        var response = await Client.PostAsync("/xrpc/com.example.echo", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("Content-Type", message);
    }

    [Fact]
    public async Task Procedure_NonJsonContentType_ReturnsInvalidRequest()
    {
        var response = await Client.PostAsync(
            "/xrpc/com.example.echo", new StringContent("""{"text":"x"}""", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("text/plain", message);
    }

    [Fact]
    public async Task Procedure_MalformedJson_ReturnsInvalidRequest()
    {
        var response = await Client.PostAsync(
            "/xrpc/com.example.echo", new StringContent("""{"text":""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, _) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
    }

    [Fact]
    public async Task Procedure_WrongFieldType_ReturnsInvalidRequestNamingTheField()
    {
        var response = await Client.PostAsync(
            "/xrpc/com.example.echo", new StringContent("""{"text":{"nested":true}}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("$.text", message);
        Assert.DoesNotContain("System.", message);
    }

    [Fact]
    public async Task ProcedureWithoutInput_NoBody_ReturnsOutput()
    {
        var response = await Client.PostAsync("/xrpc/com.example.noInput", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EchoOutput>();
        Assert.Equal("no input", result?.Echo);
    }

    [Fact]
    public async Task ProcedureVoidWithoutInput_NoBody_Returns200WithNoBody()
    {
        var response = await Client.PostAsync("/xrpc/com.example.noInputVoid", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("yes", Assert.Single(response.Headers.GetValues("X-Handled")));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BlobProcedure_PassesBodyContentTypeAndLength()
    {
        var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7)).ToArray();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await Client.PostAsync("/xrpc/com.example.upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadOutput>();
        Assert.NotNull(result);
        Assert.Equal(bytes.Length, result.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(bytes.Length, result.ContentLength);
    }

    [Fact]
    public async Task BlobProcedure_NoContentType_ReturnsInvalidRequest()
    {
        var response = await Client.PostAsync("/xrpc/com.example.upload", new ByteArrayContent([1, 2, 3]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("Content-Type", message);
    }

    [Fact]
    public async Task BlobQuery_StreamsBodyWithContentTypeAndLength()
    {
        var response = await Client.GetAsync("/xrpc/com.example.download?size=100000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(100_000, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Enumerable.Range(0, 100_000).Select(i => (byte)i), body);
    }

    [Fact]
    public async Task BlobQuery_HandlerThrows_ReturnsJsonEnvelope()
    {
        var response = await Client.GetAsync("/xrpc/com.example.download?size=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("size must not be negative", message);
    }

    // ── Error envelope ────────────────────────────────────────

    [Fact]
    public async Task HandlerThrowsXrpcException_WritesItsStatusErrorAndHeaders()
    {
        var response = await Client.GetAsync("/xrpc/com.example.fail?kind=xrpc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("detail", Assert.Single(response.Headers.GetValues("X-Error-Detail")));
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.RecordNotFound, error);
        Assert.Equal("No such record.", message);
    }

    [Fact]
    public async Task HandlerThrowsUnknownException_ReturnsInternalServerErrorWithoutItsMessage()
    {
        var response = await Client.GetAsync("/xrpc/com.example.fail?kind=crash");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", body);

        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InternalServerError, error);
        Assert.Equal("Internal Server Error", message);
    }

    [Fact]
    public async Task HandlerThrowsUnknownException_IsLoggedWithTheException()
    {
        await Client.GetAsync("/xrpc/com.example.fail?kind=crash");

        var entry = Assert.Single(_logs.Entries, e => e.Category == XrpcErrorResponse.LoggerCategory);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("com.example.fail", entry.Message);
        Assert.Equal(FailingQuery.SecretMessage, Assert.IsType<InvalidOperationException>(entry.Exception).Message);
    }

    [Fact]
    public async Task HandlerThrowsXrpcException_IsNotLoggedAsAFailure()
    {
        await Client.GetAsync("/xrpc/com.example.fail?kind=xrpc");

        Assert.DoesNotContain(_logs.Entries, e => e.Category == XrpcErrorResponse.LoggerCategory);
    }

    [Fact]
    public async Task BadHttpRequest413_ReturnsPayloadTooLarge()
    {
        var response = await Client.GetAsync("/xrpc/com.example.fail?kind=tooLarge");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var (error, _) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.PayloadTooLarge, error);
    }

    [Fact]
    public async Task HandlerReturnsNull_ReturnsInternalServerError()
    {
        var response = await Client.GetAsync("/xrpc/com.example.fail?kind=null");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var (error, _) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InternalServerError, error);
    }

    [Fact]
    public async Task Handle_AnyDelegate_MapsItsExceptionToTheEnvelopeAndLogsIt()
    {
        // The same wrapper guards the group's unmatched-route fallback, which has no registration.
        var logs = new CapturingLoggerProvider();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/xrpc/com.example.anything";

        var handle = XrpcErrorResponse.Handle(
            _ => throw new InvalidOperationException(FailingQuery.SecretMessage),
            logs.CreateLogger(XrpcErrorResponse.LoggerCategory));
        await handle(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("\"InternalServerError\"", body);
        Assert.DoesNotContain("hunter2", body);

        var entry = Assert.Single(logs.Entries);
        Assert.Contains("GET /xrpc/com.example.anything", entry.Message);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }
}

public class XrpcEndpointRegistrationTests
{
    [Fact]
    public void AddXrpcEndpoint_AutoPropertyNsid_IsRead()
    {
        var registration = Assert.Single(Registry(new ServiceCollection().AddXrpcEndpoint<HealthCheckQuery>()).Registrations);

        Assert.Equal("com.example.healthCheck", registration.Nsid.Value);
        Assert.Equal(HttpMethods.Get, registration.HttpMethod);
    }

    [Fact]
    public void AddXrpcEndpoint_EachShape_MapsToItsHttpMethod()
    {
        var services = new ServiceCollection()
            .AddXrpcEndpoint<SimpleQuery>()
            .AddXrpcEndpoint<DownloadQuery>()
            .AddXrpcEndpoint<EchoProcedure>()
            .AddXrpcEndpoint<NoInputProcedure>()
            .AddXrpcEndpoint<PingProcedure>()
            .AddXrpcEndpoint<NoInputVoidProcedure>()
            .AddXrpcEndpoint<UploadProcedure>();

        var methods = Registry(services).Registrations.ToDictionary(r => r.HandlerType, r => r.HttpMethod);

        Assert.Equal(HttpMethods.Get, methods[typeof(SimpleQuery)]);
        Assert.Equal(HttpMethods.Get, methods[typeof(DownloadQuery)]);
        Assert.Equal(HttpMethods.Post, methods[typeof(EchoProcedure)]);
        Assert.Equal(HttpMethods.Post, methods[typeof(NoInputProcedure)]);
        Assert.Equal(HttpMethods.Post, methods[typeof(PingProcedure)]);
        Assert.Equal(HttpMethods.Post, methods[typeof(NoInputVoidProcedure)]);
        Assert.Equal(HttpMethods.Post, methods[typeof(UploadProcedure)]);
    }

    [Fact]
    public void AddXrpcEndpoint_SameHandlerTwice_RegistersOnce()
    {
        var services = new ServiceCollection()
            .AddXrpcEndpoint<SimpleQuery>()
            .AddXrpcEndpoint<SimpleQuery>();

        Assert.Single(Registry(services).Registrations);
        Assert.Single(services, d => d.ServiceType == typeof(SimpleQuery));
    }

    [Fact]
    public void AddXrpcEndpoint_HandlerImplementingTwoShapes_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddXrpcEndpoint<TwoShapeHandler<int>>());

        Assert.Contains("one endpoint interface", ex.Message);
    }

    [Fact]
    public void AddXrpcEndpoint_NullNsid_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddXrpcEndpoint<NullNsidHandler<int>>());

        Assert.Contains("null Nsid", ex.Message);
    }

    [Fact]
    public void AddXrpcEndpoint_SecondHandlerForSameNsid_Throws()
    {
        var services = new ServiceCollection().AddXrpcEndpoint<SimpleQuery>();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddXrpcEndpoint<ConflictingHandler<int>>());

        Assert.Contains("com.example.simpleQuery", ex.Message);
    }

    [Fact]
    public void AddXrpcEndpoint_NsidDifferingOnlyInCase_ThrowsLikeAnyConflict()
    {
        var services = new ServiceCollection().AddXrpcEndpoint<SimpleQuery>();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddXrpcEndpoint<CaseVariantHandler<int>>());

        Assert.Contains("com.example.simpleQuery", ex.Message);
        Assert.Contains("com.example.SimpleQuery", ex.Message);
        Assert.Single(Registry(services).Registrations);
    }

    [Fact]
    public void AddXrpcEndpointsFromAssembly_RegistersWhatAddXrpcEndpointRegisters()
    {
        var scanned = Registry(new ServiceCollection().AddXrpcEndpointsFromAssembly(typeof(HealthCheckQuery).Assembly))
            .Registrations.Single(r => r.HandlerType == typeof(HealthCheckQuery));
        var added = Assert.Single(Registry(new ServiceCollection().AddXrpcEndpoint<HealthCheckQuery>()).Registrations);

        Assert.Equal(added.Nsid, scanned.Nsid);
        Assert.Equal(added.HttpMethod, scanned.HttpMethod);
    }

    [Fact]
    public void AddXrpcEndpointsFromAssembly_ServerAssembly_AgreesWithTheSpaceServerRegistrations()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var services = new ServiceCollection();
        services
            .AddAtProtoSpaces()
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(key)
            .AddSimpleSpace<InMemorySimpleSpaceStore>()
            .AddSpaceRepoHost<ISpaceRepoHost>();

        // The labeler's queryLabels is the one endpoint outside the space server.
        services.AddXrpcEndpoint<ATProtoNet.Server.Labeling.QueryLabelsEndpoint>();

        var registered = Registry(services).Registrations;
        var scanned = Registry(new ServiceCollection().AddXrpcEndpointsFromAssembly(typeof(SpaceNsids).Assembly))
            .Registrations;

        Assert.Equal(20, registered.Count);
        Assert.Equal(Describe(registered), Describe(scanned));
    }

    [Fact]
    public void AddXrpcEndpointsFromAssembly_SkipsOpenGenericHandlers()
    {
        var registrations = Registry(new ServiceCollection().AddXrpcEndpointsFromAssembly(typeof(HealthCheckQuery).Assembly))
            .Registrations;

        Assert.DoesNotContain(registrations, r => r.HandlerType.IsGenericType);
    }

    [Fact]
    public void MapXrpcEndpoints_NothingRegistered_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapXrpcEndpoints());

        Assert.Contains(nameof(XrpcEndpointExtensions.AddXrpcEndpoint), ex.Message);
    }

    private static IEnumerable<string> Describe(IEnumerable<XrpcEndpointRegistration> registrations) =>
        registrations.Select(r => $"{r.HttpMethod} {r.Nsid} {r.HandlerType.Name}").Order(StringComparer.Ordinal);

    private static XrpcEndpointRegistry Registry(IServiceCollection services) =>
        (XrpcEndpointRegistry)services.Single(d => d.ServiceType == typeof(XrpcEndpointRegistry)).ImplementationInstance!;
}

public class XrpcAssemblyScanTests : IAsyncLifetime
{
    private XrpcTestHost? _host;

    public async ValueTask InitializeAsync()
    {
        _host = await XrpcTestHost.StartAsync(services =>
            services.AddXrpcEndpointsFromAssembly(typeof(HealthCheckQuery).Assembly));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task AssemblyScannedEndpoints_AreReachable()
    {
        var response = await _host!.Client.GetAsync("/xrpc/com.example.healthCheck");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AssemblyScannedProcedure_Works()
    {
        var response = await _host!.Client.PostAsJsonAsync("/xrpc/com.example.echo", new EchoInput { Text = "scanned" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EchoOutput>();
        Assert.NotNull(result);
        Assert.Equal("scanned", result.Echo);
    }
}
