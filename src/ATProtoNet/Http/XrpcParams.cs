using System.Collections;
using System.Globalization;

namespace ATProtoNet.Http;

/// <summary>
/// Accumulates XRPC query parameters in call order.
/// </summary>
/// <remarks>
/// Null values are dropped, booleans render as <c>true</c>/<c>false</c>, numbers use the
/// invariant culture, and <see cref="AddAll"/> appends one pair per element so array
/// parameters are transmitted as repeated keys (<c>uris=a&amp;uris=b</c>) per the XRPC
/// convention rather than a single comma-joined value.
/// </remarks>
internal sealed class XrpcParams : IEnumerable<KeyValuePair<string, string?>>
{
    private readonly List<KeyValuePair<string, string?>> _pairs = [];

    /// <summary>Appends a string parameter, ignoring nulls.</summary>
    public XrpcParams Add(string key, string? value)
    {
        if (value is not null)
            _pairs.Add(new KeyValuePair<string, string?>(key, value));
        return this;
    }

    /// <summary>Appends an integer parameter, ignoring nulls.</summary>
    public XrpcParams Add(string key, int? value) =>
        Add(key, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends a boolean parameter as <c>true</c>/<c>false</c>, ignoring nulls.</summary>
    public XrpcParams Add(string key, bool? value) =>
        Add(key, value is null ? null : value.Value ? "true" : "false");

    /// <summary>
    /// Appends one parameter per element, all sharing <paramref name="key"/>.
    /// A null or empty sequence contributes nothing.
    /// </summary>
    public XrpcParams AddAll(string key, IEnumerable<string>? values)
    {
        if (values is not null)
        {
            foreach (var value in values)
                Add(key, value);
        }

        return this;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => _pairs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
