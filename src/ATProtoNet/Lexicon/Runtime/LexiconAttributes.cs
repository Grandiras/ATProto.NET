namespace ATProtoNet.Lexicon.Runtime;

/// <summary>Marks a generated type with the Lexicon definition it represents, for generators that resolve references to it (LEX-8).</summary>
/// <param name="reference">The definition: <c>nsid</c> for <c>main</c>, <c>nsid#name</c> otherwise.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class LexiconDefinitionAttribute(string reference) : Attribute
{
    /// <summary>The definition.</summary>
    public string Reference { get; } = reference;
}

/// <summary>Points at the generated <c>{Name}Lexicons</c> class of an assembly's Lexicon generation unit (LEX-30).</summary>
/// <param name="lexiconsClass">The class.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LexiconAssemblyAttribute(Type lexiconsClass) : Attribute
{
    /// <summary>The class.</summary>
    public Type LexiconsClass { get; } = lexiconsClass;
}

/// <summary>Maps a Lexicon string <c>format</c> to the type generated code uses for it, such as <c>space-ref</c> to <see cref="SpaceUri"/>.</summary>
/// <param name="format">The format.</param>
/// <param name="type">The type; it reads and writes a JSON string.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LexiconFormatTypeAttribute(string format, Type type) : Attribute
{
    /// <summary>The format.</summary>
    public string Format { get; } = format;

    /// <summary>The type.</summary>
    public Type Type { get; } = type;
}
