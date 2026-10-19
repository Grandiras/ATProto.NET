using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> SyntaxKnownFailures = new()
    {
    };

    /// <inheritdoc />
    public SyntaxVerdict ParseIdentifier(IdentifierKind kind, string input) => kind switch
    {
        IdentifierKind.Did => Verdict<Did>(input),
        IdentifierKind.Handle => Verdict<Handle>(input),
        IdentifierKind.AtIdentifier => Verdict<AtIdentifier>(input),
        IdentifierKind.Nsid => Verdict<Nsid>(input),
        IdentifierKind.AtUri => Verdict<AtUri>(input),
        IdentifierKind.RecordKey => Verdict<RecordKey>(input),
        IdentifierKind.Tid => Verdict<Tid>(input),
        IdentifierKind.Datetime => Verdict<AtDatetime>(input),
        IdentifierKind.Cid => Verdict<Cid>(input),
        IdentifierKind.Language => Verdict<Language>(input),
        IdentifierKind.Uri => UriFormat.IsValid(input) ? new(true, input) : SyntaxVerdict.Invalid,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> GenerateTids(int clockId, IReadOnlyList<long> clockMicros)
    {
        var generator = new TidGenerator(clockId, new ScriptedClock(clockMicros));
        return [.. clockMicros.Select(_ => generator.Next().Value)];
    }

    // The verdict of the string form, which the char-span, UTF-8 and Parse forms must agree with (SYN-3, SYN-4).
    private static SyntaxVerdict Verdict<T>(string input) where T : IAtIdentifier<T>
    {
        var valid = T.TryParse(input, out var parsed);
        var chars = T.TryParse(input.AsSpan(), out var fromChars);
        var utf8 = T.TryParse(Encoding.UTF8.GetBytes(input), out var fromUtf8);
        if (valid != chars || valid != utf8 || valid && (parsed!.Value != fromChars!.Value || parsed.Value != fromUtf8!.Value))
            throw new InvalidOperationException($"The string, char-span and UTF-8 forms disagree on {input}.");
        try
        {
            T.Parse(input);
            if (!valid) throw new InvalidOperationException($"Parse accepted {input}, TryParse did not.");
        }
        catch (FormatException) when (!valid)
        {
        }
        return valid ? new(true, parsed!.Value) : SyntaxVerdict.Invalid;
    }

    private sealed class ScriptedClock(IReadOnlyList<long> micros) : TimeProvider
    {
        private int _next;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(micros[_next++] * 10);
    }
}
