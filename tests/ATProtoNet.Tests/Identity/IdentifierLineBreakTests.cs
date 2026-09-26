using System.Globalization;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// A regular expression anchored with <c>$</c> also matches just before a final line feed, so
/// <c>"did:plc:abc\n"</c> parsed as a DID — and passed on as one, line feed and all. Every
/// identifier parser has to refuse a value carrying a line break.
/// </summary>
public class IdentifierLineBreakTests
{
    public static TheoryData<string, string> ValidValues => new()
    {
        { nameof(Did), "did:plc:z72i7hdynmk6r22z27h6tvur" },
        { nameof(Did), "did:web:example.com" },
        { nameof(Handle), "alice.bsky.social" },
        { nameof(Nsid), "app.bsky.feed.post" },
        { nameof(RecordKey), "self" },
        { nameof(Tid), "3jzfcijpj2z2a" },
        { nameof(AtIdentifier), "alice.bsky.social" },
        { nameof(AtIdentifier), "did:plc:z72i7hdynmk6r22z27h6tvur" },
        { nameof(AtUri), "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3jzfcijpj2z2a" },
        { nameof(Cid), "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" },
        { nameof(AtDatetime), "1985-04-12T23:20:50.123Z" },
        { nameof(SpaceUri), "at://did:plc:z72i7hdynmk6r22z27h6tvur/space/com.example.forum/main" },
    };

    [Theory]
    [MemberData(nameof(ValidValues))]
    public void TryParse_ValueWithALineBreak_IsRefused(string kind, string value)
    {
        Assert.True(Parses(kind, value));
        Assert.False(Parses(kind, value + "\n"));
        Assert.False(Parses(kind, value + "\r\n"));
        Assert.False(Parses(kind, value + "\r"));
        Assert.False(Parses(kind, "\n" + value));
    }

    [Theory]
    [InlineData("did:web:example.com\n")]
    [InlineData("did:web:example.com#bsky_fg\n")]
    public void ServiceAuthAudience_WithATrailingLineFeed_IsRefused(string audience) =>
        Assert.False(ServiceAuthSyntax.IsAudience(audience));

    [Fact]
    public void DidWeb_HostWithAnEncodedTrailingLineFeed_IsRefused()
    {
        // %0A decodes into the host, where the hostname check has to see it.
        var ex = Assert.Throws<DidResolutionException>(
            () => DidWebResolver.BuildResolutionUrl(Did.Parse("did:web:example.com%0A"), allowPrivateNetworks: false));

        Assert.Equal(DidResolutionErrorKind.InvalidDid, ex.Kind);
    }

    private static bool Parses(string kind, string value) => kind switch
    {
        nameof(Did) => Parses<Did>(value),
        nameof(Handle) => Parses<Handle>(value),
        nameof(Nsid) => Parses<Nsid>(value),
        nameof(RecordKey) => Parses<RecordKey>(value),
        nameof(Tid) => Parses<Tid>(value),
        nameof(AtIdentifier) => Parses<AtIdentifier>(value),
        nameof(AtUri) => Parses<AtUri>(value),
        nameof(Cid) => Parses<Cid>(value),
        nameof(AtDatetime) => Parses<AtDatetime>(value),
        nameof(SpaceUri) => Parses<SpaceUri>(value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static bool Parses<T>(string value) where T : IParsable<T> =>
        T.TryParse(value, CultureInfo.InvariantCulture, out _);
}
