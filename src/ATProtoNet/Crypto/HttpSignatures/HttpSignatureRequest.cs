using System.Text;

namespace ATProtoNet.Crypto.HttpSignatures;

/// <summary>The fields of an HTTP message, as RFC 9421 signs and verifies them; independent of any HTTP stack.</summary>
/// <remarks>
/// Names are case-insensitive. A field sent on several lines has the value of its lines, each trimmed, joined with
/// <c>", "</c> (<see href="https://www.rfc-editor.org/rfc/rfc9421#section-2.1"/>). Only field components are
/// supported; derived components such as <c>@method</c> are refused.
/// </remarks>
public sealed class HttpSignatureRequest
{
    private readonly Dictionary<string, List<string>> _fields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a message from its field lines.</summary>
    /// <param name="fields">Each field line's name and value, in order.</param>
    public HttpSignatureRequest(IEnumerable<KeyValuePair<string, string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var (name, value) in fields)
        {
            if (!_fields.TryGetValue(name, out var lines)) _fields[name] = lines = [];
            lines.Add(value.Trim(' ', '\t'));
        }
    }

    /// <summary>The value of the field <paramref name="name"/>, or <see langword="null"/> when it is absent.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The field lines' values joined with <c>", "</c>.</returns>
    public string? GetField(string name) => _fields.TryGetValue(name, out var lines) ? string.Join(", ", lines) : null;

    // The signature base of RFC 9421 §2.5 over covered (an inner list of component names), or null and why not.
    internal byte[]? SignatureBase(StructuredInnerList covered, out string error)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in covered.Items)
        {
            if (component is not { Value: string name, Parameters.Count: 0 } || name.Length == 0 || !name.All(IsFieldNameChar))
            {
                error = "A covered component is not a lower-case field name without parameters.";
                return null;
            }
            if (!seen.Add(name))
            {
                error = $"The component \"{name}\" is covered twice.";
                return null;
            }
            if (GetField(name) is not { } value || value.AsSpan().ContainsAny('\r', '\n'))
            {
                error = $"The covered field \"{name}\" is missing or not a single-line value.";
                return null;
            }
            sb.Append('"').Append(name).Append("\": ").Append(value).Append('\n');
        }
        error = "";
        return Encoding.UTF8.GetBytes(sb.Append("\"@signature-params\": ").Append(StructuredFields.Serialize(covered)).ToString());
    }

    // An RFC 9110 token character, lower case: a field name as a component identifier spells it.
    private static bool IsFieldNameChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') || "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);
}

/// <summary>The parameters of one HTTP message signature.</summary>
/// <remarks>Which components and parameters a protocol signs is that protocol's decision; this layer signs exactly what it is given.</remarks>
public sealed record HttpSignatureParameters
{
    /// <summary>The signature's label in <c>Signature-Input</c> and <c>Signature</c>, such as <c>atproto-space</c>.</summary>
    public required string Label { get; init; }

    /// <summary>The covered field names, lower case, in signing order.</summary>
    public required IReadOnlyList<string> Components { get; init; }

    /// <summary>The <c>keyid</c> parameter, such as the signer's <c>did:key</c>.</summary>
    public string? KeyId { get; init; }

    /// <summary>The <c>created</c> parameter; the caller reads it from its <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>The <c>expires</c> parameter.</summary>
    public DateTimeOffset? Expires { get; init; }
}

/// <summary>The two field values that carry a signature.</summary>
/// <param name="SignatureInput">The <c>Signature-Input</c> value, <c>label=(…);…</c>.</param>
/// <param name="Signature">The <c>Signature</c> value, <c>label=:…:</c>.</param>
public sealed record HttpSignatureFields(string SignatureInput, string Signature);

/// <summary>The outcome of verifying an HTTP message signature.</summary>
public closed record HttpSignatureVerdict
{
    /// <summary>The signature verifies.</summary>
    /// <param name="Parameters">The signature's label, covered components and parameters.</param>
    /// <param name="Key">The key it verified with.</param>
    public sealed record Accepted(HttpSignatureParameters Parameters, VerificationKey Key) : HttpSignatureVerdict;

    /// <summary>The signature is missing, malformed, does not cover what it must, or does not verify.</summary>
    /// <param name="Reason">Why, for logs.</param>
    public sealed record Refused(string Reason) : HttpSignatureVerdict;
}
