namespace ATProtoNet.Lexicon;

/// <summary>
/// The Lexicon compatibility rules (LEX-14–LEX-17): once a schema is published it may only gain optional parts and constraints
/// on previously unconstrained fields. Loosening a constraint or making a property optional breaks consumers as much as
/// tightening or requiring one does, so both are breaking.
/// </summary>
/// <remarks>See <see href="https://atproto.com/guides/lexicon#versioning"/>.</remarks>
public static partial class LexiconCompatibility
{
    /// <summary>Classifies every change from <paramref name="baseline"/> (the published schemas) to <paramref name="current"/>.</summary>
    /// <param name="baseline">The published schemas.</param>
    /// <param name="current">The proposed schemas.</param>
    /// <returns>The changes, and the revisions to publish for schemas with compatible changes only.</returns>
    public static LexiconChangeReport Compare(LexiconCatalog baseline, LexiconCatalog current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var changes = new List<LexiconChange>();
        var revisions = new Dictionary<Nsid, int>();
        foreach (var before in baseline.Documents)
        {
            if (!current.TryGetDocument(before.Id, out var after))
            {
                changes.Add(new(before.Id, "", LexiconChangeKind.Removed, LexiconChangeClassification.Breaking, "The schema was removed."));
                continue;
            }
            var start = changes.Count;
            new Differ(before.Id, changes).Document(before, after);
            var published = before.Revision ?? 1;
            // LEX-17: no suggestion once the current document already carries a higher revision.
            if (changes.Count > start && !changes.Skip(start).Any(c => c.IsBreaking) && !(after.Revision > published)) revisions[before.Id] = published + 1;
        }
        foreach (var after in current.Documents)
            if (!baseline.TryGetDocument(after.Id, out _))
                changes.Add(new(after.Id, "", LexiconChangeKind.Added, LexiconChangeClassification.Compatible, "The schema was added."));
        return new([.. changes.OrderBy(c => c.Nsid.Value, StringComparer.Ordinal)], revisions);
    }
}
