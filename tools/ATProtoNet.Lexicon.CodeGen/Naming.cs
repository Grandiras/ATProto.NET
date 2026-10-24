using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ATProtoNet.Lexicon.CodeGen;

// The naming rules of generated code (LEX-32–LEX-37, LEX-89).
internal static class Naming
{
    // Protocol words with established casing (LEX-32).
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["atproto"] = "AtProto",
        ["simplespace"] = "SimpleSpace",
        ["ageassurance"] = "AgeAssurance",
        ["richtext"] = "RichText",
        ["safelink"] = "SafeLink",
    };

    private static readonly string[] Verbs = ["Get", "List", "Search", "Query", "Create", "Put", "Update", "Delete", "Describe"];
    private static readonly string[] MethodParts = ["Parameters", "Input", "Output", "Message"];
    private static readonly string[] PlumbingWords = ["Main", "Defs", "Def"];
    private static readonly string[] GeneratedSuffixes = ["Object", "Value"];

    // A JSON key or NSID segment as a C# identifier: split on non-alphanumerics, PascalCase each part (LEX-35).
    public static string Pascal(string text)
    {
        if (Words.TryGetValue(text, out var word)) return word;
        var builder = new StringBuilder(text.Length);
        foreach (var part in Regex.Split(text, "[^A-Za-z0-9]+"))
            if (part.Length > 0) builder.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        if (builder.Length == 0) builder.Append("Value");
        if (char.IsDigit(builder[0])) builder.Insert(0, '_');
        return builder.ToString();
    }

    // The words of a PascalCase name: "GetPostThread" → Get, Post, Thread.
    public static List<string> SplitWords(string name) =>
        [.. Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+|_").Cast<Match>().Select(m => m.Value)];

    // The owner part of a union name: generated type names, without method parts and a method's leading verb (LEX-34).
    public static string OwnerPath(IEnumerable<string> typeNames, bool firstIsMethod)
    {
        var parts = typeNames.Where(n => Array.IndexOf(MethodParts, n) < 0).ToList();
        if (firstIsMethod && parts.Count > 0)
        {
            var words = SplitWords(parts[0]);
            if (words.Count > 1 && Array.IndexOf(Verbs, words[0]) >= 0) parts[0] = string.Concat(words.Skip(1));
        }
        return parts.Aggregate("", Join);
    }

    // Concatenates two names, writing a word sequence shared across the join once ("PostThread" + "Thread" → "PostThread").
    public static string Join(string left, string right)
    {
        if (left.Length == 0) return right;
        var a = SplitWords(left);
        var b = SplitWords(right);
        for (var k = Math.Min(a.Count, b.Count); k > 0; k--)
            if (a.Skip(a.Count - k).SequenceEqual(b.Take(k), StringComparer.Ordinal))
                return left + string.Concat(b.Skip(k));
        return left + right;
    }

    // LEX-89: the reason a type name does not read like a hand-written .NET name, or null.
    public static string? Violation(string name)
    {
        if (name.Length > 40) return "is longer than 40 characters";
        if (name[0] == '_' || char.IsDigit(name[0])) return "starts with a digit or underscore";
        var words = SplitWords(name);
        if (words.FirstOrDefault(w => Array.IndexOf(PlumbingWords, w) >= 0) is { } plumbing) return $"contains the protocol word \"{plumbing}\"";
        for (var i = 1; i < words.Count; i++)
        {
            if (words[i] == words[i - 1]) return $"repeats \"{words[i]}\"";
            if (Array.IndexOf(GeneratedSuffixes, words[i]) >= 0 && Array.IndexOf(GeneratedSuffixes, words[i - 1]) >= 0 && i == words.Count - 1)
                return "stacks generated suffixes";
        }
        return null;
    }

    // An NSID glob: "*" one segment, "**" any rest; matched against "nsid", "nsid#def" or "nsid#def.property".
    public static bool Matches(string glob, string text)
    {
        var pattern = "^" + string.Concat(Regex.Split(glob, @"(\*\*|\*)").Select(part => part switch
        {
            "**" => ".*",
            "*" => "[^.#]*",
            _ => Regex.Escape(part),
        })) + (glob.EndsWith(".**", StringComparison.Ordinal) ? "" : "$");
        return Regex.IsMatch(text, pattern);
    }

    public static bool MatchesAny(IEnumerable<string> globs, string text) => globs.Any(g => Matches(g, text));

    // A C# string literal.
    public static string Literal(string text)
    {
        var builder = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        return builder.Append('"').ToString();
    }

    // XML documentation text.
    public static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "").Replace("\n", " ").Trim();
}
