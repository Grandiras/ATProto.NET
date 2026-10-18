namespace ATProtoNet.TestKit.Integration;

/// <summary>
/// What an integration test needs beyond the unit-test sandbox. Each test names one through
/// <see cref="RequiresFactAttribute"/> or <see cref="RequiresTheoryAttribute"/>, which also tags
/// it with the trait <c>Requirement=&lt;name&gt;</c>, so CI selects tests by trait, not class name.
/// </summary>
public enum Requirement
{
    /// <summary>A PDS the tests can sign in to: <c>ATPROTO_PDS_ADMIN_PASSWORD</c> (provisions accounts) or <c>ATPROTO_TEST_HANDLE</c>/<c>ATPROTO_TEST_PASSWORD</c>.</summary>
    Pds,

    /// <summary>The PDS admin password: <c>ATPROTO_PDS_ADMIN_PASSWORD</c>.</summary>
    PdsAdmin,

    /// <summary>A PDS with Bluesky app-view services: the above plus <c>ATPROTO_HAS_BLUESKY=true</c>.</summary>
    Bluesky,

    /// <summary>Outbound internet to a public Jetstream: <c>ATPROTO_TEST_JETSTREAM=true</c>.</summary>
    Jetstream,

    /// <summary>The above plus a Jetstream archive API key: <c>ATPROTO_JETSTREAM_API_KEY</c>.</summary>
    JetstreamArchive,

    /// <summary>A PDS serving <c>com.atproto.space.*</c>: <c>ATPROTO_TEST_SPACES=true</c> and <c>ATPROTO_PDS_ADMIN_PASSWORD</c>.</summary>
    Spaces,

    /// <summary>A Tranquil PDS: <c>ATPROTO_TRANQUIL_PDS_URL</c>.</summary>
    Tranquil,
}
