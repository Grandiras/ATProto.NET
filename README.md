# ATProto.NET

[![CI](https://git.grandiras.net/Grandiras/ATProto.NET/actions/workflows/ci.yml/badge.svg)](https://git.grandiras.net/Grandiras/ATProto.NET/actions)

A comprehensive .NET 10 SDK for the [AT Protocol](https://atproto.com). Build custom AT Protocol applications with your own Lexicon schemas, interact with Bluesky, or run your own managed PDS — all from clean, modern .NET APIs.

**Source:** [Forgejo (canonical)](https://git.grandiras.net/Grandiras/ATProto.NET) · [GitHub (mirror — issues & PRs welcome here)](https://github.com/Grandiras/ATProto.NET)

> ⚠️ This repository was mainly built by a coding agent. Thorough testing has been conducted, and the maintainer ([Grandiras](https://github.com/Grandiras)) is a human.

## Why ATProto.NET?

The AT Protocol isn't just Bluesky — it's an open protocol where **one account works across many apps**. Each app defines its own [Lexicon schemas](https://atproto.com/guides/lexicon) and stores records in the user's Personal Data Server (PDS). ATProto.NET makes it easy to build these custom applications in .NET.

The SDK is in `0.*` because, while it's near feature-complete, it's mostly vibe-coded and won't be called "stable" until it has been put through real use. There's no fixed timeline for `1.0` — the more this project gets exercised, the sooner it happens.

## At a glance

- **Custom Lexicon support** — `RecordCollection<T>` for typed CRUD on your own record schemas, plus `QueryAsync<T>` / `ProcedureAsync<T>` for custom XRPC methods.
- **OAuth & identity** — full AT Protocol OAuth (DPoP, PAR, PKCE), `did:plc` / `did:web` resolution, type-safe `Did` / `Handle` / `AtUri` / `Nsid` / `Tid` / `RecordKey` / `Cid`.
- **Bluesky, Chat, Ozone** — `app.bsky.*` (actors, feeds, graph, notifications, rich text, video), `chat.bsky.*` (conversations, DMs), `tools.ozone.*` (moderation).
- **Hosting** — ASP.NET Core DI + JWT auth, Blazor components with cookie-based OAuth, .NET Aspire integration, and a **managed PDS** — run the official Bluesky PDS container via `ATProtoNet.Aspire.Hosting` and administer it with `PdsAdminClient`.
- **Repository internals** — typed firehose consumer with CID/signature verification, MST, CAR v1 parsing, DAG-CBOR, PLC directory client, P-256/K-256 crypto.
- **Lexicon tooling** — `atproto-lexgen` `dotnet tool` for bidirectional Lexicon JSON ↔ C#, schema diffing, and publishing.

## Install

```bash
dotnet add package ATProtoNet
```

Additional packages (`ATProtoNet.Server` — ASP.NET Core integration including the EF Core token store and Aspire client integration, `ATProtoNet.Blazor`, `ATProtoNet.Aspire.Hosting`) and the `atproto-lexgen` `dotnet tool` are documented in the [full package matrix](docs/getting-started.md#install-the-package).

## A 30-second taste

The headline feature is `RecordCollection<T>` — strongly-typed CRUD on your own Lexicon schemas, stored in the user's PDS:

```csharp
using ATProtoNet;
using System.Text.Json.Serialization;

// 1. Define your record type
public class TodoItem : AtProtoRecord
{
    [JsonPropertyName("$type")]
    public override string Type => "com.example.todo.item";

    [JsonPropertyName("title")]     public string Title { get; set; } = "";
    [JsonPropertyName("completed")] public bool Completed { get; set; }
}

// 2. Connect and authenticate
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://bsky.social")
    .Build();
await client.LoginAsync("alice.bsky.social", "app-password");

// 3. Get a typed collection and do CRUD
var todos = client.GetCollection<TodoItem>("com.example.todo.item");

var created = await todos.CreateAsync(new TodoItem { Title = "Buy groceries" });

await foreach (var record in todos.EnumerateAsync())
    Console.WriteLine($"[{(record.Value.Completed ? "x" : " ")}] {record.Value.Title}");
```

One AT Protocol account can power many such apps — todos, bookmarks, recipes, whatever — each with its own Lexicons, all stored in the same user's PDS.

## Where to go next

| If you want to… | Read |
|---|---|
| Install, configure the client, and authenticate | [Getting Started](docs/getting-started.md) |
| Build your own AT Protocol app with custom records | [Custom Lexicon Records](docs/custom-records.md) |
| Use OAuth (recommended for user-facing apps) | [OAuth Authentication](docs/oauth.md) |
| Wire AT Proto into an ASP.NET / Blazor backend | [ASP.NET Core](docs/aspnet-core.md), [Blazor](docs/blazor.md) |
| Consume the firehose with typed events | [Firehose Streaming](docs/firehose.md) |
| Run your own PDS | [Managed PDS](docs/managed-pds.md) |
| Understand how the packages compose | [Architecture](docs/architecture.md) |
| Everything else | [docs/index.md](docs/index.md) |

## Contributing

Issues and pull requests are welcome on [GitHub](https://github.com/Grandiras/ATProto.NET) (mirrored from Forgejo). See [CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, test workflow, and conventions.

## License

[MIT](LICENSE).
