# Bluesky

`client.Bsky` is the `app.bsky.*` Lexicons: profiles, posts and feeds, the social graph,
notifications, and the rest of what the Bluesky app does. This page covers the common tasks and the
parts that need more than a method name; IntelliSense and the XML documentation list every method.

```csharp
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Com.AtProto.Repo;

var client = new AtProtoClient();
await client.LoginAsync("alice.bsky.social", "app-password");
```

| Property | Lexicons |
|----------|----------|
| `Actor` | Profiles, preferences, actor search and suggestions |
| `Feed` | Timelines, author and custom feeds, threads, likes, reposts, quotes, post search |
| `Graph` | Follows, blocks, mutes, lists, starter packs |
| `Notification` | Notifications, notification preferences, activity subscriptions, push |
| `Labeler` | Label service declarations (see [Labeler Services](labeler.md)) |
| `Video` | Video upload (see [Video Upload](video.md)) |
| `Bookmark` | Private bookmarks |
| `Draft` | Private post drafts |
| `Embed` | Enhanced link cards from records |
| `AgeAssurance` | Age assurance state and regional rules |
| `Unspecced` | Endpoints the Bluesky app uses before they are specified; may change without notice |

Actor parameters take an `AtIdentifier` (a `Did` or `Handle` converts implicitly), post, feed and
list parameters an `AtUri`, and the view models carry the same types (`PostView.Uri` is an `AtUri`,
`ProfileView.Did` a `Did`, `IndexedAt` an `AtDatetime`).

## Writing as the signed-in account

The helpers on `client.Bsky` write to the signed-in account's repository. Each returns a
`RecordRef` (except the delete) and throws `XrpcAuthenticationException` with
`AuthenticationRequired` when no one is signed in.

### Posting

`PostAsync` takes a `RichText`: a `string` converts to one, and `RichTextBuilder` builds one with
mentions, links and hashtags as facets. `PostOptions` holds the rest of the post:

```csharp continued
var hello = await client.Bsky.PostAsync("Hello from ATProto.NET!");

var text = new RichTextBuilder()
    .Text("Reading the ")
    .Link("AT Protocol docs", "https://atproto.com")
    .Text(" with ")
    .Mention(Handle.Parse("bob.bsky.social"), Did.Parse("did:plc:bob123"))
    .Text(" ")
    .Tag("atproto")
    .Build();

var reply = await client.Bsky.PostAsync(text, new PostOptions
{
    Reply = new ReplyRef { Root = hello.ToStrongRef(), Parent = hello.ToStrongRef() },
    Langs = ["en"],
});
```

An image is uploaded as a blob first, then embedded:

```csharp continued
await using var photo = File.OpenRead("cat.jpg");
var blob = await client.Repo.UploadBlobAsync(photo, "image/jpeg");

await client.Bsky.PostAsync("My cat", new PostOptions
{
    Embed = new ImagesEmbed { Images = [new EmbedImage { Image = blob, Alt = "A cat on a sofa" }] },
});
```

See [Blob Upload](blob-upload.md) for blobs, and [Video Upload](video.md) for video embeds.

### Likes, reposts, follows and deleting

```csharp continued
var like = await client.Bsky.LikeAsync(reply.ToStrongRef());
var repost = await client.Bsky.RepostAsync(reply.ToStrongRef());
var follow = await client.Bsky.FollowAsync(Did.Parse("did:plc:bob123"));

// A post, like, repost or follow is deleted by its AT URI
await client.Bsky.DeleteRecordAsync(like.Uri);
await client.Bsky.DeleteRecordAsync(follow.Uri);
```

A `StrongRef` names one version of a record: a `RecordRef` makes one with `ToStrongRef()`, and a
`PostView` from a feed carries the `Uri` and `Cid` to build one (`new StrongRef { Uri = post.Uri, Cid = post.Cid }`).

### The profile

`UpdateProfileAsync` reads the profile record, applies your edit, and writes it back, keeping every
field you do not touch and retrying if another client edits it in between:

```csharp continued
await client.Bsky.UpdateProfileAsync(profile =>
{
    profile.DisplayName = "Alice";
    profile.Description = "Building on the AT Protocol";
});
```

## Reading

```csharp continued
var profile = await client.Bsky.Actor.GetProfileAsync(AtIdentifier.Parse("bob.bsky.social"));
Console.WriteLine($"{profile.DisplayName}: {profile.FollowersCount} followers");

var timeline = await client.Bsky.Feed.GetTimelineAsync(limit: 30);
foreach (var item in timeline.Feed)
    Console.WriteLine($"{item.Post.Author.Handle}: {item.Post.Record.GetProperty("text").GetString()}");

// Later: only what is newer than the first page (also GetListFeedAsync)
var newer = await client.Bsky.Feed.GetTimelineAsync(since: timeline.StartCursor);

await foreach (var item in client.Bsky.Feed.EnumerateAuthorFeedAsync(profile.Did))
    Console.WriteLine(item.Post.Uri);
```

Embeds, feed reasons, thread nodes and the other unions of the Lexicons are open: a variant the SDK
does not model reads as an `Unknown…` type (`UnknownEmbedView`, …) instead of failing, so give
every `switch` over them a fallback arm:

```csharp continued
foreach (var item in timeline.Feed)
{
    var embed = item.Post.Embed switch
    {
        null => "no embed",
        ImagesView images => $"{images.Images.Count} images",
        ExternalView link => link.External.Uri,
        RecordEmbedView => "a quote",
        _ => "something newer than this SDK",
    };
    Console.WriteLine(embed);
}
```

### Post search

`Feed.SearchPostsV2Async` (`app.bsky.feed.searchPostsV2`) takes an optional query and a
`PostSearchFilters` of includes and excludes (authors, mentions, domains, URLs, embedded records,
hashtags, languages, media, replies, thread, date range, `Following`, `QueryLanguage`). A list
matches any of its entries; the filters combine. The response has `HitsTotal` and
`DetectedQueryLanguages`; walk every page with
[`Pagination.EnumerateAsync`](../CONTRIBUTING.md#lexicon-models-and-clients).

```csharp continued
var results = await client.Bsky.Feed.SearchPostsV2Async("atproto", new PostSearchFilters
{
    Authors = [Handle.Parse("alice.bsky.social")],
    Hashtags = ["dev"],
    HasMedia = true,
}, sort: PostSearchSort.Recent);
```

### Threads

`Unspecced.GetPostThreadV2Async(anchor)` returns a thread the way the Bluesky app shows it: a flat
list of `ThreadItem`s whose `Depth` places them (0 is the anchor, parents are negative). Each
`Value` is a `ThreadItemPost` (with `OpThread`, `MoreReplies`, `HiddenByThreadgate`, …) or a
placeholder: `ThreadItemBlocked`, `ThreadItemNotFound`, `ThreadItemNoUnauthenticated`. When
`HasOtherReplies` is set, `GetPostThreadOtherV2Async(anchor)` returns the rest.
`Feed.GetPostThreadAsync` is the older nested form.

## Bookmarks

Bluesky bookmarks are private: the appview stores them for their owner, outside the repository.
Bookmark a post by its URI and CID, and read them back newest first:

```csharp continued
var post = timeline.Feed[0].Post;
await client.Bsky.Bookmark.CreateBookmarkAsync(post.Uri, post.Cid);

await foreach (var bookmark in client.Bsky.Bookmark.EnumerateBookmarksAsync())
{
    var bookmarked = bookmark.Item switch
    {
        PostView view => view.Record.GetProperty("text").GetString(),
        NotFoundPost => "(deleted)",
        BlockedPost => "(blocked)",
        _ => "(unsupported)",
    };
    Console.WriteLine(bookmarked);
}

await client.Bsky.Bookmark.DeleteBookmarkAsync(post.Uri);
```

`PostView.Viewer.Bookmarked` and `PostView.BookmarkCount` show a post's bookmark state in feeds.

## Drafts

`Draft.CreateDraftAsync(draft)` stores a `Draft` of one or more `DraftPost`s and returns its `Tid`;
`UpdateDraftAsync(id, draft)`, `DeleteDraftAsync(id)` and `GetDraftsAsync` manage them. Draft media
are on-device paths (`DraftEmbedLocalRef`), so they only resolve on the device that made the draft.
`DraftErrors.DraftLimitReached` marks a full account.

## Notifications

- `Notification.ListNotificationsAsync` / `EnumerateNotificationsAsync` read them, filtered by
  `reasons`; `GetUnreadCountAsync` counts the unread ones and `UpdateSeenAsync` marks them read.
- `Notification.GetPreferencesAsync()` returns the `NotificationPreferences`, one per notification
  kind. `PutPreferencesV2Async(new PutPreferencesV2Request { Like = … })` changes the ones set and
  returns all of them.
- `PutActivitySubscriptionAsync(did, post, reply)` subscribes to an account's posts and replies
  (both `false` unsubscribes); `ListActivitySubscriptionsAsync` lists the accounts subscribed to.
  Who may subscribe to an account is its
  `NotificationDeclarationRecord` (`app.bsky.notification.declaration`, key `self`).
- `UnregisterPushAsync(serviceDid, token, platform, appId)` undoes `RegisterPushAsync`.

## Graph

- `Graph.GetFollowersAsync` / `GetFollowsAsync` and their enumerators take a `sort` before the
  paging arguments; `GetListsAsync` takes the list `purposes`.
- `Graph.GetListsWithMembershipAsync(actor)` and `GetStarterPacksWithMembershipAsync(actor)` list the
  viewer's lists and starter packs, each with the actor's `ListItem` when the actor is on it.
- `Graph.SearchStarterPacksV2Async(query)` returns full `StarterPackView`s and `HitsTotal`.

## Link cards and feed feedback

- `Embed.GetEmbedExternalViewAsync(url, uris)` resolves the records behind a page (such as a
  `site.standard.document` and its publication) into an `ExternalView` plus the `AssociatedRefs` to
  put into the post's `ExternalInfo.AssociatedRefs`. An empty response means: render an ordinary
  link card.
- `Feed.SendInteractionsAsync(interactions, feed, feedGenerator)` tells a feed generator how the
  viewer reacted to its items (`InteractionEvent.RequestLess`, `Seen`, `Like`, …), passing back each
  item's `FeedContext` and `ReqId`. With `feedGenerator` set, the call is proxied to that service.

## Age assurance

`AgeAssurance.GetConfigAsync()` returns each region's minimum age and ordered rules
(`AgeAssuranceRule`: `DefaultAgeRule`, `DeclaredOverAgeRule`, `AssuredUnderAgeRule`,
`AccountNewerThanRule`, …). `GetStateAsync(countryCode, regionCode)` returns the account's
`AgeAssuranceState` (`Status`, `Access`) and the metadata to compute it client-side, and
`BeginAsync(email, language, countryCode)` starts the process.

## Other records

Records without a client method of their own are written through `client.Repo` or
`RecordCollection<T>` (see [Custom Lexicon Records](custom-records.md)):

| Model | Collection | Key |
|-------|------------|-----|
| `StatusRecord` | `app.bsky.actor.status` (e.g. `ActorStatus.Live`) | `self` |
| `ContentVisibilityDeclarationRecord` | `app.bsky.actor.contentVisibilityDeclaration` | `self` |
| `NotificationDeclarationRecord` | `app.bsky.notification.declaration` | `self` |
| `VerificationRecord` | `app.bsky.graph.verification` | TID |
| `ReferenceListOptOutRecord` | `app.bsky.graph.referencelistoptout` | TID |

## Next Steps

- [Chat & Direct Messages](chat.md) — `chat.bsky`
- [Video Upload](video.md) — `app.bsky.video`
- [Labeler Services](labeler.md) — labels and labeler declarations
- [Custom Lexicon Records](custom-records.md) — unions, unknown fields, and your own records
