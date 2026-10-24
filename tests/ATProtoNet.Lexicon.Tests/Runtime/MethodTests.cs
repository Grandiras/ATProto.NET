using System.Text.Json;
using ATProtoNet.Lexicon.Runtime;
using ATProtoNet.Xrpc;
using Golden.Lexicons.Test.Golden;
using Golden.Lexicons.Test.Golden.Chat;
using GoldenPost = Golden.Lexicons.Test.Golden.Post;

namespace ATProtoNet.Lexicon.Tests.Runtime;

// Descriptors, parameters and namespace clients (LEX-60–LEX-71), against the stub client of Stubs/.
public sealed class MethodTests
{
    [Fact]
    [Trait("Spec", "LEX-63")]
    public void WriteTo_Parameters_InDeclarationOrderWithRepeatedArraysAndInvariantText()
    {
        var writer = new XrpcParameterWriter();

        new GetFeed.Parameters { Actor = AtIdentifier.Parse("alice.test"), Limit = 50, Tags = ["a", "b"], Filter = GetFeed.FilterValue.PostsNoReplies, Since = Tid.Parse("3kq5wcfxw4k2a") }.WriteTo(writer);

        Assert.Equal([("actor", "alice.test"), ("limit", "50"), ("filter", "posts_no_replies"), ("tags", "a"), ("tags", "b"), ("since", "3kq5wcfxw4k2a")],
            writer.Pairs.Select(p => (p.Key, p.Value)));
    }

    [Fact]
    [Trait("Spec", "LEX-63")]
    public void ReadFrom_Pairs_BindsEveryParameter()
    {
        var parameters = GetFeed.Parameters.ReadFrom(new([new("actor", "did:plc:ewvi7nxzyoun6zhxrhs64oiz"), new("limit", "10"), new("tags", "a"), new("tags", "b"), new("filter", "other")]));

        Assert.Equal(new GetFeed.Parameters { Actor = AtIdentifier.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), Limit = 10, Tags = parameters.Tags, Filter = "other" }, parameters);
        Assert.Equal(["a", "b"], parameters.Tags!);
    }

    [Theory]
    [Trait("Spec", "LEX-63")]
    [InlineData("limit=10", "actor")]
    [InlineData("actor=not a handle", "actor")]
    [InlineData("actor=a.test&actor=b.test", "actor")]
    [InlineData("actor=alice.test&limit=ten", "limit")]
    [InlineData("actor=alice.test&limit=99999999999", "limit")]
    public void ReadFrom_MissingRepeatedOrInvalidParameter_FailsNamingIt(string query, string named)
    {
        var pairs = query.Split('&').Select(p => p.Split('=')).Select(p => new KeyValuePair<string, string>(p[0], p[1]));

        var exception = Assert.Throws<FormatException>(() => GetFeed.Parameters.ReadFrom(new(pairs)));

        Assert.Contains($"\"{named}\"", exception.Message);
    }

    [Fact]
    [Trait("Spec", "LEX-60")]
    public void Descriptor_CarriesTheLexiconsShape()
    {
        Assert.Equal("test.golden.getFeed", GetFeed.Descriptor.Nsid.Value);
        Assert.Equal(["BlockedActor", "UnknownFeed"], GetFeed.Descriptor.Errors);
        Assert.Equal("application/vnd.ipld.car", GetRepo.Descriptor.OutputEncoding);
        Assert.Equal("*/*", UploadBlob.Descriptor.InputEncoding);
        Assert.Equal("did:web:chat.example#chat", SendMessage.Descriptor.DefaultProxy);
        Assert.Null(GetFeed.Descriptor.DefaultProxy);
        Assert.IsType<XrpcProcedure<UploadPart.Parameters, XrpcBinaryContent, XrpcEmpty>>(UploadPart.Descriptor);
        Assert.IsType<XrpcSubscription<Subscribe.Parameters, SubscribeMessage>>(Subscribe.Descriptor);
    }

    [Fact]
    [Trait("Spec", "LEX-61")]
    public void Descriptor_ProcedureWithoutOutput_HasNoOutputToParse()
    {
        Assert.IsType<XrpcProcedure<XrpcEmpty>>(DeleteThing.Descriptor);
        Assert.Null(DeleteThing.Descriptor.OutputEncoding);
    }

    [Fact]
    [Trait("Spec", "LEX-64")]
    public void Errors_AreConstantsOfTheDeclaredNames() => Assert.Equal("UnknownFeed", GetFeed.Errors.UnknownFeed);

    [Fact]
    [Trait("Spec", "LEX-62")]
    public async Task Client_AllOptionalInputWithoutArguments_SendsAnEmptyObject()
    {
        var client = new AtProtoClient();

        await client.Golden.CreateThingAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.Golden.DeleteThingAsync(TestContext.Current.CancellationToken);

        Assert.Equal("{}", JsonSerializer.Serialize((CreateThing.Input)client.Calls[0].Input!, CreateThing.Descriptor.Input!));
        Assert.IsType<XrpcEmpty>(client.Calls[1].Input);
    }

    [Fact]
    [Trait("Spec", "LEX-71")]
    public async Task Enumerate_PaginatedQuery_FollowsTheCursorToTheLastPage()
    {
        var pages = new Dictionary<string, GetFeed.Output>
        {
            [""] = new() { Feed = [Post("1")], Cursor = "a" },
            ["a"] = new() { Feed = [Post("2"), Post("3")] },
        };
        var client = new AtProtoClient((_, parameters, _) => pages[((GetFeed.Parameters)parameters!).Cursor ?? ""]);

        var texts = await client.Golden.EnumerateFeedAsync(new() { Actor = AtIdentifier.Parse("alice.test") }, TestContext.Current.CancellationToken).Select(p => p.Text).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3"], texts);
        Assert.Equal([null, "a"], client.Calls.Select(c => ((GetFeed.Parameters)c.Parameters!).Cursor));
    }

    [Fact]
    [Trait("Spec", "LEX-70")]
    public void Output_WithCursorAndOneArray_IsACursorPage()
    {
        ICursorPage<GoldenPost> page = new GetFeed.Output { Feed = [Post("1")], Cursor = "c" };

        Assert.Equal("c", page.Cursor);
        Assert.Single(page.Items);
        Assert.True(typeof(ICursorParameters<GetFeed.Parameters>).IsAssignableFrom(typeof(GetFeed.Parameters)));
    }

    [Fact]
    [Trait("Spec", "LEX-69")]
    public void ExperimentalNsid_IsMarked() =>
        Assert.Equal("ATPROTO002", Assert.Single(typeof(Golden.Lexicons.Test.Golden.Labs.GetTrend).GetCustomAttributes(typeof(System.Diagnostics.CodeAnalysis.ExperimentalAttribute), false)
            .Cast<System.Diagnostics.CodeAnalysis.ExperimentalAttribute>()).DiagnosticId);

    [Fact]
    [Trait("Spec", "LEX-75")]
    public void PermissionSet_Include_BuildsTheIncludeScope()
    {
        Assert.Equal("test.golden.authFull", AuthFull.Ref.Nsid.Value);
        Assert.Equal("include:test.golden.authFull", AuthFull.Include().Value);
    }

    [Fact]
    [Trait("Spec", "LEX-76")]
    public void Space_Declaration_CarriesNameAndCollections()
    {
        var declaration = Group.Declaration;

        Assert.Equal(("test.golden.group", "Group", "Gruppe"), (declaration.Type.Value, declaration.Name, declaration.LocalizedNames!["de"]));
        Assert.Equal(["test.golden.post"], declaration.Collections);
    }

    private static GoldenPost Post(string text) => new() { Text = text, Kind = "note" };
}
