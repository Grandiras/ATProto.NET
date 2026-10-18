using Mono.Cecil;

namespace ATProtoNet.Architecture.Tests;

/// <summary>The source tree and the built runtime assemblies under test.</summary>
public static class Repository
{
    /// <summary>The repository root: the directory holding <c>ATProto.NET.slnx</c>.</summary>
    public static DirectoryInfo Root { get; } = FindRoot();

    /// <summary>The runtime packages: every project under <c>src/</c>.</summary>
    public static IReadOnlyList<DirectoryInfo> RuntimeProjects { get; } =
        [.. Root.GetDirectories("src").Single().GetDirectories().Where(d => d.GetFiles("*.csproj").Length == 1)];

    /// <summary>A runtime assembly from this test's output, by project name.</summary>
    public static ModuleDefinition Module(string name) =>
        ModuleDefinition.ReadModule(Path.Combine(AppContext.BaseDirectory, name + ".dll"));

    /// <summary>The hand-written C# files under a directory, skipping build output and generated code.</summary>
    public static IEnumerable<FileInfo> SourceFiles(DirectoryInfo directory) =>
        directory.EnumerateFiles("*.cs", SearchOption.AllDirectories).Where(f =>
            !f.FullName.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Generated"));

    private static DirectoryInfo FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ATProto.NET.slnx"))) return dir;
        throw new DirectoryNotFoundException($"No ATProto.NET.slnx above {AppContext.BaseDirectory}.");
    }
}
