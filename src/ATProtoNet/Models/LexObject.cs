using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Models;

/// <summary>
/// Base class for the SDK's models of Lexicon records and objects. Keeps every field the model
/// does not declare, so that data survives a deserialize → serialize round trip.
/// </summary>
/// <remarks>
/// <para>Lexicons evolve by adding optional fields, and a record can carry fields from a newer
/// revision of its schema than the one a model was written against. The Lexicon spec asks clients
/// not to clobber that data when they write a record back. Every property a model does not declare
/// lands in <see cref="ExtensionData"/> on read and is written back unchanged, after the declared
/// properties. Key order is not significant: a record's CID is computed over DAG-CBOR, which sorts
/// map keys.</para>
/// <para>The dictionary stays <see langword="null"/> until an undeclared field is read, so a
/// model that matches the wire exactly costs nothing extra.</para>
/// <para>Records and union variants consume their own <c>$type</c>, so it never appears here for
/// them. On any other object an optional <c>$type</c> is kept like every other undeclared field.</para>
/// </remarks>
public abstract class LexObject
{
    /// <summary>
    /// Fields read from the wire that this model does not declare, keyed by their JSON name, or
    /// <see langword="null"/> when there were none.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}
