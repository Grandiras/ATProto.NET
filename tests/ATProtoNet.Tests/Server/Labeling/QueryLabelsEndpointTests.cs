using System.Net;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Labeling;
using ATProtoNet.Server.Xrpc;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Labeling;

public sealed class QueryLabelsEndpointTests : IAsyncLifetime
{
    private const string Labeler = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string OtherLabeler = "did:plc:ar7c4by46qjdydhdevvrndac";
    private const string Alice = "did:plc:44ybard66vv44zksje25o7dz";
    private const string AlicePost = "at://did:plc:44ybard66vv44zksje25o7dz/app.bsky.feed.post/3l6oveex3ii2l";
    private const string Bob = "did:plc:qx7s5vdotumqa4h52ejojeby";

    private readonly AtProtoKey _key = AtProtoCrypto.GenerateP256Key();
    private readonly InMemoryLabelSource _source = new();
    private XrpcTestHost? _host;

    private HttpClient Client => _host!.Client;

    public async ValueTask InitializeAsync()
    {
        _source.Add(Unsigned(Labeler, Alice, "spam"));
        _source.Add(Unsigned(Labeler, AlicePost, "porn"));
        _source.Add(Unsigned(OtherLabeler, AlicePost, "!hide"));
        _source.Add(Unsigned(Labeler, Bob, "bot"));

        _host = await XrpcTestHost.StartAsync(services =>
        {
            services.AddSingleton<ILabelSource>(_source);
            services.AddSingleton(new LabelSigner(Did.Parse(Labeler), _key));
            services.AddXrpcEndpoint<QueryLabelsEndpoint>();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        _key.Dispose();
    }

    private static Label Unsigned(string src, string uri, string val) => new()
    {
        Src = Did.Parse(src),
        Uri = uri,
        Val = val,
        Cts = AtDatetime.Parse("2026-09-26T10:00:00.000Z"),
    };

    private async Task<QueryLabelsResponse> QueryAsync(string query)
    {
        var response = await Client.GetAsync($"/xrpc/com.atproto.label.queryLabels?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<QueryLabelsResponse>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), AtProtoJsonDefaults.Options)!;
    }

    [Fact]
    public async Task Query_Wildcard_ReturnsEveryLabelSignedByItsLabeler()
    {
        var page = await QueryAsync("uriPatterns=*");

        Assert.Equal(["spam", "porn", "!hide", "bot"], page.Labels.Select(l => l.Val));

        // This labeler's labels are signed on the way out; another labeler's are left alone.
        foreach (var label in page.Labels.Where(l => l.Src.Value == Labeler))
            Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(label, _key.ToDidKey()));
        Assert.Null(page.Labels.Single(l => l.Src.Value == OtherLabeler).Sig);
    }

    [Fact]
    public async Task Query_PrefixAndExactPatterns_MatchAsTheLexiconDescribes()
    {
        var prefix = await QueryAsync($"uriPatterns={Uri.EscapeDataString("at://did:plc:44ybard66vv44zksje25o7dz/*")}");
        var exact = await QueryAsync($"uriPatterns={Alice}&uriPatterns={Bob}");

        Assert.Equal(["porn", "!hide"], prefix.Labels.Select(l => l.Val));
        Assert.Equal(["spam", "bot"], exact.Labels.Select(l => l.Val));
    }

    [Fact]
    public async Task Query_Sources_FiltersByLabeler()
    {
        var page = await QueryAsync($"uriPatterns=*&sources={OtherLabeler}");

        Assert.Equal("!hide", Assert.Single(page.Labels).Val);
    }

    [Fact]
    public async Task Query_LimitAndCursor_Page()
    {
        var first = await QueryAsync("uriPatterns=*&limit=3");
        var second = await QueryAsync($"uriPatterns=*&limit=3&cursor={first.Cursor}");

        Assert.Equal(3, first.Labels.Count);
        Assert.Equal("bot", Assert.Single(second.Labels).Val);
        Assert.Null(second.Cursor);
    }

    [Fact]
    public async Task Query_Limit_DefaultsTo50AndIsCappedAt250()
    {
        await QueryAsync("uriPatterns=*");
        Assert.Equal(QueryLabelsEndpoint.DefaultLimit, _source.LastQuery!.Limit);

        await QueryAsync("uriPatterns=*&limit=1000");
        Assert.Equal(QueryLabelsEndpoint.MaxLimit, _source.LastQuery!.Limit);
    }

    [Theory]
    [InlineData("uriPatterns=at://did:plc:44ybard66vv44zksje25o7dz/*/self")]
    [InlineData("uriPatterns=**")]
    [InlineData("uriPatterns=*&limit=0")]
    [InlineData("uriPatterns=*&sources=not-a-did")]
    [InlineData("limit=5")]
    public async Task Query_InvalidRequest_Answers400(string query)
    {
        var response = await Client.GetAsync($"/xrpc/com.atproto.label.queryLabels?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(XrpcErrors.InvalidRequest, (await XrpcTestHost.ReadErrorAsync(response)).Error);
    }

    [Fact]
    public async Task Query_AlreadySignedLabel_IsServedAsStored()
    {
        var signed = new LabelSigner(Did.Parse(Labeler), _key).Sign(Unsigned(Labeler, "did:plc:riczjqh4u2cwtpzmhphy5c6n", "spam"));
        _source.Add(signed);

        var page = await QueryAsync("uriPatterns=did:plc:riczjqh4u2cwtpzmhphy5c6n");

        Assert.Equal(signed.Sig, Assert.Single(page.Labels).Sig);
    }

    [Fact]
    public async Task Query_WithoutASigner_ServesLabelsAsStored()
    {
        await using var host = await XrpcTestHost.StartAsync(services =>
        {
            services.AddSingleton<ILabelSource>(_source);
            services.AddXrpcEndpoint<QueryLabelsEndpoint>();
        });

        var response = await host.Client.GetAsync(
            $"/xrpc/com.atproto.label.queryLabels?uriPatterns={Alice}", TestContext.Current.CancellationToken);
        var page = JsonSerializer.Deserialize<QueryLabelsResponse>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), AtProtoJsonDefaults.Options)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(page.Labels).Sig);
    }

    [Fact]
    public void LabelQuery_Matches_AppliesPatternsAndSources()
    {
        var query = new LabelQuery
        {
            UriPatterns = ["at://did:plc:44ybard66vv44zksje25o7dz/*", Bob],
            Sources = [Did.Parse(Labeler)],
            Limit = 50,
        };

        Assert.True(query.Matches(Unsigned(Labeler, AlicePost, "spam")));
        Assert.True(query.Matches(Unsigned(Labeler, Bob, "spam")));
        Assert.False(query.Matches(Unsigned(Labeler, Alice, "spam")));
        Assert.False(query.Matches(Unsigned(OtherLabeler, AlicePost, "spam")));
        Assert.False(query.MatchesAllSubjects);
        Assert.True(new LabelQuery { UriPatterns = ["*"], Limit = 1 }.Matches(Unsigned(OtherLabeler, Alice, "x")));
    }

    /// <summary>A label source over a list, cursored by position.</summary>
    private sealed class InMemoryLabelSource : ILabelSource
    {
        private readonly List<Label> _labels = [];

        public LabelQuery? LastQuery { get; private set; }

        public void Add(Label label) => _labels.Add(label);

        public Task<QueryLabelsResponse> QueryLabelsAsync(LabelQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;

            var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var matching = _labels.Select((label, index) => (label, index))
                .Where(entry => entry.index >= start && query.Matches(entry.label))
                .Take(query.Limit + 1)
                .ToList();

            var page = matching.Take(query.Limit).ToList();
            return Task.FromResult(new QueryLabelsResponse
            {
                Labels = [.. page.Select(entry => entry.label)],
                Cursor = matching.Count > query.Limit
                    ? (page[^1].index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null,
            });
        }
    }
}
