using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ATProtoNet.Tests.Conventions;

/// <summary>
/// Guards the wire-name convention: every public property of a Lexicon model carries an explicit
/// camelCase <see cref="JsonPropertyNameAttribute"/>, so no name depends on a serializer's
/// naming policy.
/// </summary>
/// <remarks>
/// Whether each name is the one upstream uses is the drift test's job
/// (<c>Lexicon/Upstream/LexiconDriftTests</c>); together they replace the per-model tests that
/// serialized a model only to look for its property names.
/// </remarks>
public partial class LexiconJsonNameTests
{
    [GeneratedRegex(@"^\$?[a-z][A-Za-z0-9]*$")]
    private static partial Regex CamelCase();

    /// <summary>
    /// Properties that break the convention, keyed <c>Namespace.Type.Property</c>, and why. Each
    /// must still be a violation.
    /// </summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["ATProtoNet.Lexicon.Com.AtProto.Space.SpaceRecordView.Path"] =
            "A computed helper the serializer writes as \"path\"; the space models are reworked by #119 part 4.",
        ["ATProtoNet.Lexicon.Com.AtProto.Lexicon.LexiconPermissionSet.LocalizedTitles"] =
            "The Lexicon language's own field name, \"title:lang\".",
        ["ATProtoNet.Lexicon.Com.AtProto.Lexicon.LexiconPermissionSet.LocalizedDetails"] =
            "The Lexicon language's own field name, \"detail:lang\".",
    };

    [Fact]
    public void LexiconModels_PublicProperties_HaveExplicitCamelCaseJsonNames()
    {
        var models = typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns
                && (ns.StartsWith("ATProtoNet.Lexicon.", StringComparison.Ordinal) || ns == "ATProtoNet.Models")
                && !t.IsDefined(typeof(CompilerGeneratedAttribute))
                && t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.IsDefined(typeof(JsonPropertyNameAttribute))))
            .ToList();
        Assert.True(models.Count > 300, $"Only {models.Count} models found; is the scan still matching?");

        var violations = new List<string>();
        foreach (var type in models)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // A conditional [JsonIgnore] (WhenWritingNull, …) still puts the property on the wire.
                if (property.IsDefined(typeof(JsonExtensionDataAttribute))
                    || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                {
                    continue;
                }

                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                if (name is null)
                    violations.Add($"{type.FullName}.{property.Name}: no [JsonPropertyName]");
                else if (!CamelCase().IsMatch(name))
                    violations.Add($"{type.FullName}.{property.Name}: \"{name}\" is not camelCase");
            }
        }

        var unexpected = violations.Where(v => !Exceptions.ContainsKey(v[..v.IndexOf(':')])).ToList();
        var stale = Exceptions.Keys.Where(k => !violations.Any(v => v.StartsWith(k + ":", StringComparison.Ordinal))).ToList();

        Assert.True(unexpected.Count == 0, "Lexicon model properties without an explicit camelCase JSON name:\n  " + string.Join("\n  ", unexpected));
        Assert.True(stale.Count == 0, "Remove these stale exceptions:\n  " + string.Join("\n  ", stale));
    }
}
