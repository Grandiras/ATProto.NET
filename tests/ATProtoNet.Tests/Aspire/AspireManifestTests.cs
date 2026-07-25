using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ATProtoNet.Tests.Aspire;

/// <summary>
/// Skips a test when no published Aspire manifest is available.
/// Set ATPROTO_ASPIRE_MANIFEST to the path of a manifest produced by
/// <c>dotnet run --project samples/ManagedPdsSample.AppHost -- --publisher manifest</c>.
/// </summary>
public sealed class RequiresAspireManifestFactAttribute : FactAttribute
{
    public RequiresAspireManifestFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = AspireManifestTests.UnavailableReason() is { } reason
            ? IntegrationGate.SkipUnlessRequired(reason)
            : null;
    }
}

/// <summary>
/// <see cref="RequiresAspireManifestFactAttribute"/> for data-driven tests.
/// </summary>
public sealed class RequiresAspireManifestTheoryAttribute : TheoryAttribute
{
    public RequiresAspireManifestTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = AspireManifestTests.UnavailableReason() is { } reason
            ? IntegrationGate.SkipUnlessRequired(reason)
            : null;
    }
}

/// <summary>
/// Asserts on a manifest the AppHost sample actually produced.
/// </summary>
/// <remarks>
/// The unit tests around <c>AddAtProtoPds</c> check the Aspire application model; this
/// checks what a deployment is ultimately handed, which is where local-only defaults
/// would leak through.
/// </remarks>
public class AspireManifestTests
{
    [RequiresAspireManifestFact]
    public void Manifest_DoesNotCarryLocalOnlyDefaults()
    {
        var pds = Resources().GetProperty("pds").GetProperty("env");

        // "localhost" would give a deployed PDS a did:web identity nothing can resolve.
        var hostname = pds.GetProperty("PDS_HOSTNAME").GetString();
        Assert.StartsWith("{", hostname);
        Assert.EndsWith("}", hostname);

        // Dev mode relaxes checks a deployment needs; unset, the container defaults it off.
        Assert.False(pds.TryGetProperty("PDS_DEV_MODE", out _));
    }

    [RequiresAspireManifestFact]
    public void Manifest_ConfiguresABlobstore()
    {
        var pds = Resources().GetProperty("pds").GetProperty("env");

        // Without this the container exits with "Must configure either S3 or disk blobstore".
        Assert.Equal("/pds/blocks", pds.GetProperty("PDS_BLOBSTORE_DISK_LOCATION").GetString());
    }

    [RequiresAspireManifestTheory]
    [InlineData("pds-jwt-secret")]
    [InlineData("pds-plc-rotation-key")]
    public void Manifest_HexSecretsHaveNoGeneratedDefault(string parameterName)
    {
        var input = Resources().GetProperty(parameterName).GetProperty("inputs").GetProperty("value");

        Assert.True(input.GetProperty("secret").GetBoolean());

        // Aspire can only instruct a deployment to generate an alphanumeric string, and
        // the PDS parses these as hex — so the value has to be supplied, not generated.
        Assert.False(input.TryGetProperty("default", out _));
    }

    [RequiresAspireManifestFact]
    public void Manifest_AdminPasswordKeepsItsGeneratedDefault()
    {
        var input = Resources().GetProperty("pds-admin-password")
            .GetProperty("inputs").GetProperty("value");

        // A password has no hex constraint, so generating one is fine.
        Assert.True(input.GetProperty("default").TryGetProperty("generate", out _));
    }

    [RequiresAspireManifestFact]
    public void Manifest_WiresTheConsumerWithoutAllowingPlaintextHttp()
    {
        var api = Resources().GetProperty("api").GetProperty("env");

        Assert.True(api.TryGetProperty("AtProto__Pds__Url", out _));
        Assert.True(api.TryGetProperty("AtProto__Pds__AdminPassword", out _));

        // Sending the admin password unencrypted in a deployment is the operator's call.
        Assert.False(api.TryGetProperty("AtProto__Pds__AllowInsecureHttp", out _));
    }

    /// <summary>
    /// The environment variable naming the manifest to assert on.
    /// </summary>
    public const string ManifestPathVariable = "ATPROTO_ASPIRE_MANIFEST";

    /// <summary>
    /// Why these tests cannot run here, or <c>null</c> when they can.
    /// </summary>
    internal static string? UnavailableReason()
    {
        var path = Environment.GetEnvironmentVariable(ManifestPathVariable);

        if (string.IsNullOrEmpty(path))
        {
            return $"Set {ManifestPathVariable} to a manifest produced by the AppHost sample " +
                   "(dotnet run --project samples/ManagedPdsSample.AppHost -- --publisher manifest).";
        }

        return File.Exists(path) ? null : $"No Aspire manifest at '{path}'.";
    }

    private static JsonElement Resources()
    {
        if (UnavailableReason() is { } reason)
        {
            // Reached only when the gate refused to skip, i.e. CI expected this to run.
            throw new InvalidOperationException(reason);
        }

        var path = Environment.GetEnvironmentVariable(ManifestPathVariable)!;
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("resources");
    }
}
