using System.Diagnostics.CodeAnalysis;

namespace ATProtoNet.Crypto.HttpSignatures;

/// <summary>An RFC 8941 token, kept apart from a string so each serializes as it was written.</summary>
/// <param name="Value">The token's text.</param>
public readonly record struct StructuredToken(string Value);

/// <summary>An RFC 8941 bare item: integer, decimal, string, token, byte sequence or boolean.</summary>
/// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc8941#section-3.3"/>.</remarks>
public union BareItem(long, decimal, string, StructuredToken, ReadOnlyMemory<byte>, bool);

/// <summary>A list or dictionary member: an item or an inner list, with its parameters.</summary>
/// <param name="Parameters">The parameters, in order; a repeated key keeps its first position and its last value.</param>
/// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc8941#section-3.1"/>.</remarks>
public closed record StructuredMember(IReadOnlyList<KeyValuePair<string, BareItem>> Parameters);

/// <summary>An item: a bare item with parameters.</summary>
/// <param name="Value">The bare item.</param>
/// <param name="Parameters">The parameters, in order.</param>
public sealed record StructuredItem(BareItem Value, IReadOnlyList<KeyValuePair<string, BareItem>> Parameters) : StructuredMember(Parameters)
{
    /// <summary>Creates an item without parameters.</summary>
    /// <param name="value">The bare item.</param>
    public StructuredItem(BareItem value) : this(value, []) { }
}

/// <summary>An inner list: items in parentheses, with parameters of its own.</summary>
/// <param name="Items">The items, in order.</param>
/// <param name="Parameters">The parameters, in order.</param>
public sealed record StructuredInnerList(IReadOnlyList<StructuredItem> Items, IReadOnlyList<KeyValuePair<string, BareItem>> Parameters) : StructuredMember(Parameters);

/// <summary>An RFC 8941 dictionary: ordered members by key.</summary>
/// <remarks>
/// A repeated key keeps the position of its first occurrence and the value of its last, as RFC 8941 says;
/// <see cref="IsDuplicated"/> tells a caller that must refuse repeats. See <see href="https://www.rfc-editor.org/rfc/rfc8941#section-3.2"/>.
/// </remarks>
public sealed class StructuredDictionary
{
    private readonly List<KeyValuePair<string, StructuredMember>> _members = [];
    private readonly HashSet<string> _duplicated = [];

    /// <summary>Creates a dictionary from members in order.</summary>
    /// <param name="members">The members; a boolean <see langword="true"/> item serializes as its key alone.</param>
    public StructuredDictionary(IEnumerable<KeyValuePair<string, StructuredMember>> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        foreach (var (key, member) in members)
        {
            var index = _members.FindIndex(m => m.Key == key);
            if (index < 0)
            {
                _members.Add(new(key, member));
                continue;
            }
            _members[index] = new(key, member);
            _duplicated.Add(key);
        }
    }

    /// <summary>The members, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, StructuredMember>> Members => _members;

    /// <summary>The member with <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member, when present.</param>
    /// <returns>Whether the key is present.</returns>
    public bool TryGetValue(string key, [NotNullWhen(true)] out StructuredMember? member)
    {
        member = _members.Find(m => m.Key == key).Value;
        return member is not null;
    }

    /// <summary>Whether <paramref name="key"/> appeared more than once.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key was repeated.</returns>
    public bool IsDuplicated(string key) => _duplicated.Contains(key);
}
