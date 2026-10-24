using System.Text;
using ATProtoNet.Lexicon;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Builds Lexicon documents from JSON written with single quotes, so test schemas stay legible.</summary>
internal static class Lex
{
    /// <summary>Parses a document; <c>'</c> stands for <c>"</c>.</summary>
    public static LexiconDocument Doc(string json) => LexiconDocument.Parse(Bytes(json));

    /// <summary>A catalog of the documents.</summary>
    public static LexiconCatalog Catalog(params string[] documents) => LexiconCatalog.Create(documents.Select(Doc));

    /// <summary>The UTF-8 bytes of the JSON; <c>'</c> stands for <c>"</c>.</summary>
    public static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json.Replace('\'', '"'));

    /// <summary>A document with the given <c>defs</c> members (JSON, without the surrounding braces).</summary>
    public static string Defs(string id, string defs, string extra = "") => $$$"""{'lexicon':1,'id':'{{{id}}}'{{{extra}}},'defs':{ {{{defs}}} }}""";

    /// <summary>A record document whose object has the given properties (JSON members) and comma-separated required and nullable names.</summary>
    public static string Record(string id, string properties, string required = "", string extra = "", string nullable = "") =>
        Defs(id, $$$"""'main':{'type':'record','key':'tid','record':{'type':'object','required':[{{{Names(required)}}}],'nullable':[{{{Names(nullable)}}}],'properties':{ {{{properties}}} } } }""", extra);

    /// <summary>Comma-separated names as a JSON array's members.</summary>
    public static string Names(string names) => string.Join(",", names.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => $"'{n}'"));
}
