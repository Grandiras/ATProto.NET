using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Hosting;

/// <summary>
/// One change to an account on its host, such as an email or handle update
/// (<c>tools.ozone.hosting.getAccountHistory#event</c>).
/// </summary>
public sealed class AccountHistoryEvent : LexObject
{
    /// <summary>What changed.</summary>
    [JsonPropertyName("details")]
    public required AccountHistoryDetails Details { get; init; }

    /// <summary>Who made the change, as the host records it.</summary>
    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    /// <summary>When the change was made.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// What changed in an account history event (the open
/// <c>tools.ozone.hosting.getAccountHistory#event.details</c> union). A change this SDK does not
/// model reads as <see cref="UnknownAccountHistoryDetails"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownAccountHistoryDetails))]
[JsonDerivedType(typeof(AccountCreated), "tools.ozone.hosting.getAccountHistory#accountCreated")]
[JsonDerivedType(typeof(EmailUpdated), "tools.ozone.hosting.getAccountHistory#emailUpdated")]
[JsonDerivedType(typeof(EmailConfirmed), "tools.ozone.hosting.getAccountHistory#emailConfirmed")]
[JsonDerivedType(typeof(PasswordUpdated), "tools.ozone.hosting.getAccountHistory#passwordUpdated")]
[JsonDerivedType(typeof(HandleUpdated), "tools.ozone.hosting.getAccountHistory#handleUpdated")]
public abstract class AccountHistoryDetails : LexObject;

/// <summary>
/// Account history details whose <c>$type</c> this SDK version does not model. They keep the raw
/// object and write it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownAccountHistoryDetails : AccountHistoryDetails, IUnknownUnionVariant
{
    /// <summary>Creates unknown account history details from their discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownAccountHistoryDetails(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>The account was created.</summary>
public sealed class AccountCreated : AccountHistoryDetails
{
    /// <summary>The email address the account was created with.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>The handle the account was created with.</summary>
    [JsonPropertyName("handle")]
    public Handle? Handle { get; init; }
}

/// <summary>The account's email address changed.</summary>
public sealed class EmailUpdated : AccountHistoryDetails
{
    /// <summary>The new email address.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>The account's email address was confirmed.</summary>
public sealed class EmailConfirmed : AccountHistoryDetails
{
    /// <summary>The confirmed email address.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>The account's password changed.</summary>
public sealed class PasswordUpdated : AccountHistoryDetails;

/// <summary>The account's handle changed.</summary>
public sealed class HandleUpdated : AccountHistoryDetails
{
    /// <summary>The new handle.</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }
}

/// <summary>
/// The kinds of account history event, for <see cref="HostingClient.GetAccountHistoryAsync"/>.
/// </summary>
public static class AccountHistoryEventType
{
    /// <summary>The account was created.</summary>
    public const string AccountCreated = "accountCreated";

    /// <summary>The account's email address changed.</summary>
    public const string EmailUpdated = "emailUpdated";

    /// <summary>The account's email address was confirmed.</summary>
    public const string EmailConfirmed = "emailConfirmed";

    /// <summary>The account's password changed.</summary>
    public const string PasswordUpdated = "passwordUpdated";

    /// <summary>The account's handle changed.</summary>
    public const string HandleUpdated = "handleUpdated";
}

/// <summary>
/// Response from tools.ozone.hosting.getAccountHistory.
/// </summary>
public sealed class GetAccountHistoryResponse : ICursorPage<AccountHistoryEvent>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The events.</summary>
    [JsonPropertyName("events")]
    public required IReadOnlyList<AccountHistoryEvent> Events { get; init; }

    IReadOnlyList<AccountHistoryEvent> ICursorPage<AccountHistoryEvent>.Items => Events;
}
