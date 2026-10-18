namespace DocSnippets;

/// <summary>
/// The variables documentation samples take for granted from the code around them. Every
/// compiled sample derives from this class, so it can use these without declaring them; a
/// sample that declares its own variable of the same name shadows the one here.
/// </summary>
/// <remarks>
/// Keep this list short and conventional. The client variable (<c>client</c>) arrives with
/// <c>AtProtoClient</c> in phase 5.
/// </remarks>
public abstract class SnippetContext
{
    /// <summary>The application's service collection, as in <c>Program.cs</c>.</summary>
    protected IServiceCollection services = null!;

    /// <summary>The web application builder, as in <c>Program.cs</c>.</summary>
    protected WebApplicationBuilder builder = null!;

    /// <summary>The built web application, as in <c>Program.cs</c>.</summary>
    protected WebApplication app = null!;

    /// <summary>The application's configuration.</summary>
    protected IConfiguration configuration = null!;

    /// <summary>A logger.</summary>
    protected ILogger logger = null!;

    /// <summary>An <see cref="HttpClient"/> the application already has.</summary>
    protected HttpClient httpClient = null!;

    /// <summary>A cancellation token.</summary>
    protected CancellationToken cancellationToken;

    /// <summary>A cancellation token, under its short name.</summary>
    protected CancellationToken ct;

    /// <summary>The command-line arguments, as in <c>Program.cs</c>.</summary>
    protected string[] args = [];
}
