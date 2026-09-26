using ATProtoNet.Lexicon.App.Bsky.AgeAssurance;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.AgeAssurance;

public sealed class AgeAssuranceClientTests : IDisposable
{
    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public AgeAssuranceClientTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task BeginAsync_PostsTheInitiation_ReturnsTheState()
    {
        _handler.On("app.bsky.ageassurance.begin", """{"lastInitiatedAt":"2026-09-25T10:00:00.000Z","status":"pending","access":"unknown"}""");

        var state = await _client.Bsky.AgeAssurance.BeginAsync("alice@example.com", "en", "GB");

        Assert.Equal(
            """{"email":"alice@example.com","language":"en","countryCode":"GB"}""",
            Assert.Single(_handler.Requests).BodyText);
        Assert.Equal(AgeAssuranceStatus.Pending, state.Status);
        Assert.Equal(AgeAssuranceAccess.Unknown, state.Access);
        Assert.Equal("2026-09-25T10:00:00.000Z", state.LastInitiatedAt?.ToString());
    }

    [Fact]
    public async Task GetConfigAsync_BindsRegionsAndEveryRuleKind()
    {
        _handler.On("app.bsky.ageassurance.getConfig", """
            {"regions":[
              {"countryCode":"GB","minAccessAge":13,"rules":[
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAssuredOverAge","age":18,"access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAssuredUnderAge","age":18,"access":"safe"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfDeclaredOverAge","age":18,"access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfDeclaredUnderAge","age":13,"access":"none"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAccountNewerThan","date":"2025-07-25T00:00:00.000Z","access":"safe"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAccountOlderThan","date":"2025-07-25T00:00:00.000Z","access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfSomethingNew","access":"none"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleDefault","access":"safe"}]},
              {"countryCode":"US","regionCode":"TX","platforms":["ios","android"],"minAccessAge":13,"additionalVerificationMethods":["device"],"rules":[
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleDefault","access":"full"}]}
            ]}
            """);

        var config = await _client.Bsky.AgeAssurance.GetConfigAsync();

        Assert.Equal(HttpMethod.Get, Assert.Single(_handler.Requests).Method);
        Assert.Equal(2, config.Regions.Count);

        var gb = config.Regions[0];
        Assert.Equal(13, gb.MinAccessAge);
        Assert.Null(gb.Platforms);
        Assert.Equal(18, Assert.IsType<AssuredOverAgeRule>(gb.Rules[0]).Age);
        Assert.Equal(AgeAssuranceAccess.Safe, Assert.IsType<AssuredUnderAgeRule>(gb.Rules[1]).Access);
        Assert.IsType<DeclaredOverAgeRule>(gb.Rules[2]);
        Assert.Equal(AgeAssuranceAccess.None, Assert.IsType<DeclaredUnderAgeRule>(gb.Rules[3]).Access);
        Assert.Equal("2025-07-25T00:00:00.000Z", Assert.IsType<AccountNewerThanRule>(gb.Rules[4]).Date.ToString());
        Assert.IsType<AccountOlderThanRule>(gb.Rules[5]);
        Assert.Equal(
            "app.bsky.ageassurance.defs#configRegionRuleIfSomethingNew",
            Assert.IsType<UnknownAgeAssuranceRule>(gb.Rules[6]).Type);
        Assert.Equal(AgeAssuranceAccess.Safe, Assert.IsType<DefaultAgeRule>(gb.Rules[7]).Access);

        var texas = config.Regions[1];
        Assert.Equal("TX", texas.RegionCode);
        Assert.Equal(["ios", "android"], texas.Platforms);
        Assert.Equal(["device"], texas.AdditionalVerificationMethods);
    }

    [Fact]
    public async Task GetStateAsync_SendsTheRegion_BindsStateAndMetadata()
    {
        _handler.On("app.bsky.ageassurance.getState", """{"state":{"status":"assured","access":"full"},"metadata":{"accountCreatedAt":"2023-04-12T04:53:57.057Z"}}""");

        var response = await _client.Bsky.AgeAssurance.GetStateAsync("US", "TX");

        Assert.Equal("countryCode=US&regionCode=TX", Assert.Single(_handler.Requests).Query);
        Assert.Equal(AgeAssuranceStatus.Assured, response.State.Status);
        Assert.Equal(AgeAssuranceAccess.Full, response.State.Access);
        Assert.Null(response.State.LastInitiatedAt);
        Assert.Equal("2023-04-12T04:53:57.057Z", response.Metadata.AccountCreatedAt?.ToString());
    }
}
