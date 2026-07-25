namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Skips a test when no PDS is available for integration testing.
/// Set environment variables:
///   ATPROTO_PDS_URL (default: http://localhost:2583)
///   ATPROTO_TEST_HANDLE
///   ATPROTO_TEST_PASSWORD
/// </summary>
public sealed class RequiresPdsFactAttribute : FactAttribute
{
    public RequiresPdsFactAttribute()
    {
        var handle = Environment.GetEnvironmentVariable("ATPROTO_TEST_HANDLE");
        var password = Environment.GetEnvironmentVariable("ATPROTO_TEST_PASSWORD");

        if (string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(password))
        {
            Skip = "Integration tests require ATPROTO_TEST_HANDLE and ATPROTO_TEST_PASSWORD environment variables. " +
                   "Optionally set ATPROTO_PDS_URL (defaults to http://localhost:2583).";
        }
    }
}

/// <summary>
/// Test configuration sourced from environment variables.
/// </summary>
/// <summary>
/// Skips a test that requires Bluesky app view services (not available on a bare PDS).
/// Set ATPROTO_HAS_BLUESKY=true to enable these tests.
/// </summary>
public sealed class RequiresBlueskyFactAttribute : FactAttribute
{
    public RequiresBlueskyFactAttribute()
    {
        var handle = Environment.GetEnvironmentVariable("ATPROTO_TEST_HANDLE");
        var password = Environment.GetEnvironmentVariable("ATPROTO_TEST_PASSWORD");
        var hasBluesky = Environment.GetEnvironmentVariable("ATPROTO_HAS_BLUESKY");

        if (string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(password))
        {
            Skip = "Integration tests require ATPROTO_TEST_HANDLE and ATPROTO_TEST_PASSWORD environment variables.";
        }
        else if (!string.Equals(hasBluesky, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Bluesky app view tests require ATPROTO_HAS_BLUESKY=true. " +
                   "A bare PDS does not have app.bsky.* services configured.";
        }
    }
}

/// <summary>
/// Skips a test that administers a PDS when no admin password is available.
/// Set ATPROTO_PDS_ADMIN_PASSWORD (and optionally ATPROTO_PDS_URL).
/// </summary>
/// <remarks>
/// Unlike <see cref="RequiresPdsFactAttribute"/> these tests need no pre-existing
/// account — they provision their own — but they do need the server's admin password.
/// </remarks>
public sealed class RequiresPdsAdminFactAttribute : FactAttribute
{
    public RequiresPdsAdminFactAttribute()
    {
        if (string.IsNullOrEmpty(TestConfig.AdminPassword) && !TestConfig.IntegrationRequired)
        {
            Skip = "PDS admin tests require the ATPROTO_PDS_ADMIN_PASSWORD environment variable. " +
                   "Optionally set ATPROTO_PDS_URL (defaults to http://localhost:2583).";
        }
    }
}

public static class TestConfig
{
    public static string PdsUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_PDS_URL") ?? "http://localhost:2583";

    public static string AdminPassword =>
        Environment.GetEnvironmentVariable("ATPROTO_PDS_ADMIN_PASSWORD") ?? "";

    /// <summary>
    /// Whether environment-dependent tests must run rather than skip.
    /// </summary>
    /// <remarks>
    /// <c>dotnet test --filter</c> exits 0 when every matched test skips, so a CI job
    /// whose environment variables drifted would pass while verifying nothing. Setting
    /// <c>ATPROTO_REQUIRE_INTEGRATION=1</c> makes a missing prerequisite a failure.
    /// </remarks>
    public static bool IntegrationRequired =>
        Environment.GetEnvironmentVariable("ATPROTO_REQUIRE_INTEGRATION") is "1" or "true";

    public static string Handle =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_HANDLE") ?? "";

    public static string Password =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_PASSWORD") ?? "";
}
