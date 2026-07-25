namespace ATProtoNet.Tests;

/// <summary>
/// Decides whether an environment-dependent test may skip.
/// </summary>
/// <remarks>
/// <para>
/// Skipping is right on a developer's machine, where the PDS container or a published
/// manifest usually isn't there. It is wrong in CI: <c>dotnet test --filter</c> exits 0
/// when every matched test skips, so a mistyped environment variable turns a job that
/// verifies nothing into a green check.
/// </para>
/// <para>
/// Set <c>ATPROTO_REQUIRE_INTEGRATION=1</c> wherever these tests are expected to run.
/// The gate then refuses to skip, and the test fails on the missing prerequisite
/// instead.
/// </para>
/// </remarks>
public static class IntegrationGate
{
    /// <summary>
    /// The environment variable that forbids skipping.
    /// </summary>
    public const string RequireVariable = "ATPROTO_REQUIRE_INTEGRATION";

    /// <summary>
    /// Whether environment-dependent tests must run rather than skip.
    /// </summary>
    public static bool IsRequired =>
        Environment.GetEnvironmentVariable(RequireVariable) is "1" or "true";

    /// <summary>
    /// Returns the skip reason to apply, or <c>null</c> to let the test run — and fail on
    /// the missing prerequisite — when it is required.
    /// </summary>
    /// <param name="reason">Why the test cannot run here.</param>
    public static string? SkipUnlessRequired(string reason) => IsRequired ? null : reason;
}
