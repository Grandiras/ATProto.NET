using System;
using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet;

// The NSID the linked schema model uses. ATProtoNet's own Nsid needs static abstract interface members, which the
// compiler hosts of a netstandard2.0 generator cannot load, so CodeGen carries this syntax-only copy
// (https://atproto.com/specs/nsid#nsid-syntax).
internal sealed class Nsid : IEquatable<Nsid>
{
    private Nsid(string value) => Value = value;

    public string Value { get; }

    public string Authority => Value.Substring(0, Value.LastIndexOf('.'));

    public string Name => Value.Substring(Value.LastIndexOf('.') + 1);

    public static Nsid Parse(string s) => TryParse(s, out var nsid) ? nsid : throw new FormatException($"\"{s}\" is not a valid NSID.");

    public static bool TryParse(string? s, [NotNullWhen(true)] out Nsid? result)
    {
        result = s is not null && IsValid(s) ? new(s) : null;
        return result is not null;
    }

    private static bool IsValid(string s)
    {
        if (s.Length > 317) return false;
        var segments = s.Split('.');
        if (segments.Length < 3) return false;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var last = i == segments.Length - 1;
            if (segment.Length is 0 or > 63) return false;
            if (last ? !IsLetter(segment[0]) : i == 0 && !IsLetter(segment[0])) return false;
            foreach (var c in segment)
                if (!(IsLetter(c) || c is >= '0' and <= '9' || c == '-' && !last)) return false;
            if (!last && (segment[0] == '-' || segment[segment.Length - 1] == '-')) return false;
        }
        return s.Length - segments[segments.Length - 1].Length - 1 <= 253;
    }

    private static bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    public bool Equals(Nsid? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Nsid);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(Nsid? left, Nsid? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Nsid? left, Nsid? right) => !(left == right);
}
