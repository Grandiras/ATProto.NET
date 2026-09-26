using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.LexiconGenerator;
using ATProtoNet.LexiconGenerator.Publishing;
using ATProtoNet.Serialization;
using LexGen = ATProtoNet.LexiconGenerator.Program;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// The <c>atproto-lexgen</c> command line, run in process against a scratch directory and a
/// network stand-in: argument handling, and each command's happy and error paths.
/// </summary>
public sealed class LexiconGeneratorProgramTests : IDisposable
{
    private static readonly Did Publisher = Did.Parse("did:plc:publisherpublisherpublis");

    private const string TodoItem = """
        {
          "lexicon": 1,
          "id": "com.example.todo.item",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "required": ["title"],
                "properties": {
                  "title": { "type": "string", "maxLength": 256 },
                  "done": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    private const string TodoAuthFull = """
        {
          "lexicon": 1,
          "id": "com.example.todo.authFull",
          "defs": {
            "main": {
              "type": "permission-set",
              "title": "Manage your to-do items",
              "permissions": [
                { "type": "permission", "resource": "repo", "collection": ["com.example.todo.item"] },
                { "type": "permission", "resource": "rpc", "inheritAud": true, "lxm": ["com.example.todo.getItems"] }
              ]
            }
          }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lexgen-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);
    private readonly List<string> _prompts = [];
    private readonly FakeNetwork _network = new();
    private string? _secret;

    public LexiconGeneratorProgramTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ──────────────────────────────────────────────────────────
    //  General
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData]
    [InlineData("--help")]
    [InlineData("help")]
    public async Task Main_NoCommandOrHelp_PrintsTheCommands(params string[] args)
    {
        var (exitCode, output, _) = await RunAsync(args);

        Assert.Equal(0, exitCode);
        foreach (var command in (string[])["csharp", "lexicon", "lint", "diff", "publish", "resolve"])
            Assert.Contains($"    {command} ", output);
        Assert.DoesNotContain("migrate", output);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task Main_VersionFlag_PrintsPackageVersion(string flag)
    {
        var assemblyVersion = typeof(LexGen).Assembly.GetName().Version!;
        var packageVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        var (exitCode, output, _) = await RunAsync(flag);

        Assert.Equal(0, exitCode);
        Assert.StartsWith($"atproto-lexgen {packageVersion}", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_UnknownCommand_Fails()
    {
        var (exitCode, _, error) = await RunAsync("generate");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command: 'generate'", error);
    }

    [Fact]
    public async Task Main_Migrate_ExplainsWhyItWasRemoved()
    {
        var (exitCode, _, error) = await RunAsync("migrate", "--baseline", "a", "--current", "b");

        Assert.Equal(1, exitCode);
        Assert.Contains("'migrate' was removed", error);
        Assert.Contains("new NSID", error);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("lexicon")]
    [InlineData("lint")]
    [InlineData("diff")]
    [InlineData("publish")]
    [InlineData("resolve")]
    public async Task Main_CommandHelp_PrintsTheCommandsUsage(string command)
    {
        var (exitCode, output, _) = await RunAsync(command, "--help");

        Assert.Equal(0, exitCode);
        Assert.StartsWith($"atproto-lexgen {command} — ", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("lexicon")]
    [InlineData("lint")]
    [InlineData("diff")]
    [InlineData("publish")]
    [InlineData("resolve")]
    public async Task Main_UnknownOption_Fails(string command)
    {
        var (exitCode, _, error) = await RunAsync(command, "--bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", error);
    }

    // ──────────────────────────────────────────────────────────
    //  csharp
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Csharp_Schemas_WritesOneFilePerDocument()
    {
        var input = Lexicons(TodoItem, TodoAuthFull);
        var output = Path.Combine(_root, "out");

        var (exitCode, stdout, _) = await RunAsync("csharp", "--input", input, "--output", output, "--namespace", "My.Lexicons");

        Assert.Equal(0, exitCode);
        Assert.Contains("  OK    com.example.todo.item", stdout);
        Assert.Contains("Generated 2 C# file(s)", stdout);
        Assert.Contains("public sealed class ItemRecord", File.ReadAllText(Path.Combine(output, "Com", "Example", "Todo", "Item.g.cs")));
        Assert.Contains("namespace My.Lexicons.Com.Example.Todo;", File.ReadAllText(Path.Combine(output, "Com", "Example", "Todo", "AuthFull.g.cs")));
    }

    [Fact]
    public async Task Csharp_MalformedAndForeignFiles_AreReportedAndSkipped()
    {
        var input = Lexicons(TodoItem);
        File.WriteAllText(Path.Combine(input, "broken.json"), "{ \"lexicon\": ");
        File.WriteAllText(Path.Combine(input, "package.json"), "{ \"name\": \"not a lexicon\" }");
        File.WriteAllText(Path.Combine(input, "v2.json"), "{ \"lexicon\": 2, \"id\": \"com.example.v2\", \"defs\": {} }");

        var (exitCode, _, error) = await RunAsync("csharp", "-i", input, "-o", Path.Combine(_root, "out"));

        Assert.Equal(0, exitCode);
        Assert.Contains("  ERROR ", error);
        Assert.Contains("broken.json", error);
        Assert.Contains("package.json (not a Lexicon document)", error);
        Assert.Contains("(unsupported lexicon version: 2)", error);
        Assert.Contains("1 file(s) had parse errors.", error);
    }

    [Theory]
    [InlineData("--output", "out", "--input is required")]
    [InlineData("--input", "in", "--output is required")]
    public async Task Csharp_RequiredOptionMissing_Fails(string option, string value, string message)
    {
        var (exitCode, _, error) = await RunAsync("csharp", option, Path.Combine(_root, value));

        Assert.Equal(1, exitCode);
        Assert.Contains(message, error);
    }

    [Fact]
    public async Task Csharp_NoLexiconsInInput_Fails()
    {
        var input = Lexicons();

        var (exitCode, _, error) = await RunAsync("csharp", "-i", input, "-o", Path.Combine(_root, "out"));

        Assert.Equal(1, exitCode);
        Assert.Contains("No valid Lexicon documents found", error);
        Assert.False(Directory.Exists(Path.Combine(_root, "out")));
    }

    [Fact]
    public async Task Csharp_InputDirectoryMissing_Fails()
    {
        var (exitCode, _, error) = await RunAsync("csharp", "-i", Path.Combine(_root, "nope"), "-o", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("Input directory not found", error);
    }

    // ──────────────────────────────────────────────────────────
    //  lexicon
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Lexicon_Assembly_WritesItsSchemasFromAnIsolatedLoad()
    {
        // This test assembly carries the space fixtures of LexiconEmitterTests.
        var output = Path.Combine(_root, "lexicons");

        var (exitCode, stdout, _) = await RunAsync("lexicon", "--assembly", typeof(LexiconGeneratorProgramTests).Assembly.Location, "--output", output);

        Assert.Equal(0, exitCode);
        var forum = Path.Combine(output, "com", "atmoboards", "test", "forum.json");
        Assert.Contains("\"type\": \"space\"", File.ReadAllText(forum));
        Assert.Contains($"  WRITE {Path.Combine("com", "atmoboards", "test", "forum.json")}", stdout);
    }

    [Fact]
    public async Task Lexicon_NotAnAssembly_Fails()
    {
        var fake = Path.Combine(_root, "Fake.dll");
        File.WriteAllText(fake, "not a PE file");

        var (exitCode, _, error) = await RunAsync("lexicon", "-a", fake, "-o", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to load assembly", error);
    }

    [Fact]
    public async Task Lexicon_AssemblyMissing_Fails()
    {
        var (exitCode, _, error) = await RunAsync("lexicon", "-a", Path.Combine(_root, "Missing.dll"), "-o", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("Assembly not found", error);
    }

    // ──────────────────────────────────────────────────────────
    //  lint
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Lint_CleanSchemas_Succeeds()
    {
        var (exitCode, output, _) = await RunAsync("lint", "--input", Lexicons(TodoItem, TodoAuthFull));

        Assert.Equal(0, exitCode);
        Assert.Contains("Linted 2 schema(s): 0 error(s), 0 warning(s).", output);
    }

    [Fact]
    public async Task Lint_PermissionSetErrors_FailsAndNamesThem()
    {
        var input = Lexicons(TodoAuthFull.Replace("\"com.example.todo.item\"", "\"*\"", StringComparison.Ordinal));

        var (exitCode, output, _) = await RunAsync("lint", "-i", input);

        Assert.Equal(1, exitCode);
        Assert.Contains("  ERROR com.example.todo.authFull#main: permissions[0]: 'collection' may not use wildcards", output);
    }

    [Fact]
    public async Task Lint_WarningsOnly_SucceedUnlessStrict()
    {
        var input = Lexicons(TodoAuthFull.Replace("\"title\": \"Manage your to-do items\",", "", StringComparison.Ordinal));

        Assert.Equal(0, (await RunAsync("lint", "-i", input)).ExitCode);
        var (exitCode, output, _) = await RunAsync("lint", "-i", input, "--strict");

        Assert.Equal(1, exitCode);
        Assert.Contains("  WARN  com.example.todo.authFull#main: no 'title'", output);
    }

    [Fact]
    public async Task Lint_UnreadableFile_Fails()
    {
        var input = Lexicons(TodoItem);
        File.WriteAllText(Path.Combine(input, "broken.json"), "[");

        Assert.Equal(1, (await RunAsync("lint", "-i", input)).ExitCode);
    }

    // ──────────────────────────────────────────────────────────
    //  diff
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Diff_OptionalPropertyAdded_SucceedsAndSuggestsARevision()
    {
        var baseline = Lexicons(TodoItem);
        var current = Lexicons(WithProperty(TodoItem, "note", required: false));

        var (exitCode, output, _) = await RunAsync("diff", "--baseline", baseline, "--current", current, "--strict");

        Assert.Equal(0, exitCode);
        Assert.Contains("Property 'note' added (optional)", output);
        Assert.Contains("com.example.todo.item: revision → 2", output);
    }

    [Fact]
    public async Task Diff_BreakingChangeUnderStrict_Fails()
    {
        var baseline = Lexicons(TodoItem);
        var current = Lexicons(WithProperty(TodoItem, "owner", required: true));

        var (exitCode, output, _) = await RunAsync("diff", "-b", baseline, "-c", current, "--strict");

        Assert.Equal(1, exitCode);
        Assert.Contains("BREAK", output);
    }

    [Fact]
    public async Task Diff_AgainstAnAssembly_ComparesTheSchemasItDerives()
    {
        var (exitCode, output, _) = await RunAsync(
            "diff", "-b", Lexicons(TodoItem), "-a", typeof(LexiconGeneratorProgramTests).Assembly.Location);

        Assert.Equal(0, exitCode);
        Assert.Contains("com.atmoboards.test.forum", output);
        Assert.Contains("Schema removed", output);
    }

    [Fact]
    public async Task Diff_NoCurrentSchemas_Fails()
    {
        var (exitCode, _, error) = await RunAsync("diff", "-b", Lexicons(TodoItem));

        Assert.Equal(1, exitCode);
        Assert.Contains("Either --current (directory) or --assembly (DLL) is required", error);
    }

    // ──────────────────────────────────────────────────────────
    //  publish
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_NewSchemas_WritesThemAndChecksDns()
    {
        SignInFromEnvironment();
        _network.Authorities["_lexicon.todo.example.com"] = Publisher;

        var (exitCode, output, _) = await RunAsync("publish", "--input", Lexicons(TodoItem, TodoAuthFull));

        Assert.Equal(0, exitCode);
        var signIn = Assert.Single(_network.SignIns);
        Assert.Equal(("alice.example.com", "app-pass-word", (Uri?)null), signIn);
        Assert.True(_network.Repository.Disposed);

        Assert.Equal(["com.example.todo.authFull", "com.example.todo.item"], _network.Repository.Puts.Select(p => p.Nsid));
        var record = _network.Repository.Puts[1].Record;
        Assert.Equal("$type", record.First().Key);
        Assert.Equal("com.atproto.lexicon.schema", record["$type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(TodoItem), WithoutType(record)));
        Assert.Null(_network.Repository.Puts[1].Replacing);

        Assert.Contains($"  CREATE com.example.todo.item  at://{Publisher}/com.atproto.lexicon.schema/com.example.todo.item", output);
        Assert.Contains($"  OK       _lexicon.todo.example.com  TXT \"did={Publisher}\"", output);
    }

    [Fact]
    public async Task Publish_SchemaAlreadyPublished_IsLeftAlone()
    {
        SignInFromEnvironment();
        // The same document, its members in another order.
        _network.Repository.Publish("com.example.todo.item", """
            { "defs": { "main": { "record": { "properties": { "done": { "type": "boolean" }, "title": { "maxLength": 256, "type": "string" } }, "required": ["title"], "type": "object" }, "key": "tid", "type": "record" } },
              "id": "com.example.todo.item", "lexicon": 1, "$type": "com.atproto.lexicon.schema" }
            """);

        var (exitCode, output, _) = await RunAsync("publish", "--input", Lexicons(TodoItem));

        Assert.Equal(0, exitCode);
        Assert.Empty(_network.Repository.Puts);
        Assert.Contains("  SAME   com.example.todo.item", output);
    }

    [Fact]
    public async Task Publish_CompatibleChange_ReplacesTheRecordItRead()
    {
        SignInFromEnvironment();
        var current = _network.Repository.Publish("com.example.todo.item", WithType(TodoItem));

        var (exitCode, output, _) = await RunAsync("publish", "-i", Lexicons(WithProperty(TodoItem, "note", required: false)));

        Assert.Equal(0, exitCode);
        Assert.Equal(current, Assert.Single(_network.Repository.Puts).Replacing);
        Assert.Contains("  UPDATE com.example.todo.item", output);
    }

    [Fact]
    public async Task Publish_PublishedRecordIsNoLexicon_IsReplaced()
    {
        SignInFromEnvironment();
        var current = _network.Repository.Publish(
            "com.example.todo.item", """{ "$type": "com.atproto.lexicon.schema", "lexicon": 1, "defs": "not an object" }""");

        var (exitCode, output, _) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(0, exitCode);
        Assert.Equal(current, Assert.Single(_network.Repository.Puts).Replacing);
        Assert.Contains("  UPDATE com.example.todo.item", output);
    }

    [Fact]
    public async Task Publish_BreakingChange_WritesNothingAtAll()
    {
        SignInFromEnvironment();
        _network.Repository.Publish("com.example.todo.item", WithType(TodoItem));

        var (exitCode, output, error) = await RunAsync(
            "publish", "-i", Lexicons(WithProperty(TodoItem, "owner", required: true), TodoAuthFull));

        Assert.Equal(1, exitCode);
        Assert.Empty(_network.Repository.Puts);
        Assert.Contains("  REFUSE com.example.todo.authFull", output);
        Assert.Contains("Property 'owner' added (required — BREAKING)", error);
        Assert.Contains("--force", error);
    }

    [Fact]
    public async Task Publish_BreakingChangeForced_IsWritten()
    {
        SignInFromEnvironment();
        _network.Repository.Publish("com.example.todo.item", WithType(TodoItem));

        var (exitCode, _, error) = await RunAsync(
            "publish", "-i", Lexicons(WithProperty(TodoItem, "owner", required: true)), "--force");

        Assert.Equal(0, exitCode);
        Assert.Single(_network.Repository.Puts);
        Assert.Contains("published a breaking change (--force)", error);
    }

    [Fact]
    public async Task Publish_DnsRecordMissingOrElsewhere_PrintsTheRecordToCreate()
    {
        SignInFromEnvironment();
        var other = Lexicons(TodoItem.Replace("com.example.todo.item", "org.other.stuff.thing", StringComparison.Ordinal));
        File.Copy(Path.Combine(Lexicons(TodoAuthFull), "0.json"), Path.Combine(other, "1.json"));
        _network.Authorities["_lexicon.stuff.other.org"] = Did.Parse("did:plc:someoneelsesomeoneelsesom");

        var (exitCode, output, _) = await RunAsync("publish", "-i", other);

        Assert.Equal(0, exitCode);
        Assert.Contains($"  MISSING  _lexicon.todo.example.com  TXT \"did={Publisher}\"  ← create this record", output);
        Assert.Contains($"  MISMATCH _lexicon.stuff.other.org  names did:plc:someoneelsesomeoneelsesom; set it to TXT \"did={Publisher}\"", output);
    }

    [Fact]
    public async Task Publish_DnsLookupFails_SaysItCouldNotCheck()
    {
        SignInFromEnvironment();
        _network.FailingDns.Add("_lexicon.todo.example.com");

        var (exitCode, output, _) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(0, exitCode);
        Assert.Contains("  UNKNOWN  _lexicon.todo.example.com", output);
    }

    [Fact]
    public async Task Publish_LintErrors_RefusesBeforeSigningIn()
    {
        SignInFromEnvironment();
        var input = Lexicons(TodoAuthFull.Replace("\"inheritAud\": true", "\"aud\": \"did:web:api.example.com#svc\"", StringComparison.Ordinal));

        var (exitCode, _, error) = await RunAsync("publish", "-i", input);

        Assert.Equal(1, exitCode);
        Assert.Contains("'aud' must be '*' in a permission set", error);
        Assert.Contains("Publish refused", error);
        Assert.Empty(_network.SignIns);
    }

    [Fact]
    public async Task Publish_PasswordAsAnArgument_IsRefusedWithoutEchoingIt()
    {
        var (exitCode, output, error) = await RunAsync("publish", "-i", Lexicons(TodoItem), "--password", "hunter2hunter2");

        Assert.Equal(1, exitCode);
        Assert.Contains("ATPROTO_PASSWORD", error);
        Assert.DoesNotContain("hunter2hunter2", output + error);
        Assert.Empty(_network.SignIns);
    }

    [Fact]
    public async Task Publish_NoPasswordInTheEnvironment_PromptsForOne()
    {
        _environment["ATPROTO_IDENTIFIER"] = "alice.example.com";
        _secret = "prompted-secret";

        var (exitCode, output, error) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(0, exitCode);
        Assert.Equal("App password for alice.example.com: ", Assert.Single(_prompts));
        Assert.Equal("prompted-secret", Assert.Single(_network.SignIns).Password);
        Assert.DoesNotContain("prompted-secret", output + error);
    }

    [Fact]
    public async Task Publish_NoPasswordAtAll_Fails()
    {
        _environment["ATPROTO_IDENTIFIER"] = "alice.example.com";

        var (exitCode, _, error) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(1, exitCode);
        Assert.Contains("No password: set ATPROTO_PASSWORD", error);
        Assert.Empty(_network.SignIns);
    }

    [Fact]
    public async Task Publish_NoIdentifier_Fails()
    {
        _environment["ATPROTO_PASSWORD"] = "app-pass-word";

        var (exitCode, _, error) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(1, exitCode);
        Assert.Contains("--identifier is required (or set ATPROTO_IDENTIFIER)", error);
    }

    [Fact]
    public async Task Publish_IdentifierAndPdsOptions_OverrideTheEnvironment()
    {
        SignInFromEnvironment();
        _environment["ATPROTO_PDS_URL"] = "https://env.example.com";

        await RunAsync("publish", "-i", Lexicons(TodoItem), "--identifier", "did:plc:publisherpublisherpublis", "--pds", "https://pds.example.com");

        var signIn = Assert.Single(_network.SignIns);
        Assert.Equal("did:plc:publisherpublisherpublis", signIn.Identifier);
        Assert.Equal(new Uri("https://pds.example.com"), signIn.Pds);
    }

    [Fact]
    public async Task Publish_PdsFromTheEnvironment_IsUsed()
    {
        SignInFromEnvironment();
        _environment["ATPROTO_PDS_URL"] = "http://localhost:2583";

        await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(new Uri("http://localhost:2583"), Assert.Single(_network.SignIns).Pds);
    }

    [Fact]
    public async Task Publish_InvalidPds_Fails()
    {
        SignInFromEnvironment();

        var (exitCode, _, error) = await RunAsync("publish", "-i", Lexicons(TodoItem), "--pds", "ftp://pds.example.com");

        Assert.Equal(1, exitCode);
        Assert.Contains("--pds must be an absolute https URL", error);
    }

    [Theory]
    [InlineData("http://pds.example.com")]
    [InlineData("http://192.168.1.10:2583")]
    public async Task Publish_PlainHttpPdsOffThisMachine_IsRefusedBeforeThePasswordIsSent(string pds)
    {
        SignInFromEnvironment();

        var (exitCode, _, error) = await RunAsync("publish", "-i", Lexicons(TodoItem), "--pds", pds);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pds must be an absolute https URL", error);
        Assert.Empty(_network.SignIns);
    }

    [Theory]
    [InlineData("https://pds.example.com", true)]
    [InlineData("http://localhost:2583", true)]
    [InlineData("http://127.0.0.1:2583", true)]
    [InlineData("http://[::1]:2583", true)]
    [InlineData("http://pds.example.com", false)]
    [InlineData("http://10.0.0.5", false)]
    [InlineData("ftp://pds.example.com", false)]
    public void MaySignInAt_OnlyHttpsOrLoopbackHttp(string url, bool expected) =>
        Assert.Equal(expected, SdkLexgenNetwork.MaySignInAt(new Uri(url)));

    [Fact]
    public async Task Publish_SignInRefused_ReportsIt()
    {
        SignInFromEnvironment();
        _network.SignInError = new XrpcException("AuthenticationRequired", "Invalid identifier or password");

        var (exitCode, _, error) = await RunAsync("publish", "-i", Lexicons(TodoItem));

        Assert.Equal(1, exitCode);
        Assert.Contains("Publish failed: ", error);
        Assert.Contains("Invalid identifier or password", error);
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("--baseline")]
    [InlineData("--assembly")]
    [InlineData("--no-bump")]
    public async Task Publish_OptionOfTheOldCommand_ExplainsTheNewOne(string option)
    {
        var (exitCode, _, error) = await RunAsync("publish", option, _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("publish now writes the schemas to your repository on a PDS", error);
    }

    // ──────────────────────────────────────────────────────────
    //  resolve
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_PublishedSchema_PrintsTheLexiconFile()
    {
        _network.Publish(WithType(TodoItem));

        var (exitCode, output, error) = await RunAsync("resolve", "com.example.todo.item");

        Assert.Equal(0, exitCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(TodoItem), JsonNode.Parse(output)));
        Assert.Contains("Resolved com.example.todo.item from at://", error);
        Assert.Null(Assert.Single(_network.Resolutions).Authority);
    }

    [Fact]
    public async Task Resolve_OutputDirectory_WritesEachSchema()
    {
        _network.Publish(WithType(TodoItem));
        _network.Publish(WithType(TodoAuthFull));
        var output = Path.Combine(_root, "resolved");

        var (exitCode, _, _) = await RunAsync("resolve", "com.example.todo.item", "com.example.todo.authFull", "--output", output);

        Assert.Equal(0, exitCode);
        var written = File.ReadAllText(Path.Combine(output, "com", "example", "todo", "authFull.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(TodoAuthFull), JsonNode.Parse(written)));
        Assert.True(File.Exists(Path.Combine(output, "com", "example", "todo", "item.json")));
    }

    [Fact]
    public async Task Resolve_WithDid_ReadsFromThatRepository()
    {
        _network.Publish(WithType(TodoItem));

        await RunAsync("resolve", "com.example.todo.item", "--did", Publisher.Value);

        Assert.Equal(Publisher, Assert.Single(_network.Resolutions).Authority);
    }

    [Fact]
    public async Task Resolve_Unresolvable_ReportsItAndFails()
    {
        _network.Publish(WithType(TodoItem));

        var (exitCode, output, error) = await RunAsync("resolve", "com.example.todo.missing", "com.example.todo.item");

        Assert.Equal(1, exitCode);
        Assert.Contains("  FAIL  com.example.todo.missing: nothing published", error);
        Assert.Contains("\"id\": \"com.example.todo.item\"", output);
    }

    [Theory]
    [InlineData(new string[0], "Specify the NSID of at least one schema")]
    [InlineData(new[] { "not an nsid" }, "'not an nsid' is not an NSID")]
    [InlineData(new[] { "com.example.todo.item", "--did", "alice.example.com" }, "--did must be a DID")]
    public async Task Resolve_BadArguments_Fail(string[] args, string message)
    {
        var (exitCode, _, error) = await RunAsync(["resolve", .. args]);

        Assert.Equal(1, exitCode);
        Assert.Contains(message, error);
        Assert.Empty(_network.Resolutions);
    }

    [Fact]
    public void ToDocumentJson_SchemaRecord_DropsTheRecordType()
    {
        var schema = JsonSerializer.Deserialize<LexiconSchemaRecord>(WithType(TodoItem), AtProtoJsonDefaults.Options)!;

        var json = LexGen.ToDocumentJson(schema);

        Assert.DoesNotContain("$type", json);
        Assert.DoesNotContain("null", json);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(TodoItem), JsonNode.Parse(json)));
    }

    [Fact]
    public void ToDocumentJson_LocalizedText_IsWrittenAsIs()
    {
        var schema = JsonSerializer.Deserialize<LexiconSchemaRecord>(
            WithType(TodoAuthFull.Replace("\"title\": \"Manage your to-do items\",", "\"title:lang\": { \"ja\": \"基本的なアプリ機能\" },", StringComparison.Ordinal)),
            AtProtoJsonDefaults.Options)!;

        Assert.Contains("\"ja\": \"基本的なアプリ機能\"", LexGen.ToDocumentJson(schema));
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CommandContext
        {
            Out = output,
            Error = error,
            GetEnvironmentVariable = name => _environment.GetValueOrDefault(name),
            ReadSecret = prompt =>
            {
                _prompts.Add(prompt);
                return _secret;
            },
            CreateNetwork = () => _network,
        };

        var exitCode = await LexGen.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private void SignInFromEnvironment()
    {
        _environment["ATPROTO_IDENTIFIER"] = "alice.example.com";
        _environment["ATPROTO_PASSWORD"] = "app-pass-word";
    }

    /// <summary>A fresh directory holding the given documents as <c>0.json</c>, <c>1.json</c>, ….</summary>
    private string Lexicons(params string[] documents)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        for (var i = 0; i < documents.Length; i++)
            File.WriteAllText(Path.Combine(directory, $"{i}.json"), documents[i]);
        return directory;
    }

    private static string WithProperty(string document, string name, bool required)
    {
        var node = JsonNode.Parse(document)!;
        var record = node["defs"]!["main"]!["record"]!;
        record["properties"]![name] = new JsonObject { ["type"] = "string" };
        if (required)
            record["required"]!.AsArray().Add(name);
        return node.ToJsonString();
    }

    private static string WithType(string document)
    {
        var node = JsonNode.Parse(document)!.AsObject();
        node.Insert(0, "$type", "com.atproto.lexicon.schema");
        return node.ToJsonString();
    }

    private static JsonObject WithoutType(JsonObject record)
    {
        var copy = record.DeepClone().AsObject();
        copy.Remove("$type");
        return copy;
    }

    /// <summary>The network: published schemas to resolve, DNS records, and one account to publish to.</summary>
    private sealed class FakeNetwork : ILexgenNetwork
    {
        private readonly Dictionary<string, ResolvedLexicon> _published = new(StringComparer.Ordinal);

        public Dictionary<string, Did> Authorities { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FailingDns { get; } = new(StringComparer.Ordinal);

        public FakeRepository Repository { get; } = new();

        public List<(string Identifier, string Password, Uri? Pds)> SignIns { get; } = [];

        public List<(Nsid Nsid, Did? Authority)> Resolutions { get; } = [];

        public Exception? SignInError { get; set; }

        public void Publish(string record)
        {
            var schema = JsonSerializer.Deserialize<LexiconSchemaRecord>(record, AtProtoJsonDefaults.Options)!;
            _published[schema.Id!.Value] = new ResolvedLexicon
            {
                Uri = AtUri.Parse($"at://{Publisher}/com.atproto.lexicon.schema/{schema.Id}"),
                Cid = Cid.Parse("bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy"),
                Schema = schema,
            };
        }

        public Task<ResolvedLexicon> ResolveAsync(Nsid nsid, Did? authority, CancellationToken cancellationToken)
        {
            Resolutions.Add((nsid, authority));
            return _published.TryGetValue(nsid.Value, out var resolved)
                ? Task.FromResult(resolved)
                : Task.FromException<ResolvedLexicon>(
                    new LexiconResolutionException("nothing published", nsid, LexiconResolutionErrorKind.NotFound));
        }

        public Task<Did?> ResolveAuthorityAsync(Nsid nsid, CancellationToken cancellationToken)
        {
            var name = LexiconResolver.GetDnsName(nsid);
            if (FailingDns.Contains(name))
            {
                return Task.FromException<Did?>(new LexiconResolutionException(
                    "the DNS-over-HTTPS endpoint answered with an error", nsid, LexiconResolutionErrorKind.ResolutionFailed));
            }

            return Task.FromResult(Authorities.GetValueOrDefault(name));
        }

        public Task<ILexiconRepository> SignInAsync(string identifier, string password, Uri? pds, CancellationToken cancellationToken)
        {
            SignIns.Add((identifier, password, pds));
            return SignInError is null
                ? Task.FromResult<ILexiconRepository>(Repository)
                : Task.FromException<ILexiconRepository>(SignInError);
        }
    }

    private sealed class FakeRepository : ILexiconRepository
    {
        private readonly Dictionary<string, PublishedSchema> _records = new(StringComparer.Ordinal);
        private int _cids;

        public Did Did => Publisher;

        public List<(string Nsid, JsonObject Record, Cid? Replacing)> Puts { get; } = [];

        public bool Disposed { get; private set; }

        public Cid Publish(string nsid, string record)
        {
            var cid = NextCid();
            _records[nsid] = new PublishedSchema(JsonDocument.Parse(record).RootElement.Clone(), cid);
            return cid;
        }

        public Task<PublishedSchema?> GetAsync(Nsid nsid, CancellationToken cancellationToken) =>
            Task.FromResult(_records.GetValueOrDefault(nsid.Value));

        public Task<AtUri> PutAsync(Nsid nsid, JsonObject record, Cid? replacing, CancellationToken cancellationToken)
        {
            Puts.Add((nsid.Value, record, replacing));
            return Task.FromResult(AtUri.Parse($"at://{Did}/com.atproto.lexicon.schema/{nsid}"));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private Cid NextCid() => ++_cids switch
        {
            1 => Cid.Parse("bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy"),
            _ => Cid.Parse("bafyreifaauqak7wnmabf6ympoqokw7tfpaa5ir6rmvhmibytcmwfkyg7lu"),
        };
    }
}
