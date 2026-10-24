using System.Text;
using System.Text.Json.Nodes;
using ATProtoNet.Lexicon.Cli;
using ATProtoNet.TestKit;

namespace ATProtoNet.Lexicon.Tests.Cli;

/// <summary><c>install</c> and <c>update</c> from git archives served by a fake codeload, and from local directories.</summary>
public sealed class InstallUpdateTests : LexgenHarness
{
    private const string First = "1111111111111111111111111111111111111111";
    private const string Second = "2222222222222222222222222222222222222222";
    private const string Url = "https://codeload.github.com/example/lexicons/tar.gz/";

    private static readonly string Other = Document("org.other.thing");

    public InstallUpdateTests()
    {
        Write("proposals/notes.json", "{}");
        Write("lexicons.json", $$"""
        {
          "version": 1,
          "sources": {
            "example": {
              "type": "git",
              "repository": "example/lexicons",
              "commit": "{{First}}",
              "path": "lexicons",
              "namespaces": []
            },
            "proposals": { "type": "directory", "path": "proposals" },
            "site": { "type": "network", "did": "did:plc:re3ebnp5v7ffagz6rb6xfei4", "prefix": "site.example." }
          },
          "lexicons": {}
        }
        """);
    }

    [Fact]
    [Trait("Spec", "TOO-14")]
    [Trait("Spec", "TOO-16")]
    public async Task Install_GitNamespacePattern_CopiesItsFilesByteForByte()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem), ("com/example/todo/authFull.json", TodoAuthFull), ("org/other/thing.json", Other));

        var (exitCode, output, _) = await RunAsync("install", "com.example.todo.*", "--source", "example");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(Url + First, Assert.Single(Http.Requests));
        Assert.Equal("added com.example.todo.authFull\nadded com.example.todo.item\n", output.ReplaceLineEndings("\n"));
        Assert.Equal(TodoItem, Text("lexicons/com/example/todo/item.json"));
        Assert.False(Files.FileExists("lexicons/org/other/thing.json"));
        var manifest = Manifest();
        Assert.Equal(Cid(TodoItem), (string?)manifest["lexicons"]!["com.example.todo.item"]!["cid"]);
        Assert.Equal("example", (string?)manifest["lexicons"]!["com.example.todo.item"]!["source"]);
        Assert.Equal("com/example/todo", (string?)manifest["sources"]!["example"]!["namespaces"]![0]);
        Assert.Equal("2026-10-24", (string?)manifest["sources"]!["example"]!["fetched"]);
        Assert.Equal(0, (await RunAsync("generate", "--out", "out")).ExitCode);
    }

    [Fact]
    [Trait("Spec", "TOO-14")]
    public async Task Install_NsidCoveredByASourceNamespace_PicksThatSource()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        var manifest = Manifest();
        manifest["sources"]!["example"]!["namespaces"] = new JsonArray("com/example");
        Files.WriteAllBytes("lexicons.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        var (exitCode, output, _) = await RunAsync("install", "com.example.todo.item");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("added com.example.todo.item", output);
    }

    [Fact]
    [Trait("Spec", "TOO-14")]
    public async Task Install_DirectorySource_CopiesTheFile()
    {
        Write("proposals/com/example/todo/item.json", TodoItem);
        Write("proposals/notes.json", "{}");

        var (exitCode, _, _) = await RunAsync("install", "com.example.todo.item", "--source", "proposals");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(TodoItem, Text("lexicons/com/example/todo/item.json"));
        Assert.Equal("proposals", (string?)Manifest()["lexicons"]!["com.example.todo.item"]!["source"]);
        Assert.Empty(Http.Requests);
    }

    [Fact]
    [Trait("Spec", "TOO-14")]
    public async Task Install_SameLexiconAgain_ChangesNothing()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        await RunAsync("install", "com.example.todo.item", "--source", "example");
        var before = Files.ReadAllBytes("lexicons.json");
        Time.Advance(TimeSpan.FromDays(3));

        var (exitCode, output, error) = await RunAsync("install", "com.example.todo.item", "--source", "example");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(output);
        Assert.Contains("Nothing changed", error);
        Assert.Equal(before, Files.ReadAllBytes("lexicons.json"));
    }

    [Fact]
    [Trait("Spec", "TOO-17")]
    public async Task Install_References_InstallsThemTransitively()
    {
        Serve(First, ("com/example/a.json", Document("com.example.a", "com.example.b#main")), ("com/example/b.json", Document("com.example.b", "com.example.c")),
            ("com/example/c.json", Document("com.example.c")), ("com/example/unrelated.json", Document("com.example.unrelated")));

        var (exitCode, output, _) = await RunAsync("install", "com.example.a", "--source", "example");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("added com.example.a\nadded com.example.b\nadded com.example.c\n", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Spec", "TOO-17")]
    public async Task Install_ReferenceProvidedByThePattern_IsNotInstalled()
    {
        Serve(First, ("com/example/a.json", Document("com.example.a", "com.atproto.repo.strongRef")), ("com/atproto/repo/strongRef.json", Document("com.atproto.repo.strongRef")));
        var manifest = Manifest();
        manifest["provided"] = new JsonArray("com.atproto.*");
        Files.WriteAllBytes("lexicons.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        var (exitCode, output, _) = await RunAsync("install", "com.example.a", "--source", "example");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("added com.example.a\n", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Spec", "TOO-17")]
    public async Task Install_ReferenceDefinedByAReferencedAssembly_IsNotInstalled()
    {
        // This assembly compiles the generated first-party Lexicons, so it defines com.atproto.repo.strongRef.
        Files.WriteAllBytes("refs/Lexicons.dll", File.ReadAllBytes(typeof(InstallUpdateTests).Assembly.Location));
        Serve(First, ("com/example/a.json", Document("com.example.a", "com.atproto.repo.strongRef")), ("com/atproto/repo/strongRef.json", Document("com.atproto.repo.strongRef")));

        var (exitCode, output, _) = await RunAsync("install", "com.example.a", "--source", "example", "--reference", "refs/Lexicons.dll");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("added com.example.a\n", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Spec", "TOO-17")]
    public async Task Install_ReferenceNowhere_FailsAndWritesNothing()
    {
        Serve(First, ("com/example/a.json", Document("com.example.a", "com.example.missing")));
        var before = Files.ReadAllBytes("lexicons.json");

        var (exitCode, _, error) = await RunAsync("install", "com.example.a", "--source", "example");

        Assert.Equal(ExitCodes.Input, exitCode);
        Assert.Contains("com.example.a references com.example.missing", error);
        Assert.Equal(before, Files.ReadAllBytes("lexicons.json"));
        Assert.False(Files.FileExists("lexicons/com/example/a.json"));
    }

    [Theory]
    [Trait("Spec", "TOO-3")]
    [Trait("Spec", "TOO-14")]
    [InlineData(ExitCodes.Input, "com.example.nothing", "--source", "example")]
    [InlineData(ExitCodes.Usage, "not an nsid", "--source", "example")]
    [InlineData(ExitCodes.Usage, "com.example.todo.item", "--source", "nobody")]
    [InlineData(ExitCodes.Usage, "site.example.thing", "--source", "site")]
    [InlineData(ExitCodes.Usage, "org.unknown.thing")]
    [InlineData(ExitCodes.Input, "com.example.todo.item", "--source", "example", "--manifest", "none.json")]
    public async Task Install_BadRequest_FailsWithItsExitCode(int expected, params string[] args)
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));

        var (exitCode, _, error) = await RunAsync(["install", .. args]);

        Assert.Equal(expected, exitCode);
        Assert.NotEmpty(error);
        Assert.False(Files.FileExists("lexicons/com/example/todo/item.json"));
    }

    [Fact]
    [Trait("Spec", "TOO-3")]
    public async Task Install_ArchiveMissing_FailsWithANetworkError()
    {
        var (exitCode, _, error) = await RunAsync("install", "com.example.todo.item", "--source", "example");

        Assert.Equal(ExitCodes.Network, exitCode);
        Assert.Contains("404", error);
    }

    [Fact]
    [Trait("Spec", "TOO-16")]
    public async Task Install_ArchiveNotATarGz_FailsWithANetworkError()
    {
        Http.Responses[Url + First] = Encoding.UTF8.GetBytes("<html>rate limited</html>");

        Assert.Equal(ExitCodes.Network, (await RunAsync("install", "com.example.todo.item", "--source", "example")).ExitCode);
    }

    [Fact]
    [Trait("Spec", "TOO-16")]
    public async Task Install_ArchiveOverTheLimit_FailsBeforeReadingIt()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        Http.Oversized.Add(Url + First);

        var (exitCode, _, error) = await RunAsync("install", "com.example.todo.item", "--source", "example");

        Assert.Equal(ExitCodes.Network, exitCode);
        Assert.Contains("larger than", error);
    }

    [Fact]
    [Trait("Spec", "TOO-18")]
    public async Task Update_NewCommit_RewritesWhatChangedAndRecordsProvenance()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem), ("com/example/todo/authFull.json", TodoAuthFull));
        await RunAsync("install", "com.example.todo.*", "--source", "example");
        var changed = TodoItem.Replace("256", "512", StringComparison.Ordinal);
        Serve(Second, ("com/example/todo/item.json", changed), ("com/example/todo/list.json", Document("com.example.todo.list", "com.example.todo.item")));
        Time.Advance(TimeSpan.FromDays(2));

        var (exitCode, output, error) = await RunAsync("update", "com.example.todo.*", "--commit", Second);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("removed com.example.todo.authFull\nbreaking com.example.todo.item\nadded com.example.todo.list\n", output.ReplaceLineEndings("\n"));
        Assert.Contains("1 added, 1 breaking, 1 removed.", error);
        Assert.Equal(changed, Text("lexicons/com/example/todo/item.json"));
        Assert.False(Files.FileExists("lexicons/com/example/todo/authFull.json"));
        var manifest = Manifest();
        Assert.Null(manifest["lexicons"]!["com.example.todo.authFull"]);
        Assert.Equal(Cid(changed), (string?)manifest["lexicons"]!["com.example.todo.item"]!["cid"]);
        Assert.Equal(Second, (string?)manifest["sources"]!["example"]!["commit"]);
        Assert.Equal("2026-10-26", (string?)manifest["sources"]!["example"]!["fetched"]);
    }

    [Fact]
    [Trait("Spec", "TOO-18")]
    public async Task Update_DryRun_PrintsTheSummaryAndWritesNothing()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        await RunAsync("install", "com.example.todo.*", "--source", "example");
        Serve(Second, ("com/example/todo/item.json", TodoItem.Replace("256", "512", StringComparison.Ordinal)));
        var manifest = Files.ReadAllBytes("lexicons.json");

        var (exitCode, output, error) = await RunAsync("update", "--commit", Second, "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("breaking com.example.todo.item", output);
        Assert.Contains("dry run", error);
        Assert.Equal(manifest, Files.ReadAllBytes("lexicons.json"));
        Assert.Equal(TodoItem, Text("lexicons/com/example/todo/item.json"));
    }

    [Fact]
    [Trait("Spec", "TOO-18")]
    public async Task Update_NothingChanged_LeavesTheManifestUntouched()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        await RunAsync("install", "com.example.todo.*", "--source", "example");
        Serve(Second, ("com/example/todo/item.json", TodoItem));
        var manifest = Files.ReadAllBytes("lexicons.json");
        Time.Advance(TimeSpan.FromDays(2));

        var (exitCode, output, error) = await RunAsync("update", "--commit", Second);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(output);
        Assert.Contains("Nothing changed.", error);
        Assert.Contains("site: network sources are updated from phase 8 on; skipped.", error);
        Assert.Equal(manifest, Files.ReadAllBytes("lexicons.json"));
    }

    [Fact]
    [Trait("Spec", "TOO-18")]
    public async Task Update_FileEditedByHand_IsRestored()
    {
        Serve(First, ("com/example/todo/item.json", TodoItem));
        await RunAsync("install", "com.example.todo.*", "--source", "example");
        Write("lexicons/com/example/todo/item.json", "{}");

        var (exitCode, output, _) = await RunAsync("update", "com.example.todo.item");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("breaking com.example.todo.item", output);
        Assert.Equal(TodoItem, Text("lexicons/com/example/todo/item.json"));
    }

    [Theory]
    [Trait("Spec", "TOO-3")]
    [Trait("Spec", "TOO-18")]
    [InlineData(ExitCodes.Usage, "--commit", "main")]
    [InlineData(ExitCodes.Usage, "site.example.thing")]
    [InlineData(ExitCodes.Usage, "proposals.x.*", "--commit", Second)]
    [InlineData(ExitCodes.Input, "org.unknown.thing")]
    public async Task Update_BadRequest_FailsWithItsExitCode(int expected, params string[] args)
    {
        Write("proposals/proposals/x/y.json", Document("proposals.x.y"));
        var manifest = Manifest();
        manifest["sources"]!["example"]!["namespaces"] = new JsonArray("com/example");
        manifest["sources"]!["proposals"]!["namespaces"] = new JsonArray("proposals/x");
        Files.WriteAllBytes("lexicons.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        Assert.Equal(expected, (await RunAsync(["update", .. args])).ExitCode);
    }

    [Fact]
    [Trait("Spec", "TOO-18")]
    public async Task Update_RepositoryManifestUnchanged_LeavesItByteIdentical()
    {
        var original = File.ReadAllBytes(RepositoryFiles.PathOf("lexicons.json"));
        Files.WriteAllBytes("lexicons.json", original);
        var vendored = Directory.EnumerateFiles(RepositoryFiles.Lexicons, "*.json", SearchOption.AllDirectories)
            .Select(f => (Path: Path.GetRelativePath(RepositoryFiles.Lexicons, f).Replace(Path.DirectorySeparatorChar, '/'), Bytes: File.ReadAllBytes(f))).ToList();
        foreach (var (path, bytes) in vendored) Files.WriteAllBytes("lexicons/" + path, bytes);
        var sources = JsonNode.Parse(original)!["sources"]!;
        foreach (var name in (string[])["atproto", "spaces"])
        {
            var commit = (string)sources[name]!["commit"]!;
            Http.Responses[$"https://codeload.github.com/bluesky-social/atproto/tar.gz/{commit}"] = Archive(commit, vendored.Select(f => ("lexicons/" + f.Path, f.Bytes)));
        }

        var (exitCode, output, error) = await RunAsync("update");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(output);
        Assert.Contains("Nothing changed.", error);
        Assert.Equal(original, Files.ReadAllBytes("lexicons.json"));
        Assert.Equal(original, LexiconManifest.Parse(original).ToUtf8());
    }

    private void Serve(string commit, params (string Path, string Text)[] files) =>
        Http.Responses[Url + commit] = Archive(commit, files.Select(f => Lexicon("lexicons/" + f.Path, f.Text)).Append(Lexicon("README.md", "# Lexicons")));

    private JsonNode Manifest() => JsonNode.Parse(Files.ReadAllBytes("lexicons.json"))!;

    private static string Cid(string text) => LexiconDocument.Parse(Encoding.UTF8.GetBytes(text)).ComputeRecordCid().ToString();
}
