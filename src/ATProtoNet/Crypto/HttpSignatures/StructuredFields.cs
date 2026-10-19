using System.Globalization;
using System.Text;

namespace ATProtoNet.Crypto.HttpSignatures;

/// <summary>Parses and serializes RFC 8941 structured field values: items, lists and dictionaries.</summary>
/// <remarks>
/// Parsing is strict: whatever RFC 8941 makes a parser fail on fails here, with no lenient recovery, since a field
/// two parties read differently is a way to smuggle a signature past one of them. The date and display-string items
/// of RFC 9651 are not recognised. Field values are bounded by <see cref="MaxHeaderValueLength"/> before parsing.
/// See <see href="https://www.rfc-editor.org/rfc/rfc8941#section-4"/>.
/// </remarks>
public static partial class StructuredFields
{
    /// <summary>The longest field value the parsers read, in characters.</summary>
    public const int MaxHeaderValueLength = 8 * 1024;

    /// <summary>Parses an item field.</summary>
    /// <param name="field">The field value.</param>
    /// <returns>The item.</returns>
    /// <exception cref="FormatException">The value is not an RFC 8941 item.</exception>
    public static StructuredItem ParseItem(string field) => Parse(field, static p => p.Item());

    /// <summary>Parses a list field; an empty value is an empty list.</summary>
    /// <param name="field">The field value, all field lines combined with commas.</param>
    /// <returns>The members, in order.</returns>
    /// <exception cref="FormatException">The value is not an RFC 8941 list.</exception>
    public static IReadOnlyList<StructuredMember> ParseList(string field) => Parse(field, static p =>
    {
        var members = new List<StructuredMember>();
        p.Members(() => members.Add(p.ItemOrInnerList()));
        return members;
    });

    /// <summary>Parses a dictionary field; an empty value is an empty dictionary.</summary>
    /// <param name="field">The field value, all field lines combined with commas.</param>
    /// <returns>The dictionary.</returns>
    /// <exception cref="FormatException">The value is not an RFC 8941 dictionary.</exception>
    public static StructuredDictionary ParseDictionary(string field) => Parse(field, static p =>
    {
        var members = new List<KeyValuePair<string, StructuredMember>>();
        p.Members(() =>
        {
            var key = p.Key();
            members.Add(new(key, p.Take('=') ? p.ItemOrInnerList() : new StructuredItem(true, p.Parameters())));
        });
        return new StructuredDictionary(members);
    });

    /// <summary>The canonical serialization of an item or inner list.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The field value.</returns>
    /// <exception cref="ArgumentException">A value cannot be serialized (out of range, or not valid for its type).</exception>
    public static string Serialize(StructuredMember member) => Write(new StringBuilder(), member).ToString();

    /// <summary>The canonical serialization of a list.</summary>
    /// <param name="members">The members.</param>
    /// <returns>The field value.</returns>
    /// <exception cref="ArgumentException">A value cannot be serialized.</exception>
    public static string SerializeList(IEnumerable<StructuredMember> members) => string.Join(", ", members.Select(Serialize));

    /// <summary>The canonical serialization of a dictionary.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <returns>The field value.</returns>
    /// <exception cref="ArgumentException">A key or value cannot be serialized.</exception>
    public static string Serialize(StructuredDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        var sb = new StringBuilder();
        foreach (var (key, member) in dictionary.Members)
        {
            if (sb.Length > 0) sb.Append(", ");
            WriteKey(sb, key);
            if (member is StructuredItem { Value: bool and true } flag) WriteParameters(sb, flag.Parameters);
            else Write(sb.Append('='), member);
        }
        return sb.ToString();
    }

    /// <summary>The canonical serialization of a bare item.</summary>
    /// <param name="item">The bare item.</param>
    /// <returns>Its text.</returns>
    /// <exception cref="ArgumentException">The value cannot be serialized.</exception>
    public static string Serialize(BareItem item) => WriteBare(new StringBuilder(), item).ToString();

    private static T Parse<T>(string field, Func<Parser, T> parse)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Length > MaxHeaderValueLength) throw new FormatException($"A structured field is at most {MaxHeaderValueLength} characters.");
        var parser = new Parser(field);
        parser.SkipSpaces();
        var result = parse(parser);
        parser.SkipSpaces();
        return parser.End ? result : throw parser.Fail("Unexpected characters after the value");
    }

    private static StringBuilder Write(StringBuilder sb, StructuredMember member)
    {
        switch (member)
        {
            case StructuredItem item:
                WriteBare(sb, item.Value);
                break;
            case StructuredInnerList list:
                sb.Append('(');
                for (var i = 0; i < list.Items.Count; i++) Write(i == 0 ? sb : sb.Append(' '), list.Items[i]);
                sb.Append(')');
                break;
        }
        return WriteParameters(sb, member.Parameters);
    }

    private static StringBuilder WriteParameters(StringBuilder sb, IReadOnlyList<KeyValuePair<string, BareItem>> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            WriteKey(sb.Append(';'), key);
            if (value is not (bool and true)) WriteBare(sb.Append('='), value);
        }
        return sb;
    }

    private static void WriteKey(StringBuilder sb, string key) =>
        sb.Append(key is [(>= 'a' and <= 'z') or '*', ..] && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.' or '*')
            ? key : throw new ArgumentException($"\"{key}\" is not a structured field key.", nameof(key)));

    private static StringBuilder WriteBare(StringBuilder sb, BareItem item) => item switch
    {
        long integer when integer is >= -999_999_999_999_999 and <= 999_999_999_999_999 => sb.Append(integer.ToString(CultureInfo.InvariantCulture)),
        decimal number when Math.Abs(Math.Round(number, 3, MidpointRounding.ToEven)) < 1_000_000_000_000m =>
            sb.Append(Math.Round(number, 3, MidpointRounding.ToEven).ToString("0.0##", CultureInfo.InvariantCulture)),
        string text when text.All(c => c is >= ' ' and <= '~') =>
            sb.Append('"').Append(text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"'),
        StructuredToken token when token.Value is [(>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '*', ..] && token.Value.All(Parser.IsTokenChar) =>
            sb.Append(token.Value),
        ReadOnlyMemory<byte> bytes => sb.Append(':').Append(Convert.ToBase64String(bytes.Span)).Append(':'),
        bool flag => sb.Append(flag ? "?1" : "?0"),
        _ => throw new ArgumentException("The value cannot be serialized as an RFC 8941 bare item.", nameof(item)),
    };
}
