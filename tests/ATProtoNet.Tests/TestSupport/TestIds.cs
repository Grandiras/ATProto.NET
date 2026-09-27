namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Identifiers shared across client tests, so fixtures in different files agree on the same
/// DID instead of each declaring their own copy of the same literal.
/// </summary>
internal static class TestIds
{
    /// <summary>
    /// A DID many Lexicon client tests use as the moderator, owner, or actor under test.
    /// </summary>
    public const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
}
