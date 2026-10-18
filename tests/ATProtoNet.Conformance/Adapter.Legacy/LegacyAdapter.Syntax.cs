using ATProtoNet.Identity;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public SyntaxVerdict ParseIdentifier(IdentifierKind kind, string input) => kind switch
    {
        IdentifierKind.Did => Did.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.Handle => Handle.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.AtIdentifier => AtIdentifier.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.Nsid => Nsid.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.AtUri => AtUri.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.RecordKey => RecordKey.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.Tid => Tid.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        IdentifierKind.Datetime => AtDatetime.TryParse(input, out _) ? new(true, input) : SyntaxVerdict.Invalid,
        IdentifierKind.Cid => Cid.TryParse(input, out var v) ? new(true, v.Value) : SyntaxVerdict.Invalid,
        _ => throw new NotSupportedException($"0.8 has no {kind} type."),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> GenerateTids(int clockId, IReadOnlyList<long> clockMicros)
    {
        var generator = new TidGenerator(clockId, new ScriptedClock(clockMicros));
        return [.. clockMicros.Select(_ => generator.Next().Value)];
    }

    private sealed class ScriptedClock(IReadOnlyList<long> micros) : TimeProvider
    {
        private int _next;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(micros[_next++] * 10);
    }
}
