namespace ATProtoNet.Spaces;

/// <summary>A space type as a Lexicon <c>space</c> definition declares it: its name and the collections its spaces hold (LEX-76).</summary>
/// <param name="Type">The space type: the definition's NSID.</param>
/// <param name="Name">The name shown on consent screens, if declared.</param>
/// <param name="LocalizedNames">Localized names by language, if declared.</param>
/// <param name="Collections">The collections spaces of this type hold, as declared; the default set of a bare <c>space:</c> grant.</param>
public sealed record SpaceTypeDeclaration(Nsid Type, string? Name, IReadOnlyDictionary<string, string>? LocalizedNames, IReadOnlyList<string> Collections);
