using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Embed;

// ──────────────────────────────────────────────────────────────
//  Embeds a message is sent with (chat.bsky.convo.defs#messageInput.embed)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Content embedded in a message being sent (the open <c>chat.bsky.convo.defs#messageInput.embed</c>
/// union): a <see cref="MessageRecordEmbed"/> or a <see cref="JoinLinkEmbed"/>. An embed this SDK
/// does not model reads as <see cref="UnknownMessageEmbed"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownMessageEmbed))]
[JsonDerivedType(typeof(MessageRecordEmbed), "app.bsky.embed.record")]
[JsonDerivedType(typeof(JoinLinkEmbed), "chat.bsky.embed.joinLink")]
public abstract class MessageEmbed : LexObject;

/// <summary>
/// A message embed whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownMessageEmbed : MessageEmbed, IUnknownUnionVariant
{
    /// <summary>Creates an unknown message embed from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownMessageEmbed(string type, JsonElement raw)
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

/// <summary>
/// A record, such as a post, embedded in a message (<c>app.bsky.embed.record</c>).
/// </summary>
public sealed class MessageRecordEmbed : MessageEmbed
{
    /// <summary>A strong reference to the record.</summary>
    [JsonPropertyName("record")]
    public required StrongRef Record { get; init; }
}

/// <summary>
/// A group's join link embedded in a message (<c>chat.bsky.embed.joinLink</c>).
/// </summary>
public sealed class JoinLinkEmbed : MessageEmbed
{
    /// <summary>The join link's code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Embeds a message is read with (chat.bsky.convo.defs#messageView.embed)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Content embedded in a message as the chat service shows it (the open
/// <c>chat.bsky.convo.defs#messageView.embed</c> union): a <see cref="MessageRecordEmbedView"/> or a
/// <see cref="JoinLinkEmbedView"/>. An embed view this SDK does not model reads as
/// <see cref="UnknownMessageEmbedView"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownMessageEmbedView))]
[JsonDerivedType(typeof(MessageRecordEmbedView), "app.bsky.embed.record#view")]
[JsonDerivedType(typeof(JoinLinkEmbedView), "chat.bsky.embed.joinLink#view")]
public abstract class MessageEmbedView : LexObject;

/// <summary>
/// A message embed view whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownMessageEmbedView : MessageEmbedView, IUnknownUnionVariant
{
    /// <summary>Creates an unknown message embed view from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownMessageEmbedView(string type, JsonElement raw)
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

/// <summary>
/// The view of a record embedded in a message (<c>app.bsky.embed.record#view</c>).
/// </summary>
public sealed class MessageRecordEmbedView : MessageEmbedView
{
    /// <summary>
    /// The embedded record: an <see cref="EmbeddedRecord"/> for a post, a placeholder when it
    /// cannot be shown, or the view of a feed generator, list, labeler or starter pack.
    /// </summary>
    [JsonPropertyName("record")]
    public required EmbeddedRecordView Record { get; init; }
}

/// <summary>
/// The view of a join link embedded in a message (<c>chat.bsky.embed.joinLink#view</c>).
/// </summary>
public sealed class JoinLinkEmbedView : MessageEmbedView
{
    /// <summary>
    /// The group behind the link: a <see cref="JoinLinkPreviewView"/>, or a
    /// <see cref="DisabledJoinLinkPreviewView"/> or <see cref="InvalidJoinLinkPreviewView"/> when
    /// the link cannot be used.
    /// </summary>
    [JsonPropertyName("joinLinkPreview")]
    public required JoinLinkPreview JoinLinkPreview { get; init; }
}
