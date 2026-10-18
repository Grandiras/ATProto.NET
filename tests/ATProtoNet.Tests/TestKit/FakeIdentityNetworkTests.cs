using System.Text.Json;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.TestKit;

public class FakeIdentityNetworkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAccount_Plc_ServesDocumentHandleAndTxt()
    {
        var network = new FakeIdentityNetwork().AddAccount(TestIds.Did, TestIds.Handle, TestIds.Pds, TestKeys.K256Multibase);
        using var http = network.Server.CreateHttpClient();

        var document = JsonDocument.Parse(await http.GetStringAsync($"https://plc.directory/{TestIds.Did}", Ct)).RootElement;
        Assert.Equal(TestIds.Did, document.GetProperty("id").GetString());
        Assert.Equal(TestIds.Did, await http.GetStringAsync($"https://{TestIds.Handle}/.well-known/atproto-did", Ct));
        Assert.Equal([$"did={TestIds.Did}"], network.Txt($"_atproto.{TestIds.Handle}"));
    }

    [Fact]
    public async Task AddAccount_DidWeb_ServesWellKnownDocument()
    {
        const string did = "did:web:example.com";
        var network = new FakeIdentityNetwork().AddAccount(did, "example.com", TestIds.Pds, TestKeys.P256Multibase);
        using var http = network.Server.CreateHttpClient();
        var document = JsonDocument.Parse(await http.GetStringAsync("https://example.com/.well-known/did.json", Ct)).RootElement;
        Assert.Equal(did, document.GetProperty("id").GetString());
    }

    [Fact]
    public void Txt_UnknownName_IsEmpty() => Assert.Empty(new FakeIdentityNetwork().Txt("_atproto.nobody.test"));
}
