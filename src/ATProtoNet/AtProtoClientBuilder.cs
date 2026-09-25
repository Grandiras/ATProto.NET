using ATProtoNet.Auth;
using Microsoft.Extensions.Logging;

namespace ATProtoNet;

/// <summary>
/// Builder for constructing a configured <see cref="AtProtoClient"/> instance.
/// </summary>
/// <example>
/// <code>
/// var client = new AtProtoClientBuilder()
///     .WithInstanceUrl("https://bsky.social")
///     .WithSessionStore(new InMemoryAtProtoSessionStore())
///     .Build();
/// </code>
/// </example>
public sealed class AtProtoClientBuilder
{
    private string _instanceUrl = "https://bsky.social";
    private string? _relayUrl = "wss://bsky.network";
    private bool _autoRefreshSession = true;
    private bool _backgroundRefresh;
    private HttpClient? _httpClient;
    private IAtProtoSessionStore? _sessionStore;
    private ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Set the PDS / service instance URL.
    /// Default: "https://bsky.social"
    /// </summary>
    public AtProtoClientBuilder WithInstanceUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _instanceUrl = url;
        return this;
    }

    /// <summary>
    /// Set the WebSocket URL of the relay service for firehose subscriptions.
    /// Default: "wss://bsky.network". Pass <c>null</c> to disable relay integration.
    /// </summary>
    public AtProtoClientBuilder WithRelayUrl(string? url)
    {
        _relayUrl = url;
        return this;
    }

    /// <summary>
    /// Set whether the client refreshes the session by itself, before the access token expires
    /// and after the service rejects it as expired. Default: true.
    /// See <see cref="AtProtoClientOptions.AutoRefreshSession"/>.
    /// </summary>
    public AtProtoClientBuilder WithAutoRefreshSession(bool enabled)
    {
        _autoRefreshSession = enabled;
        return this;
    }

    /// <summary>
    /// Set whether the client also refreshes on a timer while idle. Default: false.
    /// See <see cref="AtProtoClientOptions.BackgroundRefresh"/>.
    /// </summary>
    public AtProtoClientBuilder WithBackgroundRefresh(bool enabled)
    {
        _backgroundRefresh = enabled;
        return this;
    }

    /// <summary>
    /// Provide a custom HttpClient instance.
    /// Caller is responsible for disposal.
    /// </summary>
    public AtProtoClientBuilder WithHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        return this;
    }

    /// <summary>
    /// Provide a store the client persists its session to: every session it installs or
    /// refreshes is written, and a signed-out or expired one removed. Default: none.
    /// <see cref="AtProtoClient.TryRestoreSessionAsync"/> reads it back.
    /// </summary>
    public AtProtoClientBuilder WithSessionStore(IAtProtoSessionStore sessionStore)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        _sessionStore = sessionStore;
        return this;
    }

    /// <summary>
    /// Provide a logger factory for structured logging.
    /// </summary>
    public AtProtoClientBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Build the <see cref="AtProtoClient"/> with the configured options.
    /// </summary>
    public AtProtoClient Build()
    {
        var options = new AtProtoClientOptions
        {
            InstanceUrl = _instanceUrl,
            AutoRefreshSession = _autoRefreshSession,
            BackgroundRefresh = _backgroundRefresh,
            RelayUrl = _relayUrl,
        };

        var logger = _loggerFactory?.CreateLogger<AtProtoClient>();

        return new AtProtoClient(options, _httpClient, _sessionStore, logger);
    }
}
