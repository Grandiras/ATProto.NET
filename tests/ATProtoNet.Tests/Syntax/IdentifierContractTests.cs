using System.Reflection;
using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Syntax;

/// <summary>What every identifier type shares, reached through the public <see cref="IAtIdentifier{TSelf}"/> constraint.</summary>
public class IdentifierContractTests
{
    private const string DidText = "did:plc:abc123", OtherDid = "did:plc:abc124";
    private const string SpaceText = "at://did:plc:abc123/space/com.example.forum/a";
    private const string RecordText = SpaceText + "/did:plc:def456/com.example.post/1";
    private const string CidText = "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku";

    // A value, the next one in order, and an invalid one, for each identifier type.
    public static TheoryData<Type, string, string, string> Samples() => new()
    {
        { typeof(Did), DidText, OtherDid, "did:plc:" },
        { typeof(Handle), "alice.bsky.social", "bob.bsky.social", "alice" },
        { typeof(AtIdentifier), "alice.bsky.social", DidText, "did:plc:" },
        { typeof(Nsid), "app.bsky.feed.like", "app.bsky.feed.post", "app.bsky" },
        { typeof(RecordKey), "3jzfcijpj2z2a", "self", ".." },
        { typeof(Tid), "3jzfcijpj2z2a", "3jzfcijpj2z2b", "zzzzzzzzzzzzz" },
        { typeof(AtUri), $"at://{DidText}/app.bsky.feed.like/1", $"at://{DidText}/app.bsky.feed.post/1", $"at://{DidText}/" },
        { typeof(SpaceUri), SpaceText, SpaceText[..^1] + "b", "at://alice.bsky.social/space/com.example.forum/a" },
        { typeof(SpaceRecordUri), RecordText, RecordText[..^1] + "2", SpaceText },
        { typeof(Cid), CidText, "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", "hello" },
        { typeof(AtDatetime), "2024-01-15T12:00:00.000Z", "2024-01-16T12:00:00.000Z", "2024-01-15" },
        { typeof(Language), "de", "en", "e" },
        { typeof(DecimalString), "1.5", "2.5", "1,5" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    [Trait("Spec", "SYN-3"), Trait("Spec", "SYN-4"), Trait("Spec", "SYN-7"), Trait("Spec", "SYN-10")]
    public void Contract_EveryForm_AcceptsAndRejectsTheSameInputs(Type type, string lower, string higher, string invalid) =>
        Generic.Invoke(typeof(IdentifierContractTests), nameof(AssertContract), type, lower, higher, invalid);

    private static void AssertContract<T>(string lower, string higher, string invalid) where T : IAtIdentifier<T>
    {
        var parsed = T.Parse(lower);
        Assert.Equal(lower, parsed.Value);
        Assert.Equal(lower, parsed.ToString());

        Assert.True(T.TryParse(lower, out var fromString));
        Assert.True(T.TryParse(lower.AsSpan(), out var fromChars));
        Assert.True(T.TryParse(Encoding.UTF8.GetBytes(lower), out var fromUtf8));
        Assert.Equal([parsed, parsed, parsed], [fromString, fromChars, fromUtf8]);
        Assert.Equal(parsed, T.Parse(lower.AsSpan()));
        Assert.Equal(parsed, T.Parse(Encoding.UTF8.GetBytes(lower)));
        Assert.Equal(parsed, IParse<T>.Parse(lower));
        Assert.Equal(parsed.GetHashCode(), fromChars.GetHashCode());

        var other = T.Parse(higher);
        Assert.True(parsed.CompareTo(other) < 0);
        Assert.True(other.CompareTo(parsed) > 0);
        Assert.Equal(0, parsed.CompareTo(fromUtf8));
        Assert.False(parsed.Equals(other));

        Assert.False(T.TryParse(invalid, out _));
        Assert.False(T.TryParse(invalid.AsSpan(), out _));
        Assert.False(T.TryParse(Encoding.UTF8.GetBytes(invalid), out _));
        Assert.False(T.TryParse((string?)null, out _));
        Assert.Throws<FormatException>(() => T.Parse(invalid));
        Assert.Throws<FormatException>(() => T.Parse(invalid.AsSpan()));
        Assert.Throws<FormatException>(() => T.Parse(Encoding.UTF8.GetBytes(invalid)));
        Assert.Throws<FormatException>(() => IParse<T>.Parse(invalid));
        Assert.Throws<ArgumentNullException>(() => T.Parse((string)null!));
    }

    // Under an IAtIdentifier<T> constraint a string reaches the IParsable<T> members only through a constraint that offers just them.
    private static class IParse<T> where T : IParsable<T>
    {
        public static T Parse(string s) => T.Parse(s, provider: null);
    }

    private static readonly Type[] AllIdentifierTypes = [.. typeof(Did).Assembly.GetExportedTypes()
        .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAtIdentifier<>)))];

    public static TheoryData<Type> IdentifierTypes() => [.. AllIdentifierTypes];

    [Fact]
    [Trait("Spec", "SYN-5")]
    public void IdentifierTypes_AreSealedClassesWithoutPublicConstructors_ExceptTid()
    {
        Assert.Contains(typeof(Did), AllIdentifierTypes);
        foreach (var type in AllIdentifierTypes)
        {
            Assert.Equal(type == typeof(Tid), type.IsValueType);
            Assert.True(type.IsValueType || type.IsSealed, type.Name);
            Assert.True(type.IsValueType || !type.IsAbstract, type.Name);
            Assert.DoesNotContain(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance), c => c.GetParameters().Length > 0);
        }
    }

    [Theory]
    [MemberData(nameof(IdentifierTypes))]
    [Trait("Spec", "SYN-6")]
    public void Conversions_NoneFromString_OneToString(Type type)
    {
        var conversions = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "op_Implicit" or "op_Explicit").ToList();

        Assert.DoesNotContain(conversions, m => m.GetParameters()[0].ParameterType == typeof(string));
        Assert.Single(conversions, m => m.Name == "op_Implicit" && m.ReturnType == typeof(string));
    }

    [Fact]
    [Trait("Spec", "SYN-6")]
    public void Conversions_DidAndHandleToAtIdentifier_AreImplicitAndNullSafe()
    {
        AtIdentifier fromDid = Did.Parse(DidText);
        AtIdentifier fromHandle = Handle.Parse("alice.bsky.social");

        Assert.True(fromDid.IsDid);
        Assert.True(fromHandle.IsHandle);
        Assert.Null((AtIdentifier?)(Did?)null);
        Assert.Null((AtIdentifier?)(Handle?)null);
        Assert.Null((string?)(Did?)null);
        Assert.Equal(DidText, (string)fromDid);
    }

    [Fact]
    [Trait("Spec", "SYN-7")]
    public void Sort_UsesOrdinalOrder()
    {
        var handles = new[] { "b.test", "a.test", "c.test" }.Select(Handle.Parse).ToList();

        handles.Sort();

        Assert.Equal(["a.test", "b.test", "c.test"], handles.Select(h => h.Value));
    }

    [Fact]
    [Trait("Spec", "SYN-7")]
    public void Equality_OperatorsCompareByValueAndNull()
    {
        Did? first = Did.Parse(DidText), same = Did.Parse(DidText), none = null;

        Assert.True(first == same);
        Assert.True(first != none);
        Assert.True(none == null);
        Assert.False(first.Equals((object?)none));
    }

    [Theory]
    [InlineData(typeof(Did), 2048)]
    [InlineData(typeof(Handle), 253)]
    [InlineData(typeof(Nsid), 317)]
    [InlineData(typeof(RecordKey), 512)]
    [InlineData(typeof(AtUri), 8192)]
    [InlineData(typeof(AtDatetime), 64)]
    [InlineData(typeof(Language), 128)]
    [InlineData(typeof(DecimalString), 64)]
    [InlineData(typeof(Cid), 59)]
    [Trait("Spec", "SYN-9")]
    public void MaxLength_IsTheLimitTheTypeReports(Type type, int maxLength) =>
        Assert.Equal(maxLength, (int)Generic.Invoke(typeof(IdentifierContractTests), nameof(Limit), type)!);

    private static int Limit<T>() where T : IAtIdentifier<T> => T.MaxLength;

}
