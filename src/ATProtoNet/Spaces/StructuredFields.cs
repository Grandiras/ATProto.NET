using System.Globalization;
using System.Text;

namespace ATProtoNet.Spaces;

// The subset of RFC 8941 structured fields HTTP message signatures need: parsing a dictionary (the
// Signature-Input and Signature fields), and serializing an inner list with its parameters (the
// @signature-params line of the signature base).
//
// Parsing is strict: whatever RFC 8941 makes a parser fail on fails here, as FormatException. An item is a
// string, token, integer, decimal, byte sequence or boolean; the date and display-string items of RFC 9651
// are not recognised, so a field carrying one is refused rather than half read. A decimal's integer part
// is at most 12 digits, which is what RFC 8941 can serialize.
internal static class StructuredFields
{
    // A token item, kept apart from a string so each serializes as it was written.
    internal readonly record struct Token(string Value);

    // A bare item (string, Token, long, decimal, byte[] or bool) with its parameters, whose values are
    // bare items too.
    internal sealed record Item(object Value, IReadOnlyList<KeyValuePair<string, object>> Parameters);

    internal sealed record InnerList(IReadOnlyList<Item> Items, IReadOnlyList<KeyValuePair<string, object>> Parameters);

    // A parsed dictionary. A repeated key keeps the position of its first occurrence and the value of its
    // last, as RFC 8941 says; Duplicated tells a caller that wants repeats refused.
    internal sealed class Dictionary
    {
        private readonly List<KeyValuePair<string, object>> _members = [];
        private readonly HashSet<string> _duplicated = [];

        // The member's value, an Item or an InnerList, or null.
        public object? Get(string key) => FindIndex(_members, key) is var i and >= 0 ? _members[i].Value : null;

        public bool Duplicated(string key) => _duplicated.Contains(key);

        internal void Set(string key, object value)
        {
            if (FindIndex(_members, key) is var i and >= 0)
            {
                _members[i] = new(key, value);
                _duplicated.Add(key);
            }
            else
                _members.Add(new(key, value));
        }
    }

    internal static Dictionary ParseDictionary(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parser = new Parser(input);
        var dictionary = new Dictionary();
        parser.SkipSpaces();
        while (!parser.End)
        {
            var key = parser.Key();
            dictionary.Set(key, parser.Peek == '=' && parser.Advance() ? parser.ItemOrInnerList() : new Item(true, parser.Parameters()));
            parser.SkipWhitespace();
            if (parser.End)
                break;

            parser.Expect(',');
            parser.SkipWhitespace();
            if (parser.End)
                throw new FormatException("A structured field dictionary cannot end with a comma.");
        }

        return dictionary;
    }

    // The canonical serialization of an inner list: members separated by one space, parameters in order, each
    // bare item in its canonical form.
    internal static string Serialize(InnerList list) =>
        $"({string.Join(' ', list.Items.Select(Serialize))}){SerializeParameters(list.Parameters)}";

    private static string Serialize(Item item) => SerializeBare(item.Value) + SerializeParameters(item.Parameters);

    private static string SerializeParameters(IReadOnlyList<KeyValuePair<string, object>> parameters)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            sb.Append(';').Append(key);
            if (value is not true)
                sb.Append('=').Append(SerializeBare(value));
        }

        return sb.ToString();
    }

    private static string SerializeBare(object value) => value switch
    {
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.0##", CultureInfo.InvariantCulture),
        string text => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        Token token => token.Value,
        byte[] bytes => $":{Convert.ToBase64String(bytes)}:",
        bool flag => flag ? "?1" : "?0",
        _ => throw new ArgumentException($"Not a structured field item: {value.GetType().Name}.", nameof(value)),
    };

    private static int FindIndex(List<KeyValuePair<string, object>> pairs, string key) =>
        pairs.FindIndex(pair => string.Equals(pair.Key, key, StringComparison.Ordinal));

    private sealed class Parser(string input)
    {
        private int _position;

        public bool End => _position >= input.Length;

        public int Peek => End ? -1 : input[_position];

        // Consumes the current character; always true, so a caller can use it inside a condition.
        public bool Advance()
        {
            _position++;
            return true;
        }

        public void SkipSpaces()
        {
            while (Peek == ' ')
                _position++;
        }

        public void SkipWhitespace()
        {
            while (Peek is ' ' or '\t')
                _position++;
        }

        public void Expect(char expected)
        {
            if (Peek != expected)
                throw Fail($"Expected '{expected}'");

            _position++;
        }

        public string Key()
        {
            if (!(IsLowerAlpha(Peek) || Peek == '*'))
                throw Fail("Expected a key");

            var start = _position;
            while (IsLowerAlpha(Peek) || IsDigit(Peek) || Peek is '_' or '-' or '.' or '*')
                _position++;

            return input[start.._position];
        }

        public object ItemOrInnerList() => Peek == '(' ? InnerList() : Item();

        public List<KeyValuePair<string, object>> Parameters()
        {
            var parameters = new List<KeyValuePair<string, object>>();
            while (Peek == ';')
            {
                _position++;
                SkipSpaces();
                var key = Key();
                var value = Peek == '=' && Advance() ? BareItem() : true;

                if (FindIndex(parameters, key) is var i and >= 0)
                    parameters[i] = new(key, value);
                else
                    parameters.Add(new(key, value));
            }

            return parameters;
        }

        private Item Item() => new(BareItem(), Parameters());

        private InnerList InnerList()
        {
            Expect('(');
            var items = new List<Item>();
            while (!End)
            {
                SkipSpaces();
                if (Peek == ')')
                {
                    _position++;
                    return new InnerList(items, Parameters());
                }

                items.Add(Item());
                if (Peek is not (' ' or ')'))
                    throw Fail("Expected a space or ')' after an inner list member");
            }

            throw Fail("Unterminated inner list");
        }

        private object BareItem() => Peek switch
        {
            '-' or (>= '0' and <= '9') => Number(),
            '"' => String(),
            ':' => ByteSequence(),
            '?' => Boolean(),
            _ when IsAlpha(Peek) || Peek == '*' => Token(),
            _ => throw Fail("Expected an item"),
        };

        private object Number()
        {
            var negative = Peek == '-' && Advance();
            if (!IsDigit(Peek))
                throw Fail("Expected a digit");

            var start = _position;
            var decimalPoint = -1;
            while (IsDigit(Peek) || (Peek == '.' && decimalPoint < 0))
            {
                if (Peek == '.')
                    decimalPoint = _position - start;
                _position++;
            }

            var digits = input.AsSpan(start.._position);
            if (decimalPoint < 0)
            {
                if (digits.Length > 15)
                    throw Fail("An integer has at most 15 digits");

                var integer = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
                return negative ? -integer : integer;
            }

            if (decimalPoint > 12 || digits.Length - decimalPoint - 1 is < 1 or > 3)
                throw Fail("A decimal has at most 12 integer and 3 fractional digits");

            var number = decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return negative && number != 0 ? -number : number;
        }

        private string String()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (!End)
            {
                var c = input[_position++];
                switch (c)
                {
                    case '\\' when Peek is '"' or '\\':
                        sb.Append(input[_position++]);
                        break;
                    case '"':
                        return sb.ToString();
                    case < ' ' or > '~' or '\\':
                        throw Fail("Invalid character in a string");
                    default:
                        sb.Append(c);
                        break;
                }
            }

            throw Fail("Unterminated string");
        }

        private Token Token()
        {
            var start = _position;
            while (!End && (IsAlpha(Peek) || IsDigit(Peek) || Peek is ':' or '/' || "!#$%&'*+-.^_`|~".Contains((char)Peek, StringComparison.Ordinal)))
                _position++;

            return new Token(input[start.._position]);
        }

        private byte[] ByteSequence()
        {
            Expect(':');
            var end = input.IndexOf(':', _position);
            if (end < 0)
                throw Fail("Unterminated byte sequence");

            var content = input[_position..end].TrimEnd('=');
            _position = end + 1;

            // Padding is optional on input.
            if (content.Length % 4 == 1 || content.Any(c => !(IsAlpha(c) || IsDigit(c) || c is '+' or '/')))
                throw Fail("Invalid base64 in a byte sequence");

            return Convert.FromBase64String(content.PadRight(content.Length + (4 - content.Length % 4) % 4, '='));
        }

        private bool Boolean()
        {
            Expect('?');
            var value = Peek switch
            {
                '1' => true,
                '0' => false,
                _ => throw Fail("Expected ?0 or ?1"),
            };
            _position++;
            return value;
        }

        private FormatException Fail(string message) => new($"{message} at position {_position} of a structured field.");

        private static bool IsDigit(int c) => c is >= '0' and <= '9';

        private static bool IsLowerAlpha(int c) => c is >= 'a' and <= 'z';

        private static bool IsAlpha(int c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    }
}
