using System.Runtime.CompilerServices;

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
    public RequiresPdsFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
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
    public RequiresBlueskyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
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
    public RequiresPdsAdminFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrEmpty(TestConfig.AdminPassword) && !TestConfig.IntegrationRequired)
        {
            Skip = "PDS admin tests require the ATPROTO_PDS_ADMIN_PASSWORD environment variable. " +
                   "Optionally set ATPROTO_PDS_URL (defaults to http://localhost:2583).";
        }
    }
}

/// <summary>
/// Skips a test that talks to Bluesky's public Jetstream instances.
/// Set ATPROTO_TEST_JETSTREAM=true to enable these tests.
/// </summary>
/// <remarks>
/// Unlike the other integration tests these need no PDS and no credentials — only outbound
/// internet access to <see cref="ATProtoNet.Streaming.JetstreamEndpoints.UsEast"/> — so they
/// have their own switch rather than riding on the PDS environment variables.
/// </remarks>
public sealed class RequiresJetstreamFactAttribute : FactAttribute
{
    public RequiresJetstreamFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestConfig.JetstreamEnabled && !TestConfig.IntegrationRequired)
        {
            Skip = "Jetstream tests require ATPROTO_TEST_JETSTREAM=true. " +
                   "They connect to Bluesky's public Jetstream instances over the internet.";
        }
    }
}

/// <summary>
/// Skips a test that talks to the Jetstream v2 archive (the metered HTTP replay endpoints).
/// </summary>
/// <remarks>
/// On top of <see cref="RequiresJetstreamFactAttribute"/>'s outbound internet these need an API
/// key: the archive endpoints are authenticated and metered in response bytes, unlike the live
/// WebSocket tail. Set <c>ATPROTO_JETSTREAM_API_KEY</c> to run them.
/// </remarks>
public sealed class RequiresJetstreamArchiveFactAttribute : FactAttribute
{
    public RequiresJetstreamArchiveFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (TestConfig.IntegrationRequired)
            return;

        if (!TestConfig.JetstreamEnabled)
        {
            Skip = "Jetstream tests require ATPROTO_TEST_JETSTREAM=true. " +
                   "They connect to Bluesky's public Jetstream instances over the internet.";
        }
        else if (string.IsNullOrEmpty(TestConfig.JetstreamApiKey))
        {
            Skip = "Jetstream archive tests require ATPROTO_JETSTREAM_API_KEY. " +
                   "The replay endpoints are authenticated and metered in response bytes.";
        }
    }
}

/// <summary>
/// Skips a test that talks to a PDS serving <c>com.atproto.space.*</c> — the permissioned data
/// protocol.
/// </summary>
/// <remarks>
/// <para>No PDS release serves these endpoints yet; they live on
/// <see href="https://github.com/bluesky-social/atproto/pull/5187">bluesky-social/atproto#5187</see>.
/// Until one does, these tests run against a dev network built from that branch — see
/// <c>docs/testing-spaces.md</c> for the three commands that stand one up.</para>
/// <para>They provision their own accounts rather than using <c>ATPROTO_TEST_HANDLE</c>, because
/// a space needs an authority, a second member to read across the repo boundary, and a
/// non-member to be refused. That takes the server's admin password, as
/// <see cref="RequiresPdsAdminFactAttribute"/> does.</para>
/// </remarks>
public sealed class RequiresSpacesFactAttribute : FactAttribute
{
    public RequiresSpacesFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (TestConfig.IntegrationRequired)
            return;

        if (!TestConfig.SpacesEnabled)
        {
            Skip = "Space tests require ATPROTO_TEST_SPACES=true and a PDS that serves " +
                   "com.atproto.space.* (no release does yet — see docs/testing-spaces.md).";
        }
        else if (string.IsNullOrEmpty(TestConfig.AdminPassword))
        {
            Skip = "Space tests provision their own accounts and require the " +
                   "ATPROTO_PDS_ADMIN_PASSWORD environment variable.";
        }
    }
}

/// <summary>
/// Skips a test that needs a live Redis, set with <c>ATPROTO_REDIS_URL</c>.
/// </summary>
/// <remarks>
/// The Redis replay store's whole claim — that a single-use token is spent once across every
/// instance — is a property of Redis executing <c>SET … NX</c> atomically, which no substitute
/// can demonstrate. These tests need nothing else: no PDS, no credentials, no internet.
/// </remarks>
public sealed class RequiresRedisFactAttribute : FactAttribute
{
    public RequiresRedisFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (TestConfig.IntegrationRequired)
            return;

        if (string.IsNullOrEmpty(TestConfig.RedisUrl))
        {
            Skip = "Redis tests require ATPROTO_REDIS_URL (e.g. localhost:6379). " +
                   "They need a Redis server and nothing else.";
        }
    }
}

public static class TestConfig
{
    /// <summary>The Redis server the replay store tests run against, if one was supplied.</summary>
    public static string RedisUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_REDIS_URL") ?? "";

    /// <summary>The Jetstream host the live protocol tests run against.</summary>
    public static string JetstreamUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_JETSTREAM_URL")
        ?? ATProtoNet.Streaming.JetstreamEndpoints.UsEast;

    /// <summary>The API key for the metered archive endpoints, if one was supplied.</summary>
    public static string JetstreamApiKey =>
        Environment.GetEnvironmentVariable("ATPROTO_JETSTREAM_API_KEY") ?? "";

    public static bool JetstreamEnabled =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_JETSTREAM") is "1" or "true" or "True";

    public static string PdsUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_PDS_URL") ?? "http://localhost:2583";

    public static string AdminPassword =>
        Environment.GetEnvironmentVariable("ATPROTO_PDS_ADMIN_PASSWORD") ?? "";

    public static bool SpacesEnabled =>
        Environment.GetEnvironmentVariable("ATPROTO_TEST_SPACES") is "1" or "true" or "True";

    /// <summary>The PDS the space tests run against, which is also the space authority's host.</summary>
    public static string SpacesPdsUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_SPACES_PDS_URL") ?? PdsUrl;

    /// <summary>
    /// The PLC directory the space PDS registers its accounts with.
    /// </summary>
    /// <remarks>
    /// Space credentials are exchanged with, and commits verified against, whatever the DID
    /// document says — so a test network's accounts have to resolve through that network's own
    /// PLC rather than the public directory.
    /// </remarks>
    public static string PlcUrl =>
        Environment.GetEnvironmentVariable("ATPROTO_PLC_URL") ?? "http://localhost:2582";

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
