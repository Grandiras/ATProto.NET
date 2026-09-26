using System.Runtime.CompilerServices;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// What an integration test needs beyond the unit-test sandbox: a live PDS, Bluesky app-view
/// services on it, its admin password, outbound internet to Jetstream, a Jetstream API key, or a
/// PDS that serves the permissioned-data (spaces) protocol. See <see cref="RequiresFactAttribute"/>.
/// </summary>
public enum IntegrationRequirement
{
    /// <summary>A PDS and a pre-existing account: <c>ATPROTO_TEST_HANDLE</c> / <c>ATPROTO_TEST_PASSWORD</c>.</summary>
    Pds,

    /// <summary>The above, plus Bluesky app-view services: <c>ATPROTO_HAS_BLUESKY=true</c>.</summary>
    Bluesky,

    /// <summary>The server's admin password: <c>ATPROTO_PDS_ADMIN_PASSWORD</c>. Provisions its own accounts.</summary>
    PdsAdmin,

    /// <summary>Outbound internet to Bluesky's public Jetstream: <c>ATPROTO_TEST_JETSTREAM=true</c>.</summary>
    Jetstream,

    /// <summary>The above, plus a Jetstream v2 archive API key: <c>ATPROTO_JETSTREAM_API_KEY</c>.</summary>
    JetstreamArchive,

    /// <summary>
    /// A PDS serving <c>com.atproto.space.*</c> and its admin password. Provisions its own
    /// accounts. See <c>docs/testing-spaces.md</c>.
    /// </summary>
    Spaces,
}

/// <summary>
/// Resolves what each <see cref="IntegrationRequirement"/> needs into a skip reason, or
/// <see langword="null"/> when it is satisfied.
/// </summary>
internal static class IntegrationRequirements
{
    public static string? SkipReason(IntegrationRequirement requirement)
    {
        var reason = requirement switch
        {
            IntegrationRequirement.Pds => PdsReason(),
            IntegrationRequirement.Bluesky => BlueskyReason(),
            IntegrationRequirement.PdsAdmin => PdsAdminReason(),
            IntegrationRequirement.Jetstream => JetstreamReason(),
            IntegrationRequirement.JetstreamArchive => JetstreamArchiveReason(),
            IntegrationRequirement.Spaces => SpacesReason(),
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, null),
        };

        // dotnet test --filter exits 0 when every matched test skips, so a CI job whose
        // environment variables drifted would pass while verifying nothing.
        // ATPROTO_REQUIRE_INTEGRATION=1 makes a missing prerequisite a failure instead: the
        // attribute lets the test run, and it fails on its own for lacking what it needs.
        return reason is not null && !TestConfig.IntegrationRequired ? reason : null;
    }

    private static bool HasAccount => !string.IsNullOrEmpty(TestConfig.Handle) && !string.IsNullOrEmpty(TestConfig.Password);

    /// <summary>
    /// Whether a test can get an authenticated client at all: either <see cref="AuthenticatedClientFixture"/>
    /// signs in with an existing account (<c>ATPROTO_TEST_HANDLE</c> / <c>ATPROTO_TEST_PASSWORD</c>), or
    /// it provisions a throwaway one through the admin API (<c>ATPROTO_PDS_ADMIN_PASSWORD</c>).
    /// </summary>
    private static bool CanAuthenticate => HasAccount || !string.IsNullOrEmpty(TestConfig.AdminPassword);

    private static string? PdsReason() => CanAuthenticate
        ? null
        : "Integration tests require ATPROTO_PDS_ADMIN_PASSWORD (to provision a throwaway account) or " +
          "ATPROTO_TEST_HANDLE and ATPROTO_TEST_PASSWORD (to sign in as an existing one). " +
          "Optionally set ATPROTO_PDS_URL (defaults to http://localhost:2583).";

    private static string? BlueskyReason()
    {
        if (!CanAuthenticate)
        {
            return "Integration tests require ATPROTO_PDS_ADMIN_PASSWORD or ATPROTO_TEST_HANDLE / ATPROTO_TEST_PASSWORD.";
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("ATPROTO_HAS_BLUESKY"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return "Bluesky app view tests require ATPROTO_HAS_BLUESKY=true. " +
                   "A bare PDS does not have app.bsky.* services configured.";
        }

        return null;
    }

    private static string? PdsAdminReason() => string.IsNullOrEmpty(TestConfig.AdminPassword)
        ? "PDS admin tests require the ATPROTO_PDS_ADMIN_PASSWORD environment variable. " +
          "Optionally set ATPROTO_PDS_URL (defaults to http://localhost:2583)."
        : null;

    private static string? JetstreamReason() => TestConfig.JetstreamEnabled
        ? null
        : "Jetstream tests require ATPROTO_TEST_JETSTREAM=true. " +
          "They connect to Bluesky's public Jetstream instances over the internet.";

    private static string? JetstreamArchiveReason()
    {
        if (!TestConfig.JetstreamEnabled)
        {
            return "Jetstream tests require ATPROTO_TEST_JETSTREAM=true. " +
                   "They connect to Bluesky's public Jetstream instances over the internet.";
        }

        if (string.IsNullOrEmpty(TestConfig.JetstreamApiKey))
        {
            return "Jetstream archive tests require ATPROTO_JETSTREAM_API_KEY. " +
                   "The replay endpoints are authenticated and metered in response bytes.";
        }

        return null;
    }

    private static string? SpacesReason()
    {
        if (!TestConfig.SpacesEnabled)
        {
            return "Space tests require ATPROTO_TEST_SPACES=true and a PDS that serves " +
                   "com.atproto.space.* (no release does yet — see docs/testing-spaces.md).";
        }

        if (string.IsNullOrEmpty(TestConfig.AdminPassword))
        {
            return "Space tests provision their own accounts and require the " +
                   "ATPROTO_PDS_ADMIN_PASSWORD environment variable.";
        }

        return null;
    }
}

/// <summary>
/// An integration <see cref="FactAttribute"/> gated on an <see cref="IntegrationRequirement"/>:
/// skipped when it is not met, unless <c>ATPROTO_REQUIRE_INTEGRATION</c> is set, in which case the
/// test runs and fails on its own for lacking what it needs.
/// </summary>
public sealed class RequiresFactAttribute : FactAttribute
{
    public RequiresFactAttribute(
        IntegrationRequirement requirement,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = IntegrationRequirements.SkipReason(requirement);
    }
}

public static class TestConfig
{
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
