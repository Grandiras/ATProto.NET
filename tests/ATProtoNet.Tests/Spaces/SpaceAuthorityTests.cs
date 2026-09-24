using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceAuthorityTests
{
    private const string Did = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";

    private static DidDocument Document(params ServiceEndpoint[] services) => new()
    {
        Id = Did,
        Service = [.. services],
    };

    private static ServiceEndpoint Pds => new()
    {
        Id = "#atproto_pds",
        Type = "AtprotoPersonalDataServer",
        Endpoint = "https://pds.example.com",
    };

    [Fact]
    public void GetServiceEndpoint_SpaceHostFragmentOnAnOrdinaryAccount_FallsBackToThePds()
    {
        // `{authority}#atproto_space_host` is what a repo host registers for a space's authority,
        // and an authority on an ordinary PDS publishes no such entry.
        Assert.Equal(
            "https://pds.example.com",
            SpaceAuthority.GetServiceEndpoint(Document(Pds), SpaceAuthority.HostServiceId));
    }

    [Fact]
    public void GetServiceEndpoint_PublishedSpaceHost_IsPreferredOverThePds()
    {
        var host = new ServiceEndpoint
        {
            Id = SpaceAuthority.HostServiceId,
            Type = SpaceAuthority.HostServiceType,
            Endpoint = "https://spaces.example.com",
        };

        Assert.Equal(
            "https://spaces.example.com",
            SpaceAuthority.GetServiceEndpoint(Document(Pds, host), "atproto_space_host"));
    }

    [Fact]
    public void GetServiceEndpoint_OtherFragment_DoesNotFallBack()
    {
        // A syncer that names an entry it does not publish is unreachable, not its PDS.
        Assert.Null(SpaceAuthority.GetServiceEndpoint(Document(Pds), "#atproto_space_syncer"));
    }
}
