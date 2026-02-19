using ATProtoNet.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet;

/// <summary>
/// Builder for constructing a configured <see cref="AtProtoClient"/> instance.
/// </summary>
/// <example>
/// <code>
/// var client = new AtProtoClientBuilder()
///     .WithInstanceUrl("https://bsky.social")
///     .WithSessionStore(new FileSessionStore("session.json"))
///     .Build();
/// </code>
/// </example>
public sealed class AtProtoClientBuilder
{
    private string _instanceUrl = "https://bsky.social";
    private bool _autoRefreshSession = true;
    private HttpClient? _httpClient;
    private ISessionStore? _sessionStore;
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
    /// Set whether the client auto-refreshes the session token.
    /// Default: true.
    /// </summary>
    public AtProtoClientBuilder WithAutoRefreshSession(bool enabled)
    {
        _autoRefreshSession = enabled;
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
    /// Provide a session store for persisting session tokens.
    /// Default: <see cref="InMemorySessionStore"/> (lost on restart).
    /// </summary>
    public AtProtoClientBuilder WithSessionStore(ISessionStore sessionStore)
    {
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
        };

        var logger = _loggerFactory?.CreateLogger<AtProtoClient>();

        return new AtProtoClient(options, _httpClient, _sessionStore, logger);
    }
}
