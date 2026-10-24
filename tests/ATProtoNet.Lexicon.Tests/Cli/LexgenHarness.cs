using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using ATProtoNet.Lexicon.Cli;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Lexicon.Tests.Cli;

/// <summary>Runs <c>atproto-lexgen</c> in process against in-memory files, a fake network and a fixed clock (TOO-6).</summary>
public abstract class LexgenHarness
{
    protected const string TodoItem = """
        {
          "lexicon": 1,
          "id": "com.example.todo.item",
          "defs": {
            "main": {
              "type": "record",
              "description": "A to-do item.",
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

    protected const string TodoAuthFull = """
        {
          "lexicon": 1,
          "id": "com.example.todo.authFull",
          "defs": {
            "main": {
              "type": "permission-set",
              "description": "Full access to to-do items.",
              "title": "Manage your to-do items",
              "permissions": [
                { "type": "permission", "resource": "repo", "collection": ["com.example.todo.item"] },
                { "type": "permission", "resource": "rpc", "inheritAud": true, "lxm": ["com.example.todo.getItems"] }
              ]
            }
          }
        }
        """;

    protected InMemoryFiles Files { get; } = new();

    protected FakeHttp Http { get; } = new();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    protected async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var client = new HttpClient(Http, disposeHandler: false);
        var environment = new LexgenEnvironment { Out = output, Error = error, Files = Files, GetEnvironmentVariable = _ => null, Http = client, Time = Time };
        var exitCode = await new LexgenApp(environment).RunAsync(args, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    protected string Text(string path) => Encoding.UTF8.GetString(Files.ReadAllBytes(path));

    protected void Write(string path, string text) => Files.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

    /// <summary>A Lexicon with the given id that references <paramref name="refs"/>.</summary>
    protected static string Document(string nsid, params string[] refs) =>
        $$"""
        {
          "lexicon": 1,
          "id": "{{nsid}}",
          "defs": {
            "main": {
              "type": "object",
              "properties": { {{string.Join(", ", refs.Select((r, i) => $"\"p{i}\": {{ \"type\": \"ref\", \"ref\": \"{r}\" }}"))}} }
            }
          }
        }
        """;

    /// <summary>A codeload-style tar.gz: a global header, then every file under <c>{name}-{commit}/</c>.</summary>
    protected static byte[] Archive(string commit, IEnumerable<(string Path, byte[] Bytes)> files)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["comment"] = commit }));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"repo-{commit}/"));
            foreach (var (path, bytes) in files)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"repo-{commit}/{path}") { DataStream = new MemoryStream(bytes) });
        }
        return buffer.ToArray();
    }

    protected static (string Path, byte[] Bytes) Lexicon(string path, string text) => (path, Encoding.UTF8.GetBytes(text));

    /// <summary>Files keyed by full path; directories exist while they hold files.</summary>
    protected sealed class InMemoryFiles : ILexgenFileSystem
    {
        private readonly SortedDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public IEnumerable<string> Paths => _files.Keys.Select(p => Path.GetRelativePath(Directory.GetCurrentDirectory(), p));

        public bool FileExists(string path) => _files.ContainsKey(Path.GetFullPath(path));

        public bool DirectoryExists(string path) => _files.Keys.Any(f => f.StartsWith(Prefix(path), StringComparison.Ordinal));

        public byte[] ReadAllBytes(string path) => _files.TryGetValue(Path.GetFullPath(path), out var bytes) ? bytes : throw new FileNotFoundException("Could not find file.", path);

        public void WriteAllBytes(string path, byte[] bytes) => _files[Path.GetFullPath(path)] = bytes;

        public void Delete(string path) => _files.Remove(Path.GetFullPath(path));

        public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        {
            var extension = pattern.TrimStart('*');
            return [.. _files.Keys.Where(f => f.StartsWith(Prefix(directory), StringComparison.Ordinal) && f.EndsWith(extension, StringComparison.Ordinal))
                .Select(f => Path.Combine(directory, f[Prefix(directory).Length..]))];
        }

        private static string Prefix(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
    }

    /// <summary>Serves fixed responses by URL and records every request.</summary>
    protected sealed class FakeHttp : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        /// <summary>URLs whose responses announce a length over the git archive limit.</summary>
        public HashSet<string> Oversized { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (!Responses.TryGetValue(url, out var bytes)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new ByteArrayContent(bytes);
            if (Oversized.Contains(url)) content.Headers.ContentLength = LexgenApp.MaxGitArchiveBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
