using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>A generated Lexicon object: it keeps the properties this version of its schema does not know (LEX-42).</summary>
public interface ILexiconObject
{
    /// <summary>Properties the schema does not declare, written back unchanged; <see langword="null"/> when there are none.</summary>
    IDictionary<string, JsonElement>? ExtensionData { get; }
}

/// <summary>A generated type of a named Lexicon definition: its <c>$type</c> and its serialization contract.</summary>
/// <typeparam name="TSelf">The type itself.</typeparam>
public interface ILexiconType<TSelf> : ILexiconObject where TSelf : ILexiconType<TSelf>
{
    /// <summary>The definition's <c>$type</c> in union position: the NSID for <c>main</c>, <c>nsid#name</c> otherwise.</summary>
    static abstract string LexiconType { get; }

    /// <summary>The source-generated contract.</summary>
    static abstract JsonTypeInfo<TSelf> JsonTypeInfo { get; }
}

/// <summary>A record type: a Lexicon <c>record</c> definition.</summary>
public interface IAtProtoRecord : ILexiconObject
{
    /// <summary>The collection: the record definition's NSID.</summary>
    static abstract Nsid Collection { get; }

    /// <summary>The record key type.</summary>
    static abstract RecordKeyType KeyType { get; }
}

/// <summary>A record type with its own contract; generated record types implement it (LEX-56).</summary>
/// <typeparam name="TSelf">The record type itself.</typeparam>
public interface IAtProtoRecord<TSelf> : IAtProtoRecord, ILexiconType<TSelf> where TSelf : IAtProtoRecord<TSelf>;

/// <summary>A record with an optional <c>createdAt</c> that the records API stamps on create when it is missing (LEX-58).</summary>
/// <typeparam name="TSelf">The record type itself.</typeparam>
public interface ICreatedAtRecord<TSelf> where TSelf : ICreatedAtRecord<TSelf>
{
    /// <summary>When the record was created, if set.</summary>
    AtDatetime? CreatedAt { get; }

    /// <summary>A copy of <paramref name="record"/> with <see cref="CreatedAt"/> set.</summary>
    /// <param name="record">The record.</param>
    /// <param name="createdAt">The time.</param>
    /// <returns>The copy.</returns>
    static abstract TSelf WithCreatedAt(TSelf record, AtDatetime createdAt);
}

/// <summary>A string with known values (open) or an enum (closed), as a struct over the string (LEX-47).</summary>
/// <typeparam name="TSelf">The struct itself.</typeparam>
/// <remarks>Any string converts to and from it: membership of a closed set is checked by validation, not by reading.</remarks>
public interface IKnownValue<TSelf> where TSelf : struct, IKnownValue<TSelf>
{
    /// <summary>The string; <see langword="null"/> only for <see langword="default"/>, which cannot be written.</summary>
    string Value { get; }

    /// <summary>Whether <see cref="Value"/> is one of the values the schema lists.</summary>
    bool IsKnown { get; }

    /// <summary>The value for a string.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The value.</returns>
    static abstract TSelf Create(string value);
}

/// <summary>A page of a paginated query: the items and the cursor of the next page (LEX-70).</summary>
/// <typeparam name="T">The item type.</typeparam>
public interface ICursorPage<T>
{
    /// <summary>The cursor of the next page; <see langword="null"/> on the last page.</summary>
    string? Cursor { get; }

    /// <summary>The page's items.</summary>
    IReadOnlyList<T> Items { get; }
}

/// <summary>The parameters of a paginated query (LEX-70).</summary>
/// <typeparam name="TSelf">The parameters type itself.</typeparam>
public interface ICursorParameters<TSelf> where TSelf : ICursorParameters<TSelf>
{
    /// <summary>The cursor of the page to fetch; <see langword="null"/> for the first.</summary>
    string? Cursor { get; init; }
}

/// <summary>The query parameters of an XRPC method, written and read by generated code without reflection (LEX-63).</summary>
/// <typeparam name="TSelf">The parameters type itself.</typeparam>
public interface IXrpcParameters<TSelf> where TSelf : IXrpcParameters<TSelf>
{
    /// <summary>Writes the parameters in declaration order.</summary>
    /// <param name="writer">The writer.</param>
    void WriteTo(XrpcParameterWriter writer);

    /// <summary>Reads the parameters, as a server binds a request.</summary>
    /// <param name="reader">The request's parameters.</param>
    /// <returns>The parameters.</returns>
    /// <exception cref="FormatException">A parameter is missing, repeated or invalid; the message names it.</exception>
    static abstract TSelf ReadFrom(XrpcParameterReader reader);
}
