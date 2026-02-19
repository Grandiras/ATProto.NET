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

public static class TestConfig
{
    public static string PdsUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_PDS_URL") ?? "http://localhost:2583";

    public static string Handle =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_HANDLE") ?? "";

    public static string Password =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_PASSWORD") ?? "";
}
