using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ATProtoNet.Lexicon.CodeGen;

internal enum DeclKind
{
    Object,     // a sealed partial record: object defs, inline objects
    Record,     // a record def
    MethodPart, // Parameters, Input, Output or Message of a method
    Union,      // a C# 15 union
    KnownValues,// a readonly record struct over string
    Method,     // a static class with the descriptor
    Tokens,     // a static class of const strings
    PermissionSet,
    Space,
}

// One generated type, named before anything is written so references and collisions are known up front.
internal sealed class TypeDecl(DeclKind kind, string name, string ns, TypeDecl? parent, LexiconDocument document, PlanUnit unit, string path)
{
    public DeclKind Kind { get; } = kind;
    public string Name { get; set; } = name;
    public string Namespace { get; } = ns;
    public TypeDecl? Parent { get; } = parent;
    public LexiconDocument Document { get; } = document;
    public PlanUnit Unit { get; } = unit;

    // "nsid#def" for a named def, "nsid#def.prop.prop" for an inline type: the key of renames and the inventory.
    public string Path { get; } = path;

    // The $type of a named object or record ("nsid" for main), else null.
    public string? LexiconType { get; init; }

    public LexSchema? Schema { get; init; }
    public List<TypeDecl> Nested { get; } = [];

    // Union cases and the context NSID their refs resolve in.
    public LexUnion? Union { get; init; }

    // A method's parts.
    public TypeDecl? Parameters { get; set; }
    public TypeDecl? Input { get; set; }
    public TypeDecl? Output { get; set; }
    public TypeDecl? Message { get; set; }

    // Token defs of a Tokens class.
    public List<KeyValuePair<string, LexToken>> Tokens { get; } = [];

    public string? Experimental { get; set; }

    public string FullName => (Parent?.FullName ?? Namespace) + "." + Name;

    public string Ref => "global::" + FullName;

    public bool IsObject => Kind is DeclKind.Object or DeclKind.Record or DeclKind.MethodPart;

    public override string ToString() => FullName;
}

internal sealed class PlanUnit(string name, IReadOnlyList<string> include)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Include { get; } = include;
    public List<LexiconDocument> Documents { get; } = [];
    public List<TypeDecl> Decls { get; } = [];
}

internal sealed class ReferenceComparer : IEqualityComparer<object>
{
    public static ReferenceComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
