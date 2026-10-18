namespace ATProtoNet.TestKit.Integration;

/// <summary>
/// Decides whether an integration test runs. A missing prerequisite skips the test, unless
/// <c>ATPROTO_REQUIRE_INTEGRATION=1</c> is set: then the test runs and fails on what it lacks, so a
/// CI job whose environment drifted cannot pass by skipping everything.
/// </summary>
public static class IntegrationGate
{
    /// <summary>The environment variable that forbids skipping.</summary>
    public const string RequireVariable = "ATPROTO_REQUIRE_INTEGRATION";

    /// <summary>Whether integration tests must run rather than skip.</summary>
    public static bool IsRequired => Variable(RequireVariable) is "1" or "true";

    /// <summary>The reason to skip a test needing <paramref name="requirement"/>, or <see langword="null"/> to run it.</summary>
    public static string? SkipReason(Requirement requirement) =>
        IsRequired ? null : MissingPrerequisite(requirement);

    /// <summary>What <paramref name="requirement"/> lacks in this environment, or <see langword="null"/> when it is met.</summary>
    public static string? MissingPrerequisite(Requirement requirement) => requirement switch
    {
        Requirement.Pds => CanSignIn ? null : "needs ATPROTO_PDS_ADMIN_PASSWORD, or ATPROTO_TEST_HANDLE and ATPROTO_TEST_PASSWORD",
        Requirement.PdsAdmin => Set("ATPROTO_PDS_ADMIN_PASSWORD") ? null : "needs ATPROTO_PDS_ADMIN_PASSWORD",
        Requirement.Bluesky => !CanSignIn ? MissingPrerequisite(Requirement.Pds) : Flag("ATPROTO_HAS_BLUESKY") ? null : "needs ATPROTO_HAS_BLUESKY=true (a bare PDS has no app.bsky.* services)",
        Requirement.Jetstream => Flag("ATPROTO_TEST_JETSTREAM") ? null : "needs ATPROTO_TEST_JETSTREAM=true (connects to a public Jetstream)",
        Requirement.JetstreamArchive => MissingPrerequisite(Requirement.Jetstream) ?? (Set("ATPROTO_JETSTREAM_API_KEY") ? null : "needs ATPROTO_JETSTREAM_API_KEY"),
        Requirement.Spaces => !Flag("ATPROTO_TEST_SPACES") ? "needs ATPROTO_TEST_SPACES=true and a PDS serving com.atproto.space.*" : MissingPrerequisite(Requirement.PdsAdmin),
        Requirement.Tranquil => Set("ATPROTO_TRANQUIL_PDS_URL") ? null : "needs ATPROTO_TRANQUIL_PDS_URL",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, null),
    };

    /// <summary>The PDS integration tests run against (<c>ATPROTO_PDS_URL</c>, default <c>http://localhost:2583</c>).</summary>
    public static string PdsUrl => Variable("ATPROTO_PDS_URL") ?? "http://localhost:2583";

    private static bool CanSignIn => Set("ATPROTO_PDS_ADMIN_PASSWORD") || Set("ATPROTO_TEST_HANDLE") && Set("ATPROTO_TEST_PASSWORD");

    private static bool Set(string name) => !string.IsNullOrEmpty(Variable(name));

    private static bool Flag(string name) => Variable(name) is "1" or "true" or "True";

    private static string? Variable(string name) => Environment.GetEnvironmentVariable(name);
}
