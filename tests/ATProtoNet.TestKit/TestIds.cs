namespace ATProtoNet.TestKit;

/// <summary>Identifiers shared across tests, so fixtures agree on one account.</summary>
public static class TestIds
{
    /// <summary>The account most tests act as.</summary>
    public const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    /// <summary>The handle of <see cref="Did"/>.</summary>
    public const string Handle = "alice.test";

    /// <summary>A second account, for tests with two parties.</summary>
    public const string OtherDid = "did:plc:yk4dd2qkboz2yv6tpubpc6co";

    /// <summary>The handle of <see cref="OtherDid"/>.</summary>
    public const string OtherHandle = "bob.test";

    /// <summary>The PDS both accounts live on.</summary>
    public const string Pds = "https://pds.example.com";

    /// <summary>A fixed instant for tests that need a clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
}
