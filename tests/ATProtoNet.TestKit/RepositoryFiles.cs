namespace ATProtoNet.TestKit;

/// <summary>Files of the repository checkout the tests run from, such as the vendored Lexicons.</summary>
public static class RepositoryFiles
{
    /// <summary>The repository root: the nearest ancestor of the test assembly holding <c>ATProto.NET.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The vendored Lexicons directory, <c>lexicons/</c>.</summary>
    public static string Lexicons => Path.Combine(Root, "lexicons");

    /// <summary>The full path of a repository file.</summary>
    public static string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ATProto.NET.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The tests do not run inside the repository.");
    }
}
