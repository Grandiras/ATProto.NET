using System.Text;
using System.Text.Encodings.Web;
using ATProtoNet.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ATProtoNet.Tests.Server;

/// <summary>
/// The local JWT pre-checks of <see cref="AtProtoAuthenticationHandler"/>. Every case here fails
/// before the handler would call the PDS, so none of them touches the network.
/// </summary>
public class AtProtoAuthenticationHandlerTests
{
    private static readonly object ExpiredClaims = new Dictionary<string, object> { ["exp"] = 1 };
    private static readonly object Header = new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT" };

    private static string Token(JwsSegments padded = JwsSegments.None) =>
        TestJws.Mint(Header, ExpiredClaims, _ => new byte[64], padded);

    private static async Task<AuthenticateResult> AuthenticateAsync(string token)
    {
        var monitor = Substitute.For<IOptionsMonitor<AtProtoAuthenticationOptions>>();
        monitor.Get(Arg.Any<string>()).Returns(new AtProtoAuthenticationOptions());

        await using var client = new AtProtoClient();
        var handler = new AtProtoAuthenticationHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, client);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";

        await handler.InitializeAsync(
            new AuthenticationScheme("ATProto", null, typeof(AtProtoAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Theory]
    [InlineData(JwsSegments.None)]
    [InlineData(JwsSegments.Header)]
    [InlineData(JwsSegments.Payload)]
    [InlineData(JwsSegments.All)]
    public async Task AuthenticateAsync_ExpiredToken_IsRejectedWhetherOrNotItIsPadded(JwsSegments padded)
    {
        // Reaching the expiry check means both segments decoded.
        var result = await AuthenticateAsync(Token(padded));

        Assert.False(result.Succeeded);
        Assert.Equal("Token is expired.", result.Failure?.Message);
    }

    [Theory]
    [InlineData(0, "!!!!")]
    [InlineData(1, "!!!!")]
    [InlineData(0, "AAAAA")]
    [InlineData(1, "AAAAA")]
    public async Task AuthenticateAsync_SegmentThatIsNotBase64Url_IsRejected(int index, string segment)
    {
        var result = await AuthenticateAsync(TestJws.WithSegment(Token(), index, segment));

        Assert.False(result.Succeeded);
        Assert.StartsWith("Token segments are not valid base64url JSON", result.Failure?.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("alice.test")]
    [InlineData(42)]
    public async Task AuthenticateAsync_SubjectThatIsNotADid_IsRejected(object? subject)
    {
        // The session the handler validates is keyed by the token's subject, so a token without
        // a DID there cannot produce the account claims.
        var claims = new Dictionary<string, object> { ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() };
        if (subject is not null)
            claims["sub"] = subject;

        var result = await AuthenticateAsync(TestJws.Mint(Header, claims, _ => new byte[64]));

        Assert.False(result.Succeeded);
        Assert.Equal("Token subject ('sub') is not a DID.", result.Failure?.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_HeaderThatIsNotJson_IsRejected()
    {
        var token = TestJws.WithSegment(Token(), 0, TestJws.Encode(Encoding.UTF8.GetBytes("not json")));

        var result = await AuthenticateAsync(token);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Token segments are not valid base64url JSON", result.Failure?.Message, StringComparison.Ordinal);
    }
}
