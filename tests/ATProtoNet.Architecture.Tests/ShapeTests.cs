using System.Xml.Linq;
using Mono.Cecil;

namespace ATProtoNet.Architecture.Tests;

// The measurable "less chaotic" rules of design/00-vision.md, for every runtime package.
public class ShapeTests
{
    private const int MaxLines = 400;

    private static readonly string[] MutableCollections =
    [
        "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.List`1", "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1", "System.Collections.Generic.Stack`1", "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.LinkedList`1", "System.Runtime.CompilerServices.ConditionalWeakTable`2",
        "Microsoft.Extensions.Caching.Memory.MemoryCache",
    ];

    public static TheoryData<string> RuntimeProjects() => [.. Repository.RuntimeProjects.Select(d => d.Name)];

    [Theory]
    [MemberData(nameof(RuntimeProjects))]
    public void InternalsVisibleTo_OnlyItsOwnTestProject(string project)
    {
        var targets = Repository.Module(project).Assembly.CustomAttributes
            .Where(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            .Select(a => (string)a.ConstructorArguments[0].Value);
        Assert.All(targets, target => Assert.Equal($"{project}.Tests", target.Split(',')[0]));
    }

    [Theory]
    [MemberData(nameof(RuntimeProjects))]
    public void StaticFields_NoPublicMutableStateOrCaches(string project)
    {
        var offenders = Repository.Module(project).GetTypes()
            .SelectMany(t => t.Fields.Where(f => f.IsStatic && !f.IsLiteral && !f.Name.Contains('<')).Select(f => (Type: t, Field: f)))
            .Where(x => IsPublicMutable(x.Type, x.Field) || IsCache(x.Field))
            .Select(x => $"{x.Type.FullName}.{x.Field.Name}");
        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(RuntimeProjects))]
    public void StaticProperties_NoPublicSetter(string project)
    {
        var offenders = Repository.Module(project).GetTypes().Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.Properties.Where(p => p.SetMethod is { IsStatic: true, IsPublic: true }).Select(p => $"{t.FullName}.{p.Name}"));
        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(RuntimeProjects))]
    public void SourceFiles_AtMost400Lines(string project)
    {
        var directory = Repository.RuntimeProjects.Single(d => d.Name == project);
        var tooLong = Repository.SourceFiles(directory)
            .Select(f => (f.FullName, Lines: File.ReadLines(f.FullName).Count()))
            .Where(f => f.Lines > MaxLines)
            .Select(f => $"{Path.GetRelativePath(Repository.Root.FullName, f.FullName)}: {f.Lines} lines");
        Assert.Empty(tooLong);
    }

    [Theory]
    [MemberData(nameof(RuntimeProjects))]
    public void Project_HasPublicApiBaseline(string project)
    {
        var directory = Repository.RuntimeProjects.Single(d => d.Name == project);
        Assert.True(File.Exists(Path.Combine(directory.FullName, "PublicAPI.Shipped.txt")));
        Assert.True(File.Exists(Path.Combine(directory.FullName, "PublicAPI.Unshipped.txt")));
    }

    [Theory]
    [InlineData("Directory.Build.props", "TreatWarningsAsErrors", "true")]
    [InlineData("Directory.Build.props", "Nullable", "enable")]
    [InlineData("src/Directory.Build.props", "IsAotCompatible", "true")]
    [InlineData("src/Directory.Build.props", "IsTrimmable", "true")]
    [InlineData("src/Directory.Build.props", "GenerateDocumentationFile", "true")]
    public void BuildProps_SetsRuntimePackageRule(string file, string property, string value)
    {
        var props = XDocument.Load(Path.Combine(Repository.Root.FullName, file));
        Assert.Contains(props.Descendants(property), e => e.Value == value && e.Attribute("Condition") is null);
    }

    private static bool IsPublicMutable(TypeDefinition type, FieldDefinition field) =>
        (type.IsPublic || type.IsNestedPublic) && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly) && !field.IsInitOnly;

    private static bool IsCache(FieldDefinition field) =>
        MutableCollections.Contains(field.FieldType.GetElementType().FullName)
        || field.FieldType.Namespace == "System.Collections.Concurrent";
}
