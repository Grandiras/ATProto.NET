namespace ATProtoNet.Lexicon.Validation;

/// <summary>One way data breaks its Lexicon.</summary>
/// <param name="Path">Where: a data path such as <c>$.embed.images[0].alt</c>, where <c>$</c> is the value validated; <c>rkey</c> for the record key.</param>
/// <param name="Message">What is wrong.</param>
public sealed record LexiconViolation(string Path, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>The outcome of validating data against a Lexicon (LEX-18).</summary>
public sealed class LexiconValidationResult
{
    internal LexiconValidationResult(IReadOnlyList<LexiconViolation> violations) => Violations = violations;

    /// <summary>Whether the data has no violations.</summary>
    public bool IsValid => Violations.Count == 0;

    /// <summary>The violations in the order found; at most <see cref="LexiconLimits.MaxValidationErrors"/>.</summary>
    public IReadOnlyList<LexiconViolation> Violations { get; }

    /// <summary>Throws when the data has violations.</summary>
    /// <exception cref="LexiconValidationException">The data has violations.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new LexiconValidationException(Violations);
    }
}

/// <summary>Data does not match its Lexicon, where an API must throw rather than return a <see cref="LexiconValidationResult"/>.</summary>
/// <param name="violations">The violations; at least one.</param>
public sealed class LexiconValidationException(IReadOnlyList<LexiconViolation> violations)
    : AtProtoException(violations.Count switch
    {
        0 => "The data does not match its Lexicon.",
        1 => $"The data does not match its Lexicon: {violations[0]}",
        _ => $"The data does not match its Lexicon: {violations[0]} (and {violations.Count - 1} more)",
    })
{
    /// <summary>The violations.</summary>
    public IReadOnlyList<LexiconViolation> Violations { get; } = violations;
}
