namespace ATProtoNet.Lexicon;

/// <summary>What kind of change a <see cref="LexiconChange"/> is.</summary>
public enum LexiconChangeKind
{
    /// <summary>Something was added: a schema, a definition, a property, a constraint.</summary>
    Added,

    /// <summary>Something was removed.</summary>
    Removed,

    /// <summary>Something that exists on both sides has a different value.</summary>
    Changed,
}

/// <summary>Whether a change is allowed by the Lexicon versioning rules.</summary>
public enum LexiconChangeClassification
{
    /// <summary>Data and clients written against the baseline keep working.</summary>
    Compatible,

    /// <summary>Published data or existing clients may stop working; the change needs a new NSID.</summary>
    Breaking,
}

/// <summary>One difference between two versions of a Lexicon schema (LEX-14).</summary>
/// <param name="Nsid">The schema the change is in.</param>
/// <param name="Pointer">The JSON pointer of the changed node: in the baseline for a removal, in the current document otherwise.</param>
/// <param name="Kind">Whether it was added, removed or changed.</param>
/// <param name="Classification">Whether it is compatible or breaking.</param>
/// <param name="Message">What changed, in one sentence.</param>
public sealed record LexiconChange(Nsid Nsid, string Pointer, LexiconChangeKind Kind, LexiconChangeClassification Classification, string Message)
{
    /// <summary>Whether the change is <see cref="LexiconChangeClassification.Breaking"/>.</summary>
    public bool IsBreaking => Classification == LexiconChangeClassification.Breaking;

    /// <inheritdoc />
    public override string ToString() => $"{Nsid}{(Pointer.Length == 0 ? "" : " " + Pointer)}: {Classification.ToString().ToLowerInvariant()}: {Message}";
}

/// <summary>The changes between two catalogs, from <see cref="LexiconCompatibility.Compare"/>.</summary>
/// <param name="Changes">Every change, sorted by NSID.</param>
/// <param name="SuggestedRevisions">The <c>revision</c> to publish for each schema whose changes are all compatible (LEX-17).</param>
public sealed record LexiconChangeReport(IReadOnlyList<LexiconChange> Changes, IReadOnlyDictionary<Nsid, int> SuggestedRevisions)
{
    /// <summary>Whether any change is breaking.</summary>
    public bool HasBreakingChanges => Changes.Any(c => c.IsBreaking);
}
