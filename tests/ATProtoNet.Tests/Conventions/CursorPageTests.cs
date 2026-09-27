using System.Reflection;
using ATProtoNet.Models;

namespace ATProtoNet.Tests.Conventions;

/// <summary>
/// Guards the pagination convention: every response model with a Lexicon <c>cursor</c> implements
/// <see cref="ICursorPage{T}"/>, so <see cref="ATProtoNet.Http.Pagination.EnumerateAsync{TPage, T}"/>
/// can walk it.
/// </summary>
public class CursorPageTests
{
    [Fact]
    public void CursoredResponses_AllImplementICursorPage()
    {
        var missing = typeof(AtProtoClient).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("ATProtoNet.Lexicon.", StringComparison.Ordinal) == true)
            .Where(type => type.GetProperty("Cursor", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string))
            .Where(type => !type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICursorPage<>)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(missing);
    }
}
