using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATProtoNet.Lexicon.Cli;

public sealed partial class LexgenApp
{
    /// <summary>The largest git archive <c>install</c> and <c>update</c> download (tooling.md, Limits).</summary>
    public const long MaxGitArchiveBytes = 256L * 1024 * 1024;

    // A source's Lexicon files by NSID, copied byte for byte (TOO-16), at the commit it was read at.
    private sealed record SourceTree(string Name, JsonObject Source, string? Commit, Dictionary<string, byte[]> Files);

    private async Task<SourceTree> FetchAsync(LexiconManifest manifest, string manifestPath, string name, string? commit, CancellationToken cancellationToken)
    {
        if (manifest.Sources[name] is not JsonObject source) throw new LexgenException(ExitCodes.Usage, $"{manifestPath} has no source named \"{name}\".");
        return (string?)source["type"] switch
        {
            "git" => await FetchGitAsync(name, source, commit ?? (string?)source["commit"], cancellationToken),
            "directory" => ReadDirectory(name, source, manifestPath),
            "network" => throw new LexgenException(ExitCodes.Usage, $"{name}: network sources are installed and updated from phase 8 on; this version handles git and directory sources."),
            var type => throw new LexgenException(ExitCodes.Input, $"{manifestPath}: source \"{name}\" has an unknown type \"{type}\"."),
        };
    }

    // TOO-16: the archive of the repository at the commit, read from codeload without unpacking it to disk.
    private async Task<SourceTree> FetchGitAsync(string name, JsonObject source, string? commit, CancellationToken cancellationToken)
    {
        var repository = (string?)source["repository"];
        if (repository is null || !RepositoryName().IsMatch(repository)) throw new LexgenException(ExitCodes.Input, $"{name}: \"repository\" is not a GitHub owner/name.");
        if (commit is null || !CommitSha().IsMatch(commit)) throw new LexgenException(ExitCodes.Input, $"{name}: \"commit\" is not a commit SHA.");
        var prefix = ((string?)source["path"] ?? "").Trim('/') is { Length: > 0 } path ? path + "/" : "";

        using var response = await Env.Http.GetAsync($"https://codeload.github.com/{repository}/tar.gz/{commit}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxGitArchiveBytes) throw TooLarge(name);
        var archive = new MemoryStream();
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[81920];
            for (int read; (read = await body.ReadAsync(buffer, cancellationToken)) > 0;)
            {
                if (archive.Length + read > MaxGitArchiveBytes) throw TooLarge(name);
                archive.Write(buffer, 0, read);
            }
        }
        archive.Position = 0;

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var reader = new TarReader(new GZipStream(archive, CompressionMode.Decompress));
            while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                // Entries sit under one top-level directory, {repository name}-{commit}/.
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;
                var relative = entry.Name[(entry.Name.IndexOf('/') + 1)..];
                if (!relative.StartsWith(prefix, StringComparison.Ordinal) || !relative.EndsWith(".json", StringComparison.Ordinal)) continue;
                if (NsidOf(relative[prefix.Length..]) is not { } nsid) continue;
                if (entry.Length > LexiconLimits.MaxLexiconDocumentBytes) throw new LexgenException(ExitCodes.Input, $"{name}: {relative} is larger than {LexiconLimits.MaxLexiconDocumentBytes} bytes.");
                var bytes = new byte[entry.Length];
                await entry.DataStream.ReadExactlyAsync(bytes, cancellationToken);
                files[nsid] = bytes;
            }
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or EndOfStreamException)
        {
            throw new LexgenException(ExitCodes.Network, $"{name}: the archive of {repository} at {commit} is not a readable tar.gz ({e.Message}).");
        }
        return new(name, source, commit, files);
    }

    private SourceTree ReadDirectory(string name, JsonObject source, string manifestPath)
    {
        if ((string?)source["path"] is not { } path) throw new LexgenException(ExitCodes.Input, $"{name}: a directory source needs a \"path\".");
        var directory = Path.Combine(Path.GetDirectoryName(manifestPath) is { Length: > 0 } root ? root : ".", path);
        if (!Env.Files.DirectoryExists(directory)) throw new LexgenException(ExitCodes.Input, $"{directory}: the directory of source \"{name}\" does not exist.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Env.Files.EnumerateFiles(directory, "*.json"))
            if (NsidOf(Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/')) is { } nsid)
                files[nsid] = Env.Files.ReadAllBytes(file);
        return new(name, source, null, files);
    }

    // a/b/c.json → a.b.c, for paths in the vendored layout (LEX-22); anything else is not a Lexicon file.
    private static string? NsidOf(string relativePath) =>
        Nsid.TryParse(relativePath[..^".json".Length].Replace('/', '.'), out var nsid) ? nsid.ToString() : null;

    private static LexgenException TooLarge(string name) => new(ExitCodes.Network, $"{name}: the archive is larger than {MaxGitArchiveBytes} bytes.");

    // The namespaces a source owns, as NSID prefixes: git and directory "namespaces" (a/b), network "prefix" (a.b.).
    private static IEnumerable<string> NamespacesOf(JsonNode? source) =>
        ((source?["namespaces"] as JsonArray)?.Select(n => ((string?)n ?? "").Replace('/', '.')) ?? [])
        .Concat((string?)source?["prefix"] is { } prefix ? [prefix.TrimEnd('.')] : []);

    // The most specific source wins: com.atproto.space.* belongs to the source naming com/atproto/space, not com/atproto.
    private static string? OwnerOf(LexiconManifest manifest, string nsid) =>
        manifest.Sources.SelectMany(s => NamespacesOf(s.Value).Where(ns => Covers(ns, nsid)).Select(ns => (s.Key, ns.Length)))
            .OrderByDescending(m => m.Length).ThenBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key).FirstOrDefault();

    private static bool Covers(string prefix, string nsid) => nsid == prefix || nsid.StartsWith(prefix + ".", StringComparison.Ordinal);

    // An NSID, or a namespace pattern such as app.bsky.*.
    private static bool Matches(string pattern, string nsid) => pattern.EndsWith(".*", StringComparison.Ordinal) ? Covers(pattern[..^2], nsid) && nsid != pattern[..^2] : nsid == pattern;

    private static void CheckPatterns(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
            if (!Pattern().IsMatch(pattern) || !pattern.EndsWith(".*", StringComparison.Ordinal) && !Nsid.TryParse(pattern, out _))
                throw new LexgenException(ExitCodes.Usage, $"\"{pattern}\" is neither an NSID nor a pattern such as app.bsky.*.");
    }

    [GeneratedRegex("^[A-Za-z0-9-]+(\\.[A-Za-z0-9-]+)*(\\.\\*)?$")]
    private static partial Regex Pattern();

    [GeneratedRegex("^[A-Za-z0-9-_.]+/[A-Za-z0-9-_.]+$")]
    private static partial Regex RepositoryName();

    [GeneratedRegex("^[0-9a-f]{7,40}$")]
    private static partial Regex CommitSha();
}
