using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Syntax;

/// <summary>
/// A regular expression anchored with <c>$</c> also matches just before a final line feed, so <c>"did:plc:abc\n"</c> parsed as a DID and
/// was passed on as one. Every parser refuses a value with a line break, in every form.
/// </summary>
public class IdentifierLineBreakTests
{
    public static TheoryData<Type, string> ValidValues() => new()
    {
        { typeof(Did), "did:plc:z72i7hdynmk6r22z27h6tvur" },
        { typeof(Did), "did:web:example.com" },
        { typeof(Handle), "alice.bsky.social" },
        { typeof(Nsid), "app.bsky.feed.post" },
        { typeof(RecordKey), "self" },
        { typeof(Tid), "3jzfcijpj2z2a" },
        { typeof(AtIdentifier), "alice.bsky.social" },
        { typeof(AtIdentifier), "did:plc:z72i7hdynmk6r22z27h6tvur" },
        { typeof(AtUri), "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3jzfcijpj2z2a" },
        { typeof(SpaceUri), "at://did:plc:z72i7hdynmk6r22z27h6tvur/space/com.example.forum/main" },
        { typeof(SpaceRecordUri), "at://did:plc:z72i7hdynmk6r22z27h6tvur/space/com.example.forum/main/did:plc:ewvi7nxzyoun6zhxrhs64oiz/com.example.post/1" },
        { typeof(Cid), "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" },
        { typeof(AtDatetime), "1985-04-12T23:20:50.123Z" },
        { typeof(Language), "pt-BR" },
        { typeof(DecimalString), "52.520" },
    };

    [Theory]
    [MemberData(nameof(ValidValues))]
    [Trait("Spec", "SYN-2")]
    public void TryParse_ValueWithALineBreak_IsRefused(Type type, string value) =>
        Generic.Invoke(typeof(IdentifierLineBreakTests), nameof(AssertRefusesLineBreaks), type, value);

    private static void AssertRefusesLineBreaks<T>(string value) where T : IAtIdentifier<T>
    {
        Assert.True(T.TryParse(value, out _));
        foreach (var broken in new[] { value + "\n", value + "\r\n", value + "\r", "\n" + value, value[..(value.Length / 2)] + "\n" + value[(value.Length / 2)..] })
        {
            Assert.False(T.TryParse(broken, out _), broken);
            Assert.False(T.TryParse(broken.AsSpan(), out _), broken);
            Assert.False(T.TryParse(Encoding.UTF8.GetBytes(broken), out _), broken);
            Assert.Throws<FormatException>(() => T.Parse(broken));
        }
    }

    [Fact]
    [Trait("Spec", "SYN-2")]
    public void IsValid_UriWithALineBreak_IsRefused()
    {
        Assert.True(UriFormat.IsValid("https://example.com"));
        Assert.False(UriFormat.IsValid("https://example.com\n"));
        Assert.False(UriFormat.IsValid("https://exam\nple.com"));
    }
}
