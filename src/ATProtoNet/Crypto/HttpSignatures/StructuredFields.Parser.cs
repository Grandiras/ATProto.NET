using System.Buffers;
using System.Globalization;
using System.Text;

using ATProtoNet.Data;

namespace ATProtoNet.Crypto.HttpSignatures;

public static partial class StructuredFields
{
    // The parsing algorithms of RFC 8941 §4.2, one method per algorithm, over one field value.
    private sealed class Parser(string input)
    {
        private static readonly SearchValues<char> TokenChars =
            SearchValues.Create("!#$%&'*+-.^_`|~:/0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

        private int _at;

        public bool End => _at == input.Length;

        private char Peek => End ? '\0' : input[_at];

        public static bool IsTokenChar(char c) => TokenChars.Contains(c);

        public FormatException Fail(string message) => new($"{message} at position {_at} of a structured field.");

        public void SkipSpaces()
        {
            while (Peek == ' ') _at++;
        }

        // Consumes expected when it is next.
        public bool Take(char expected)
        {
            if (Peek != expected) return false;
            _at++;
            return true;
        }

        // The comma-separated members of a list or dictionary (§4.2.1, §4.2.2).
        public void Members(Action member)
        {
            while (!End)
            {
                member();
                SkipWhitespace();
                if (End) return;
                if (!Take(',')) throw Fail("Expected ','");
                SkipWhitespace();
                if (End) throw Fail("A trailing comma");
            }
        }

        public StructuredMember ItemOrInnerList() => Peek == '(' ? InnerList() : Item();

        public StructuredItem Item() => new(BareItem(), Parameters());

        public string Key()
        {
            if (Peek is not ((>= 'a' and <= 'z') or '*')) throw Fail("Expected a key");
            var start = _at;
            while (Peek is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.' or '*') _at++;
            return input[start.._at];
        }

        public IReadOnlyList<KeyValuePair<string, BareItem>> Parameters()
        {
            var parameters = new List<KeyValuePair<string, BareItem>>();
            while (Take(';'))
            {
                SkipSpaces();
                var key = Key();
                var value = Take('=') ? BareItem() : true;
                var index = parameters.FindIndex(p => p.Key == key);
                if (index < 0) parameters.Add(new(key, value));
                else parameters[index] = new(key, value);
            }
            return parameters;
        }

        private void SkipWhitespace()
        {
            while (Peek is ' ' or '\t') _at++;
        }

        private StructuredInnerList InnerList()
        {
            _at++;
            var items = new List<StructuredItem>();
            while (!End)
            {
                SkipSpaces();
                if (Take(')')) return new(items, Parameters());
                items.Add(Item());
                if (Peek is not (' ' or ')')) throw Fail("Expected a space or ')' after an inner list member");
            }
            throw Fail("An unterminated inner list");
        }

        private BareItem BareItem() => Peek switch
        {
            '-' or (>= '0' and <= '9') => Number(),
            '"' => String(),
            ':' => ByteSequence(),
            '?' => Boolean(),
            (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '*' => Token(),
            _ => throw Fail("Expected an item"),
        };

        private BareItem Number()
        {
            var negative = Take('-');
            if (Peek is not (>= '0' and <= '9')) throw Fail("Expected a digit");
            var start = _at;
            var point = -1;
            for (; Peek is (>= '0' and <= '9') or '.'; _at++)
            {
                if (Peek == '.')
                {
                    if (point >= 0) break;
                    if (_at - start > 12) throw Fail("A decimal has at most 12 integer digits");
                    point = _at - start;
                }
                if (_at - start >= (point < 0 ? 15 : 16)) throw Fail("A number is too long");
            }
            var digits = input.AsSpan(start.._at);
            if (point < 0)
            {
                var integer = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
                return negative ? -integer : integer;
            }
            if (digits.Length - point - 1 is < 1 or > 3) throw Fail("A decimal has 1 to 3 fractional digits");
            var number = decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return negative && number != 0 ? -number : number;
        }

        private string String()
        {
            _at++;
            var sb = new StringBuilder();
            while (!End)
            {
                var c = input[_at++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (Peek is not ('"' or '\\')) throw Fail("An invalid escape in a string");
                    c = input[_at++];
                }
                else if (c is < ' ' or > '~') throw Fail("An invalid character in a string");
                sb.Append(c);
            }
            throw Fail("An unterminated string");
        }

        private StructuredToken Token()
        {
            var start = _at;
            while (!End && TokenChars.Contains(Peek)) _at++;
            return new(input[start.._at]);
        }

        private ReadOnlyMemory<byte> ByteSequence()
        {
            var end = input.IndexOf(':', _at + 1);
            if (end < 0) throw Fail("An unterminated byte sequence");
            // Padding is optional on input; anything else outside the base64 alphabet fails.
            var content = input.AsSpan((_at + 1)..end);
            _at = end + 1;
            return StrictBase64.TryDecode(content, url: false, allowPadding: true, out var bytes) ? bytes : throw Fail("Invalid base64 in a byte sequence");
        }

        private bool Boolean()
        {
            _at++;
            if (Take('1')) return true;
            return Take('0') ? false : throw Fail("Expected ?0 or ?1");
        }
    }
}
