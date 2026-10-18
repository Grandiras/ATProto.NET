using System.Text.Json;

namespace ATProtoNet.TestKit;

/// <summary>
/// DID documents, handles and the PLC directory, served through <see cref="Server"/> so the real
/// resolvers run against it; DNS TXT records are answered by <see cref="Txt"/>.
/// </summary>
public sealed class FakeIdentityNetwork
{
    private readonly Dictionary<string, List<string>> _txt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The PLC directory's host.</summary>
    public const string PlcHost = "plc.directory";

    /// <summary>The HTTP side of the network: PLC, <c>did:web</c> documents and handle well-known files.</summary>
    public FakeXrpcServer Server { get; } = new();

    /// <summary>Publishes a <c>did:plc</c> document in the directory.</summary>
    public FakeIdentityNetwork AddPlc(string did, string documentJson)
    {
        Server.On($"{PlcHost}/{did}", documentJson);
        return this;
    }

    /// <summary>Publishes a <c>did:web</c> document at its well-known location.</summary>
    public FakeIdentityNetwork AddWeb(string did, string documentJson)
    {
        var host = Uri.UnescapeDataString(did["did:web:".Length..]);
        Server.On($"{host}/.well-known/did.json", documentJson);
        return this;
    }

    /// <summary>Points a handle at a DID over DNS TXT, HTTPS well-known, or both.</summary>
    public FakeIdentityNetwork AddHandle(string handle, string did, bool dns = true, bool https = true)
    {
        if (dns) AddTxt($"_atproto.{handle}", $"did={did}");
        if (https) Server.On($"{handle}/.well-known/atproto-did", _ => Responses.Text(did));
        return this;
    }

    /// <summary>Adds a raw TXT record, for conflicting or malformed answers.</summary>
    public FakeIdentityNetwork AddTxt(string name, string value)
    {
        if (!_txt.TryGetValue(name, out var values)) _txt[name] = values = [];
        values.Add(value);
        return this;
    }

    /// <summary>The TXT records of a name; empty when it has none.</summary>
    public IReadOnlyList<string> Txt(string name) => _txt.TryGetValue(name, out var values) ? values : [];

    /// <summary>Publishes an account: its DID document (PLC or web, by its DID method) and its handle.</summary>
    public FakeIdentityNetwork AddAccount(string did, string handle, string pds, string signingKeyMultibase)
    {
        var document = DidDocument(did, handle, pds, signingKeyMultibase);
        _ = did.StartsWith("did:web:", StringComparison.Ordinal) ? AddWeb(did, document) : AddPlc(did, document);
        return AddHandle(handle, did);
    }

    /// <summary>A minimal DID document with one handle, a PDS and an <c>#atproto</c> signing key.</summary>
    public static string DidDocument(string did, string handle, string pds, string signingKeyMultibase) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["@context"] = new[] { "https://www.w3.org/ns/did/v1", "https://w3id.org/security/multikey/v1" },
        ["id"] = did,
        ["alsoKnownAs"] = new[] { $"at://{handle}" },
        ["verificationMethod"] = new[] { new { id = $"{did}#atproto", type = "Multikey", controller = did, publicKeyMultibase = signingKeyMultibase } },
        ["service"] = new[] { new { id = "#atproto_pds", type = "AtprotoPersonalDataServer", serviceEndpoint = pds } },
    });
}
