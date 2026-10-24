namespace ATProtoNet.Lexicon.Cli;

/// <summary>Everything a command touches outside itself, so tests run commands in-process with fakes (TOO-6).</summary>
public sealed record LexgenEnvironment
{
    /// <summary>Results.</summary>
    public required TextWriter Out { get; init; }

    /// <summary>Diagnostics and progress.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>The files.</summary>
    public required ILexgenFileSystem Files { get; init; }

    /// <summary>Reads an environment variable.</summary>
    public required Func<string, string?> GetEnvironmentVariable { get; init; }

    /// <summary>The network client.</summary>
    public required HttpClient Http { get; init; }

    /// <summary>The clock, for provenance dates.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The process's console, disk, environment and network.</summary>
    /// <returns>The environment.</returns>
    public static LexgenEnvironment System() => new()
    {
        Out = Console.Out,
        Error = Console.Error,
        Files = new PhysicalFileSystem(),
        GetEnvironmentVariable = Environment.GetEnvironmentVariable,
        Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
    };
}

/// <summary>The file operations of the commands. Paths are relative to the current directory or absolute.</summary>
public interface ILexgenFileSystem
{
    /// <summary>Whether a file exists.</summary>
    bool FileExists(string path);

    /// <summary>Whether a directory exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>A file's bytes.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>Writes a file, creating its directory.</summary>
    void WriteAllBytes(string path, byte[] bytes);

    /// <summary>Deletes a file.</summary>
    void Delete(string path);

    /// <summary>The files below a directory matching a pattern such as <c>*.json</c>, recursively, with paths that start with <paramref name="directory"/>.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
}

/// <summary>The disk.</summary>
public sealed class PhysicalFileSystem : ILexgenFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] bytes)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes);
    }

    /// <inheritdoc />
    public void Delete(string path) => File.Delete(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories);
}
