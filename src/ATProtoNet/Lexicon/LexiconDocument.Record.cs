using System.Text.Json;
using ATProtoNet.Data;

namespace ATProtoNet.Lexicon;

// Not linked into CodeGen: it needs the data model.
public sealed partial class LexiconDocument
{
    /// <summary>The record collection Lexicon schemas are published in.</summary>
    public const string SchemaCollection = "com.atproto.lexicon.schema";

    /// <summary>
    /// The CID of the document's DRISL encoding in record form (<c>{"$type": "com.atproto.lexicon.schema", …}</c>): the CID of its
    /// published schema record when published unchanged (LEX-23).
    /// </summary>
    /// <returns>The CID.</returns>
    /// <exception cref="FormatException">The document is not a data-model value (it has a float, for example).</exception>
    public Cid ComputeRecordCid()
    {
        DataModelValue value;
        try
        {
            value = DataModelValue.FromJson(SourceUtf8.Span);
        }
        catch (JsonException e)
        {
            throw new FormatException(e.Message, e);
        }
        var map = value as DataModelMap ?? throw new FormatException("A Lexicon document is an object.");
        var record = new DataModelMap([new("$type", new DataModelString(SchemaCollection)), .. map.Entries.Where(e => e.Key != "$type")]);
        return Cid.Compute(CidCodec.DagCbor, record.ToDrisl());
    }
}
