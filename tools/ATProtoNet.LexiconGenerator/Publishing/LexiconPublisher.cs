using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.Publishing;

/// <summary>What publishing did with one schema.</summary>
public enum PublishOutcome
{
    /// <summary>The schema was not published before, and now is.</summary>
    Created,

    /// <summary>The published schema was replaced.</summary>
    Updated,

    /// <summary>The published schema is already this one; nothing was written.</summary>
    Unchanged,

    /// <summary>
    /// The change breaks the Lexicon evolution rules, so nothing was written — for this schema or
    /// any other in the run.
    /// </summary>
    Refused,
}

/// <summary>The outcome of publishing one schema.</summary>
/// <param name="Nsid">The schema.</param>
/// <param name="Outcome">What was done.</param>
/// <param name="Uri">The record written, when one was.</param>
/// <param name="Diff">The changes against the published schema, when there was one.</param>
public sealed record PublishedLexicon(string Nsid, PublishOutcome Outcome, AtUri? Uri, DiffResult? Diff);

/// <summary>Whether an authority's <c>_lexicon</c> DNS record points at the publishing account.</summary>
public enum DnsRecordStatus
{
    /// <summary>The record names the account.</summary>
    Ok,

    /// <summary>There is no record: nobody can resolve the schemas yet.</summary>
    Missing,

    /// <summary>The record names another DID: resolvers look for the schemas there.</summary>
    Mismatch,

    /// <summary>The lookup failed, so the record could not be checked.</summary>
    Unknown,
}

/// <summary>The <c>_lexicon</c> record one authority needs, and what DNS has.</summary>
/// <param name="Name">The DNS name (<c>_lexicon.&lt;authority&gt;</c>).</param>
/// <param name="Value">The TXT value it needs (<c>did=&lt;did&gt;</c>).</param>
/// <param name="Status">What DNS has.</param>
/// <param name="Detail">The DID it names instead, or why the lookup failed.</param>
public sealed record DnsRecordCheck(string Name, string Value, DnsRecordStatus Status, string? Detail);

/// <summary>
/// Publishes Lexicon schemas as <c>com.atproto.lexicon.schema</c> records, keyed by NSID, in the
/// signed-in account's repository.
/// </summary>
/// <remarks>
/// Each schema is compared with the one already published first: an identical one is left alone,
/// and a change that breaks the Lexicon evolution rules (<see cref="LexiconDiffer"/>) stops the
/// whole run before anything is written, unless forced — once a schema is published, others
/// build on it. An update names the record it replaces, so a concurrent change is not
/// overwritten unseen.
/// </remarks>
internal sealed class LexiconPublisher(ILexiconRepository repository)
{
    /// <summary>Publishes <paramref name="sources"/>.</summary>
    /// <param name="sources">The schemas, already linted.</param>
    /// <param name="force">Publish breaking changes too.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<PublishedLexicon>> PublishAsync(
        IReadOnlyList<LexiconSource> sources, bool force, CancellationToken cancellationToken = default)
    {
        var plans = new List<(LexiconSource Source, Nsid Nsid, JsonObject Record, PublishedSchema? Current, DiffResult? Diff)>();
        foreach (var source in sources)
        {
            var nsid = Nsid.Parse(source.Document.Id);
            var record = ToRecord(source.Json);
            var current = await repository.GetAsync(nsid, cancellationToken).ConfigureAwait(false);

            DiffResult? diff = null;
            if (current is not null && ReadDocument(current.Value) is { } published)
                diff = new LexiconDiffer().Compare([published], [source.Document]);

            plans.Add((source, nsid, record, current, diff));
        }

        var breaking = plans.Any(p => p.Diff?.HasBreakingChanges == true);
        var results = new List<PublishedLexicon>();
        foreach (var (_, nsid, record, current, diff) in plans)
        {
            if (current is not null && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(current.Value), record))
            {
                results.Add(new PublishedLexicon(nsid.Value, PublishOutcome.Unchanged, null, diff));
                continue;
            }

            if (breaking && !force)
            {
                results.Add(new PublishedLexicon(nsid.Value, PublishOutcome.Refused, null, diff));
                continue;
            }

            var uri = await repository.PutAsync(nsid, record, current?.Cid, cancellationToken).ConfigureAwait(false);
            results.Add(new PublishedLexicon(
                nsid.Value, current is null ? PublishOutcome.Created : PublishOutcome.Updated, uri, diff));
        }

        return results;
    }

    /// <summary>
    /// Checks the <c>_lexicon</c> record of every authority among <paramref name="nsids"/> against
    /// <paramref name="did"/>.
    /// </summary>
    public static async Task<IReadOnlyList<DnsRecordCheck>> CheckDnsAsync(
        ILexgenNetwork network, Did did, IEnumerable<Nsid> nsids, CancellationToken cancellationToken = default)
    {
        var checks = new List<DnsRecordCheck>();
        var value = $"did={did}";

        // One lookup per authority: every NSID of a group shares its record.
        foreach (var group in nsids.GroupBy(LexiconResolver.GetDnsName, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Did? named;
            try
            {
                named = await network.ResolveAuthorityAsync(group.First(), cancellationToken).ConfigureAwait(false);
            }
            catch (LexiconResolutionException ex)
            {
                checks.Add(new DnsRecordCheck(group.Key, value, DnsRecordStatus.Unknown, ex.Message));
                continue;
            }

            checks.Add(named is null
                ? new DnsRecordCheck(group.Key, value, DnsRecordStatus.Missing, null)
                : named == did
                    ? new DnsRecordCheck(group.Key, value, DnsRecordStatus.Ok, null)
                    : new DnsRecordCheck(group.Key, value, DnsRecordStatus.Mismatch, named.Value));
        }

        return checks;
    }

    /// <summary>
    /// The published schema as a document, or <see langword="null"/> when the record is not one —
    /// in which case there is nothing to break, and it is simply replaced.
    /// </summary>
    private static LexiconDocument? ReadDocument(JsonElement record)
    {
        try
        {
            return record.ValueKind == JsonValueKind.Object
                ? record.Deserialize<LexiconDocument>(LexiconJson.ReadOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The schema record for a Lexicon document: the document as written, with <c>$type</c> first.
    /// </summary>
    internal static JsonObject ToRecord(JsonElement document)
    {
        var record = new JsonObject { ["$type"] = LexiconSchemaRecord.Collection.Value };
        foreach (var property in document.EnumerateObject())
        {
            if (property.Name != "$type")
                record[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        return record;
    }
}
