using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Represents a Content Identifier (CID) used to reference content-addressed data.
/// CIDs are self-describing content-addressed identifiers used in IPLD.
/// </summary>
[JsonConverter(typeof(CidJsonConverter))]
public sealed partial class Cid : IEquatable<Cid>
{
    // CIDs in atproto are typically base32 or base58btc encoded
    [GeneratedRegex(@"^[a-zA-Z0-9+/=]+$", RegexOptions.Compiled)]
    private static partial Regex CidPattern();

    /// <summary>
    /// The CID string value.
    /// </summary>
    public string Value { get; }

    private Cid(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a CID from a string value with basic validation.
    /// </summary>
    public static Cid Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new Cid(value);
    }

    /// <summary>
    /// Attempts to create a CID from a string value without throwing.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Cid? cid)
    {
        cid = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        cid = new Cid(value);
        return true;
    }

    /// <summary>
    /// Creates a CID without validation.
    /// </summary>
    internal static Cid UnsafeCreate(string value) => new(value);

    public static implicit operator string(Cid cid) => cid.Value;

    public bool Equals(Cid? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is Cid other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(Cid? left, Cid? right) => Equals(left, right);
    public static bool operator !=(Cid? left, Cid? right) => !Equals(left, right);
}
