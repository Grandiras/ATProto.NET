using System.Net;
using System.Text;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Http;

public class XrpcExceptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_SetsProperties()
    {
        var ex = new XrpcException("InvalidRequest", "Bad input", HttpStatusCode.BadRequest, "com.example.do")
        {
            ResponseBody = "{\"error\":\"InvalidRequest\"}",
        };

        Assert.Equal("InvalidRequest", ex.Error);
        Assert.Equal("Bad input", ex.ErrorMessage);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("com.example.do", ex.Nsid);
        Assert.Equal("{\"error\":\"InvalidRequest\"}", ex.ResponseBody);
        Assert.Equal("com.example.do failed with 400 InvalidRequest: Bad input", ex.Message);
    }

    [Fact]
    public void Constructor_ServerSide_DefaultsTo400WithoutNsid()
    {
        var ex = new XrpcException(XrpcErrors.RecordNotFound);

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Null(ex.Nsid);
        Assert.Null(ex.ErrorMessage);
        Assert.Empty(ex.Headers);
    }

    [Fact]
    public void Constructor_WithInnerException_KeepsIt()
    {
        var inner = new FormatException("bad");

        var ex = new XrpcException(XrpcErrors.InvalidRequest, "wrapped", inner, HttpStatusCode.UnprocessableEntity);

        Assert.Same(inner, ex.InnerException);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithoutAnErrorName_Throws(string error)
    {
        Assert.Throws<ArgumentException>(() => new XrpcException(error));
    }

    [Fact]
    public void Is_MatchesTheErrorNameExactly()
    {
        var ex = new XrpcException(XrpcErrors.RecordNotFound);

        Assert.True(ex.Is(XrpcErrors.RecordNotFound));
        Assert.False(ex.Is("recordnotfound"));
        Assert.False(ex.Is(XrpcErrors.InvalidRequest));
    }

    [Fact]
    public void XrpcException_IsAnAtProtoExceptionButNotAnHttpRequestException()
    {
        var ex = new XrpcException(XrpcErrors.InvalidRequest);

        Assert.IsAssignableFrom<AtProtoException>(ex);
        Assert.False(typeof(HttpRequestException).IsAssignableFrom(ex.GetType()));
    }

    [Fact]
    public void SdkExceptions_ShareTheAtProtoExceptionBase()
    {
        var exceptions = new Exception[]
        {
            new XrpcRateLimitException(XrpcErrors.RateLimitExceeded, null, "x.y.z", null, null),
            new XrpcAuthenticationException(XrpcErrors.ExpiredToken, null, HttpStatusCode.BadRequest, "x.y.z"),
            new XrpcResponseFormatException("x.y.z", "bad"),
            new OAuthException("bad", "invalid_grant"),
            new SpaceCredentialException("bad"),
            new SpaceTokenException("bad"),
            new SpaceRepoVerificationException("bad"),
            new JetstreamException("bad"),
            new JetstreamException("bad"),
            new SpaceVerificationException("NotAuthorized", "bad"),
        };

        Assert.All(exceptions, ex => Assert.IsAssignableFrom<AtProtoException>(ex));
    }

    [Fact]
    public void SpaceVerificationException_IsTheCoreXrpcException()
    {
        var ex = new SpaceVerificationException("InvalidDelegationToken", "expired");

        Assert.IsAssignableFrom<XrpcException>(ex);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    // ──────────────────────────────────────────────────────────
    //  Mapping a failed response
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateException_FromAnErrorEnvelope_CarriesItsFields()
    {
        using var response = Response(HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""");
        response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev", "3l6ov0");

        var ex = Create(response, "com.atproto.repo.getRecord");

        Assert.IsType<XrpcException>(ex);
        Assert.True(ex.Is(XrpcErrors.RecordNotFound));
        Assert.Equal("Could not locate record", ex.ErrorMessage);
        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
        Assert.Contains("RecordNotFound", ex.ResponseBody);
        Assert.Equal("3l6ov0", ex.Headers["atproto-repo-rev"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, XrpcErrors.UpstreamFailure)]
    [InlineData(HttpStatusCode.InternalServerError, XrpcErrors.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound, XrpcErrors.XrpcNotSupported)]
    [InlineData((HttpStatusCode)418, XrpcErrors.Unknown)]
    public void CreateException_WithoutAnEnvelope_NamesTheStatus(HttpStatusCode status, string expected)
    {
        using var response = Response(status, "<html><body>Bad Gateway</body></html>", "text/html");

        var ex = Create(response, "com.example.do");

        Assert.Equal(expected, ex.Error);
        Assert.Null(ex.ErrorMessage);
        Assert.Equal("<html><body>Bad Gateway</body></html>", ex.ResponseBody);
    }

    [Theory]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""{"error":42}""")]
    [InlineData("")]
    public void CreateException_WithAMalformedEnvelope_NamesTheStatus(string body)
    {
        using var response = Response(HttpStatusCode.BadRequest, body);

        Assert.Equal(XrpcErrors.InvalidRequest, Create(response, "com.example.do").Error);
    }

    [Fact]
    public void CreateException_For401_IsAnAuthenticationException()
    {
        using var response = Response(HttpStatusCode.Unauthorized, """{"error":"AuthMissing"}""");
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", "DPoP error=\"invalid_token\"");

        var ex = Assert.IsType<XrpcAuthenticationException>(Create(response, "com.example.do"));

        Assert.Equal(XrpcErrors.AuthMissing, ex.Error);
        Assert.Equal("DPoP error=\"invalid_token\"", ex.Headers["WWW-Authenticate"]);
    }

    [Theory]
    [InlineData(XrpcErrors.ExpiredToken)]
    [InlineData(XrpcErrors.InvalidToken)]
    public void CreateException_ForATokenErrorOn400_IsAnAuthenticationException(string error)
    {
        // A PDS answers a stale access token with a 400, not a 401.
        using var response = Response(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","message":"Token has expired"}""");

        var ex = Assert.IsType<XrpcAuthenticationException>(Create(response, "com.example.do"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public void CreateException_For429_CarriesRetryAfterAndRateLimit()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, """{"error":"RateLimitExceeded"}""");
        response.Headers.TryAddWithoutValidation("Retry-After", "120");
        response.Headers.TryAddWithoutValidation("RateLimit-Limit", "30");
        response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("RateLimit-Reset", Now.AddHours(1).ToUnixTimeSeconds().ToString());

        var ex = Assert.IsType<XrpcRateLimitException>(Create(response, "com.atproto.server.createSession"));

        Assert.Equal(TimeSpan.FromSeconds(120), ex.RetryAfter);
        Assert.Equal(30, ex.RateLimit?.Limit);
        Assert.True(ex.RateLimit?.IsExceeded);
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }

    [Fact]
    public void GetRequestedDelay_ParsesAnHttpDate()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, "");
        response.Headers.TryAddWithoutValidation("Retry-After", Now.AddMinutes(5).ToString("R"));

        Assert.Equal(TimeSpan.FromMinutes(5), XrpcResponseReader.GetRequestedDelay(response, Now));
    }

    [Fact]
    public void GetRequestedDelay_FallsBackToRateLimitReset()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, "");
        response.Headers.TryAddWithoutValidation("RateLimit-Reset", Now.AddSeconds(42).ToUnixTimeSeconds().ToString());

        Assert.Equal(TimeSpan.FromSeconds(42), XrpcResponseReader.GetRequestedDelay(response, Now));
    }

    [Fact]
    public void GetRequestedDelay_InThePast_IsZero()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, "");
        response.Headers.TryAddWithoutValidation("Retry-After", Now.AddMinutes(-5).ToString("R"));

        Assert.Equal(TimeSpan.Zero, XrpcResponseReader.GetRequestedDelay(response, Now));
    }

    [Fact]
    public void GetRequestedDelay_WithoutHeaders_IsNull()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, "");

        Assert.Null(XrpcResponseReader.GetRequestedDelay(response, Now));
    }

    [Fact]
    public async Task ReadErrorAsync_ReadsAnEnvelope()
    {
        using var response = Response(HttpStatusCode.BadRequest, """{"error":"InvalidSwap","message":"stale"}""");

        var body = await XrpcResponseReader.ReadErrorAsync(response, CancellationToken.None);

        Assert.Equal(("InvalidSwap", "stale"), (body.Error, body.Message));
    }

    [Fact]
    public async Task ReadErrorAsync_DoesNotReadAnOversizedBody()
    {
        // The service picks the size of its error page; past the cap it is not an envelope and
        // is not buffered. The status still names the error.
        var huge = "{\"error\":\"InvalidRequest\",\"message\":\"" + new string('x', XrpcResponseReader.MaxErrorBodyBytes) + "\"}";
        using var response = Response(HttpStatusCode.BadRequest, huge);

        var body = await XrpcResponseReader.ReadErrorAsync(response, CancellationToken.None);
        var ex = XrpcResponseReader.CreateException(response, "com.example.do", body, Now);

        Assert.Equal(default, body);
        Assert.Equal(XrpcErrors.InvalidRequest, ex.Error);
        Assert.Null(ex.ResponseBody);
    }

    private static XrpcException Create(HttpResponseMessage response, string nsid) =>
        XrpcResponseReader.CreateException(
            response,
            nsid,
            XrpcResponseReader.ParseError(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()),
            Now);

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
