using System.Net;
using System.Text;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// The shared cursor loop behind every <c>Enumerate*</c> method, and the enumerators built on it.
/// </summary>
public class PaginationTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Cid1 = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private readonly PagingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public PaginationTests()
    {
        _httpClient = new HttpClient(_handler);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Page(IReadOnlyList<int> Items, string? Cursor) : ICursorPage<int>;

    private static async Task<(List<int> Items, List<string?> Requested)> Walk(params Page[] pages)
    {
        var requested = new List<string?>();
        var items = new List<int>();
        var next = 0;
        await foreach (var item in Pagination.EnumerateAsync<Page, int>((cursor, _) =>
        {
            requested.Add(cursor);
            return Task.FromResult(pages[Math.Min(next++, pages.Length - 1)]);
        }))
        {
            items.Add(item);
        }

        return (items, requested);
    }

    [Fact]
    public async Task EnumerateAsync_FollowsCursorsUntilNull()
    {
        var (items, requested) = await Walk(new Page([1, 2], "a"), new Page([3], "b"), new Page([4], null));

        Assert.Equal([1, 2, 3, 4], items);
        Assert.Equal([null, "a", "b"], requested);
    }

    [Fact]
    public async Task EnumerateAsync_EmptyCursor_Stops()
    {
        var (items, requested) = await Walk(new Page([1], "a"), new Page([2], ""));

        Assert.Equal([1, 2], items);
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task EnumerateAsync_RepeatedCursor_StopsInsteadOfLoopingForever()
    {
        // The server hands back the same cursor forever; before the guard this never ended.
        var (items, requested) = await Walk(new Page([1], "same"), new Page([2], "same"));

        Assert.Equal([1, 2], items);
        Assert.Equal([null, "same"], requested);
    }

    [Fact]
    public async Task EnumerateAsync_CursorCycle_Stops()
    {
        var (_, requested) = await Walk(
            new Page([1], "a"), new Page([2], "b"), new Page([3], "a"), new Page([4], "b"));

        Assert.Equal([null, "a", "b"], requested);
    }

    [Fact]
    public async Task EnumerateAsync_EmptyPageWithCursor_KeepsGoing()
    {
        var (items, _) = await Walk(new Page([], "a"), new Page([1], null));

        Assert.Equal([1], items);
    }

    [Fact]
    public async Task EnumerateAsync_Cancelled_StopsBetweenPages()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<int>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in Pagination.EnumerateAsync<Page, int>(
                (cursor, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(new Page([cursor is null ? 1 : 2], cursor is null ? "a" : null));
                },
                cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        });

        Assert.Equal([1], seen);
    }

    [Fact]
    public async Task EnumerateRecordsAsync_ServerRepeatsItsCursor_EndsAfterTwoRequests()
    {
        _handler.Respond = _ => $$$"""{"cursor":"c1","records":[{"uri":"at://{{{DidText}}}/com.example.note/1","cid":"{{{Cid1}}}","value":{}}]}""";

        var records = new List<ATProtoNet.Lexicon.Com.AtProto.Repo.RecordEntry>();
        await foreach (var record in _client.Repo.EnumerateRecordsAsync(
            Did.Parse(DidText), Nsid.Parse("com.example.note"), pageSize: 10))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.DoesNotContain("cursor=", _handler.Requests[0]);
        Assert.Contains("cursor=c1", _handler.Requests[1]);
        Assert.Contains("limit=10", _handler.Requests[0]);
    }

    [Fact]
    public async Task EnumerateBlobsAsync_WalksEveryPage()
    {
        _handler.Respond = request => request.Contains("cursor=")
            ? $$"""{"cids":["{{Cid1}}"]}"""
            : $$"""{"cursor":"next","cids":["{{Cid1}}","{{Cid1}}"]}""";

        var cids = new List<Cid>();
        await foreach (var cid in _client.Sync.EnumerateBlobsAsync(Did.Parse(DidText)))
            cids.Add(cid);

        Assert.Equal(3, cids.Count);
        Assert.All(cids, cid => Assert.Equal(Cid1, cid.Value));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task EnumerateAsync_OnARecordCollection_UsesThePaginator()
    {
        _handler.Respond = request => request.Contains("createSession")
            ? $$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}"""
            : $$$"""{"cursor":"same","records":[{"uri":"at://{{{DidText}}}/com.example.note/1","cid":"{{{Cid1}}}","value":{"text":"hi"}}]}""";
        await _client.LoginAsync("alice.test", "password");

        var notes = new List<RecordView<Note>>();
        await foreach (var note in _client.GetCollection<Note>(Nsid.Parse("com.example.note")).EnumerateAsync())
            notes.Add(note);

        Assert.Equal(2, notes.Count);
        Assert.Equal("1", notes[0].RecordKey.Value);
    }

    private sealed class Note
    {
        public string? Text { get; set; }
    }

    private sealed class PagingHandler : HttpMessageHandler
    {
        public Func<string, string> Respond { get; set; } = _ => "{}";

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            if (!uri.Contains("createSession"))
                Requests.Add(uri);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Respond(uri), Encoding.UTF8, "application/json"),
            });
        }
    }
}
