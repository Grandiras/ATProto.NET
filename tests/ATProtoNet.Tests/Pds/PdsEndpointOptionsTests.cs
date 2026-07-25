using ATProtoNet.Pds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;

namespace ATProtoNet.Tests.Pds;

public class PdsEndpointOptionsTests
{
    private sealed record TestMarker(string Value);

    /// <summary>
    /// Spins up a test server with the PDS endpoints mapped under <paramref name="configure"/>,
    /// optionally mapping extra host-owned routes afterwards. A probe middleware copies the
    /// matched endpoint's display name and marker metadata onto the response so conventions
    /// are observable from the client side.
    /// </summary>
    private static async Task<(IHost Host, HttpClient Client)> CreateServerAsync(
        Action<PdsEndpointOptions>? configure,
        Action<IEndpointRouteBuilder>? extraRoutes = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddAtProtoPds(opts =>
                    {
                        opts.Hostname = "test.local";
                        opts.PublicUrl = "https://test.local";
                        opts.OpenRegistration = true;
                    });
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (ctx, next) =>
                    {
                        var endpoint = ctx.GetEndpoint();
                        if (endpoint is not null)
                        {
                            ctx.Response.Headers["X-Endpoint-Name"] = endpoint.DisplayName ?? "";
                            if (endpoint.Metadata.GetMetadata<TestMarker>() is { } marker)
                                ctx.Response.Headers["X-Marker"] = marker.Value;
                        }

                        await next();
                    });
                    app.UseEndpoints(endpoints =>
                    {
                        if (configure is null)
                            endpoints.MapAtProtoPds();
                        else
                            endpoints.MapAtProtoPds(configure);

                        extraRoutes?.Invoke(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return (host, host.GetTestServer().CreateClient());
    }

    [Fact]
    public async Task MapAtProtoPds_NoOptions_MapsEveryEndpoint()
    {
        var (host, client) = await CreateServerAsync(configure: null);
        using var _ = host;

        var response = await client.GetAsync("/xrpc/com.atproto.server.describeServer");
        response.EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "all.test.local", email = "all@test.local", password = "password123" });
        create.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Exclude_EndpointIsNotMapped()
    {
        var (host, client) = await CreateServerAsync(o => o.Exclude(PdsEndpointNames.CreateAccount));
        using var _ = host;

        var response = await client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "nope.test.local", email = "nope@test.local", password = "password123" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exclude_LeavesOtherEndpointsMapped()
    {
        var (host, client) = await CreateServerAsync(o => o.Exclude(PdsEndpointNames.CreateAccount));
        using var _ = host;

        var response = await client.GetAsync("/xrpc/com.atproto.server.describeServer");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Exclude_HostCanMapItsOwnImplementationOnTheSameRoute()
    {
        // The whole point of Exclude: no ambiguous-match conflict, and the host's
        // handler is the one that runs.
        var (host, client) = await CreateServerAsync(
            o => o.Exclude(PdsEndpointNames.CreateAccount),
            endpoints => endpoints.MapPost("/xrpc/com.atproto.server.createAccount",
                () => Results.Json(new { error = "InvalidInviteCode", message = "Invite required." }, statusCode: 400)));
        using var _ = host;

        var response = await client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "custom.test.local", email = "custom@test.local", password = "password123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("InvalidInviteCode", body!["error"]);
    }

    [Fact]
    public async Task Only_MapsListedEndpointsAndSkipsTheRest()
    {
        var (host, client) = await CreateServerAsync(
            o => o.Only(PdsEndpointNames.DescribeServer, PdsEndpointNames.CreateAccount));
        using var _ = host;

        (await client.GetAsync("/xrpc/com.atproto.server.describeServer")).EnsureSuccessStatusCode();

        var listRecords = await client.GetAsync(
            "/xrpc/com.atproto.repo.listRecords?repo=did:plc:test&collection=app.bsky.feed.post");
        Assert.Equal(HttpStatusCode.NotFound, listRecords.StatusCode);
    }

    [Fact]
    public async Task Only_CombinedWithExclude_ExcludeWins()
    {
        var (host, client) = await CreateServerAsync(o => o
            .Only(PdsEndpointNames.DescribeServer, PdsEndpointNames.CreateAccount)
            .Exclude(PdsEndpointNames.CreateAccount));
        using var _ = host;

        (await client.GetAsync("/xrpc/com.atproto.server.describeServer")).EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount",
            new { handle = "x.test.local", email = "x@test.local", password = "password123" });
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Fact]
    public async Task MappedEndpoints_CarryTheirNsidAsDisplayName()
    {
        var (host, client) = await CreateServerAsync(configure: null);
        using var _ = host;

        var response = await client.GetAsync("/xrpc/com.atproto.server.describeServer");

        Assert.Equal(PdsEndpointNames.DescribeServer, response.Headers.GetValues("X-Endpoint-Name").Single());
    }

    [Fact]
    public async Task Configure_AppliesConventionToThatEndpointOnly()
    {
        var (host, client) = await CreateServerAsync(
            o => o.Configure(PdsEndpointNames.DescribeServer, b => b.WithMetadata(new TestMarker("described"))));
        using var _ = host;

        var described = await client.GetAsync("/xrpc/com.atproto.server.describeServer");
        Assert.Equal("described", described.Headers.GetValues("X-Marker").Single());

        var other = await client.GetAsync("/xrpc/com.atproto.server.getSession");
        Assert.False(other.Headers.Contains("X-Marker"));
    }

    [Fact]
    public async Task ConfigureAll_AppliesConventionToEveryMappedEndpoint()
    {
        var (host, client) = await CreateServerAsync(
            o => o.ConfigureAll((nsid, b) => b.WithMetadata(new TestMarker(nsid))));
        using var _ = host;

        var described = await client.GetAsync("/xrpc/com.atproto.server.describeServer");
        Assert.Equal(PdsEndpointNames.DescribeServer, described.Headers.GetValues("X-Marker").Single());

        var session = await client.GetAsync("/xrpc/com.atproto.server.getSession");
        Assert.Equal(PdsEndpointNames.GetSession, session.Headers.GetValues("X-Marker").Single());
    }

    // ── Option object unit tests (no host required) ──

    [Fact]
    public void IsMapped_ByDefault_AllEndpointsMapped()
    {
        var options = new PdsEndpointOptions();
        Assert.All(PdsEndpointNames.All, nsid => Assert.True(options.IsMapped(nsid)));
    }

    [Fact]
    public void IsMapped_AfterExclude_ReturnsFalseForExcluded()
    {
        var options = new PdsEndpointOptions().Exclude(PdsEndpointNames.UploadBlob, PdsEndpointNames.GetBlob);

        Assert.False(options.IsMapped(PdsEndpointNames.UploadBlob));
        Assert.False(options.IsMapped(PdsEndpointNames.GetBlob));
        Assert.True(options.IsMapped(PdsEndpointNames.GetRecord));
    }

    [Fact]
    public void Only_CalledTwice_UnionsTheSets()
    {
        var options = new PdsEndpointOptions()
            .Only(PdsEndpointNames.GetRecord)
            .Only(PdsEndpointNames.ListRecords);

        Assert.True(options.IsMapped(PdsEndpointNames.GetRecord));
        Assert.True(options.IsMapped(PdsEndpointNames.ListRecords));
        Assert.False(options.IsMapped(PdsEndpointNames.PutRecord));
    }

    [Fact]
    public void Exclude_UnknownNsid_Throws()
    {
        var options = new PdsEndpointOptions();
        var ex = Assert.Throws<ArgumentException>(() => options.Exclude("com.atproto.server.createAcount"));
        Assert.Contains("is not a PDS endpoint", ex.Message);
    }

    [Fact]
    public void Configure_UnknownNsid_Throws()
    {
        var options = new PdsEndpointOptions();
        Assert.Throws<ArgumentException>(() => options.Configure("com.example.nope", _ => { }));
    }
}
