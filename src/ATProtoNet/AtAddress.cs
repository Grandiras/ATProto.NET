using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>Any atproto address: a public <see cref="AtUri"/>, a <see cref="SpaceUri"/> or a <see cref="SpaceRecordUri"/>.</summary>
/// <remarks>
/// For places that accept an address from either data protocol. <see cref="TryParse"/> classifies the text with one scan; JSON carries it
/// as a string.
/// </remarks>
[JsonConverter(typeof(AtAddressJsonConverter))]
public union AtAddress(AtUri, SpaceUri, SpaceRecordUri)
{
    /// <summary>Parses an address of either data protocol.</summary>
    /// <param name="value">The address.</param>
    /// <returns>The address, as the case that matches its kind.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not an address.</exception>
    public static AtAddress Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var address) ? address : throw Identifiers.Invalid<AtAddress>(value);
    }

    /// <summary>Parses an address without throwing.</summary>
    /// <param name="value">The address.</param>
    /// <param name="address">The address, when <paramref name="value"/> is one.</param>
    /// <returns>Whether <paramref name="value"/> is an address.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out AtAddress address)
    {
        address = default;
        if (value is null) return false;
        switch (Address.Scan(value.AsSpan(), out var ends))
        {
            case Address.Kind.Public: address = AtUri.Of(value, ends); return true;
            case Address.Kind.Space: address = SpaceUri.Of(value, ends); return true;
            case Address.Kind.SpaceRecord: address = SpaceRecordUri.Of(value, ends); return true;
            default: return false;
        }
    }

    /// <summary>The address text of the active case.</summary>
    /// <returns>The canonical text.</returns>
    public override string ToString() => Value?.ToString() ?? "";
}
