using System.Runtime.CompilerServices;
using Xunit.v3;

namespace ATProtoNet.TestKit.Integration;

/// <summary>
/// A fact that needs a <see cref="Integration.Requirement"/>: skipped when it is not met (see
/// <see cref="IntegrationGate"/>), and tagged <c>Requirement=&lt;name&gt;</c>.
/// </summary>
public sealed class RequiresFactAttribute : FactAttribute, ITraitAttribute
{
    /// <summary>Gates the fact on <paramref name="requirement"/>.</summary>
    public RequiresFactAttribute(Requirement requirement, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Requirement = requirement;
        Skip = IntegrationGate.SkipReason(requirement);
    }

    /// <summary>What the test needs.</summary>
    public Requirement Requirement { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("Requirement", Requirement.ToString())];
}

/// <summary>The theory counterpart of <see cref="RequiresFactAttribute"/>.</summary>
public sealed class RequiresTheoryAttribute : TheoryAttribute, ITraitAttribute
{
    /// <summary>Gates the theory on <paramref name="requirement"/>.</summary>
    public RequiresTheoryAttribute(Requirement requirement, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Requirement = requirement;
        Skip = IntegrationGate.SkipReason(requirement);
    }

    /// <summary>What the test needs.</summary>
    public Requirement Requirement { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("Requirement", Requirement.ToString())];
}
