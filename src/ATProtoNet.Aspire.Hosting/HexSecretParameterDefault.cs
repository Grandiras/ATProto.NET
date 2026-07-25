using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Generates a random lowercase hex string of a fixed byte length.
/// </summary>
/// <remarks>
/// The PDS reads its JWT secret and PLC rotation key as hex, so the generated
/// alphanumeric passwords Aspire produces by default are not usable for them.
/// Combined with <c>persist: true</c>, values generated here are written to the
/// AppHost's user secrets and stay stable across runs — which matters because the
/// PDS data volume outlives any single run, and a rotation key that changed between
/// runs would orphan the identities already stored in it.
/// </remarks>
internal sealed class HexSecretParameterDefault(int byteCount) : ParameterDefault
{
    public override string GetDefaultValue() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    public override void WriteToManifest(ManifestPublishingContext context)
    {
        // Unreachable by construction: AddAtProtoPds only uses this default in run mode,
        // because a manifest `generate` block can only describe an alphanumeric string and
        // a deployment following one would provision a value the PDS rejects. Publishing
        // asks for the value instead. Fail loudly rather than emit a wrong instruction.
        throw new NotSupportedException(
            "A hex-encoded PDS secret cannot be expressed as an Aspire manifest 'generate' " +
            "instruction. Supply the value at deploy time, or pass your own parameter with " +
            "WithJwtSecret() / WithPlcRotationKey().");
    }
}
