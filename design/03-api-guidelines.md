# 03 — API guidelines

These rules apply to every public API in the rewrite, generated or hand-written. They follow the
[.NET Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/)
unless a rule here says otherwise. Reviewers cite them by number (`AG-n`).

## Namespaces and naming

**AG-1 — One root namespace per package, matching the package ID.** A package may contribute
sub-namespaces under its own root. Exception: the vocabulary types every user needs
(`Did`, `Handle`, `AtIdentifier`, `Nsid`, `AtUri`, `RecordKey`, `Tid`, `AtDatetime`, `Cid`,
`AtProtoClient`) live in the root `ATProtoNet` namespace, so one `using ATProtoNet;` is enough
for the common case. See [02 — Architecture](02-architecture.md#namespaces).

**AG-2 — Generated Lexicon code uses namespaces derived from the NSID.** The namespace is
`{root}.{Authority segments in PascalCase}`, so `app.bsky.feed.post` goes to
`ATProtoNet.Lexicons.App.Bsky.Feed`. Users choose `{root}` for their own Lexicons. The full
mapping is in [specs/lexicon.md](specs/lexicon.md#naming).

**AG-3 — Protocol words keep protocol spelling.** Use `Did`, `Nsid`, `Cid`, `Tid` (acronyms of
three or more letters are PascalCased, per the guidelines), `AtUri`, `Xrpc`, `Dpop`, `Pds`, `Plc`.
Use `AtProto` as one word in type names (`AtProtoClient`, `AtProtoException`).

**AG-4 — No suffixes that say nothing.** No `Manager`, `Helper`, `Util`, `Service` (except for
real ASP.NET Core services), `Info`, `Data` (except the data model), `Impl`. A type is named after
what it *is* (`PasswordSession`, `DidDocument`, `CursorStore`), not what it *does in general*.

**AG-5 — Async methods end in `Async`.** Methods returning `IAsyncEnumerable<T>` also end in
`Async` (`EnumerateRecordsAsync`), per current .NET convention.

## Types

**AG-6 — Sealed by default.** Classes are `sealed` unless they are designed for inheritance, and a
class designed for inheritance says so in its docs. Abstract base classes are reserved for closed
hierarchies we own (unions, exceptions, sessions).

**AG-7 — Immutable values.** Models and options are immutable after construction: `init`
accessors, `required` for mandatory members, `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>` for
collections. Use `record` (class) for models with value semantics. Mutation is through `with`.

**AG-8 — Identifier types validate on construction and cannot be invalid.** They are sealed
classes, not `record`s (so `null` means "absent", there is no invalid `default`, and `with` does
not apply), implement `ISpanParsable<TSelf>` and `IUtf8SpanParsable<TSelf>` and a public
`IAtIdentifier<TSelf>` constraint interface, compare by canonical value, and expose `Parse`,
`TryParse`, `ToString()` (canonical form). `Parse` throws `FormatException` and nothing else.
There are **no explicit or implicit conversions from `string`**: a conversion that can throw must
be visible (`Did.Parse`). An implicit conversion *to* `string` is allowed. A `readonly struct` is
allowed only where `default` is a valid value (`Tid`, whose zero value is a valid TID). Details:
[specs/syntax.md](specs/syntax.md).

**AG-9 — Open sets are open.** A Lexicon `knownValues` string is a `readonly record struct` over
`string` with static well-known values, never a C# `enum`. An unknown value round-trips. Unions
are interfaces ([D-18](06-decisions.md#d-18-unions-are-interfaces)); open unions have an
`Unknown…` variant that round-trips the raw data, closed unions do not.

**AG-9a — One representation per concept.** A CID is a `Cid` everywhere: never `byte[]` in one
API and a base32 `string` in another. A DID key is a `DidKey`, a service endpoint a `Uri`, a
proxy target an `XrpcServiceRef`.

**AG-10 — No public mutable statics.** No static caches, registries, default instances that
callers can mutate, or ambient configuration. Static readonly singletons of immutable objects
(`JsonSerializerOptions` built once and frozen, well-known values) are fine.

## Methods and parameters

**AG-11 — Parameter order.** (1) the subject (repo, actor, DID, URI); (2) other required inputs;
(3) an options/parameters record; (4) `CancellationToken cancellationToken = default`, always
last, on every method that does I/O or may wait.

**AG-12 — Optional data goes in a record, not in optional parameters.** A method with more than
two optional inputs takes a `…Options` or `…Parameters` record. This keeps signatures stable when
a Lexicon gains an optional field. Adding an `init` property is not a breaking change; adding an
optional parameter is a binary break.

**AG-13 — Generated XRPC methods take one parameters or input record.** A query takes
`{Method}.Parameters` (with `required` members for required Lexicon params); a procedure takes
its input record (or `Stream`/`ReadOnlyMemory<byte>` plus a content type for binary input). The
return type is the output record, the referenced output type, `XrpcBinaryResponse` for
binary output, or `Task` for no output. See [specs/lexicon.md](specs/lexicon.md#generated-clients).

**AG-14 — Accept broad, return narrow.** Parameters take `IEnumerable<T>`
(`IReadOnlyCollection<T>` when the count is needed), `ReadOnlyMemory<byte>`/`Stream` for bytes.
Return `IReadOnlyList<T>`, concrete records, `IAsyncEnumerable<T>` for sequences that page or
stream.

**AG-15 — `Task` for public async APIs, `ValueTask` for hot internal paths** and for interfaces
whose implementations commonly complete synchronously (stores, caches).

**AG-16 — Library code uses `ConfigureAwait(false)`** on every await, enforced by analyzer
CA2007 in `src/`.

## Pagination and streams

**AG-17 — Paginated outputs implement `ICursorPage<T>`.** One page per call from the generated
method, and a generated `Enumerate{Method}Async` that walks all pages through the single shared
pagination helper. It stops on a null, empty or repeated cursor. No other cursor loop exists.

**AG-18 — Every event stream is consumed as `IAsyncEnumerable<TEvent>`.** Not .NET events, not
callbacks, not `Channel<T>` in the public API. Reconnect, backoff and cursor persistence are
handled inside the enumerator. The cursor of event *n* is committed when the consumer pulls
event *n+1* (at-least-once delivery). Hosted-service adapters (`StreamWorker<TEvent>`) live in
`ATProtoNet.Hosting`, so worker services need no ASP.NET Core.
See [specs/streaming.md](specs/streaming.md).

## Errors

**AG-19 — Exceptions, one hierarchy.** All exceptions the SDK throws for protocol or remote
failures derive from `AtProtoException`. XRPC failures are `XrpcException` (with `StatusCode`,
`Error`, `Message`, rate-limit info) and a small set of subtypes for errors callers branch on.
Input validation uses the BCL types (`ArgumentException`, `FormatException` for `Parse`).
Cancellation is always `OperationCanceledException`, never wrapped.

**AG-20 — `Try` patterns only for parsing and cache-style lookups**, never for remote calls.

**AG-21 — Never return `null` to mean "failed".** `null` means "absent" (a record that does not
exist where the Lexicon allows that, an optional field).

## Options, configuration and dependency injection

**AG-22 — Options are values, not switches.** An options type carries URLs, limits, timeouts and
names. A choice between code paths is a choice between components (pass a different
`IDidResolver`), not a `bool`.

**AG-23 — Options are validated when the component is constructed,** and through
`IValidateOptions<T>` when bound from configuration, so bad configuration fails at startup.

**AG-24 — DI-friendly, DI-optional.** Every component has a public constructor taking its
dependencies (with sensible defaults), so it works with `new`. DI registration is a thin layer in
the hosting package.

**AG-25 — Registration verbs.** One builder per hosting package entry point
(`services.AddAtProto()` returns `AtProtoBuilder`). On the builder, `Add*` adds a feature and
`Use*` selects the implementation of a seam, always with replace semantics. On
`IEndpointRouteBuilder`, `MapAtProto*` maps endpoints. Authentication schemes follow ASP.NET
Core's convention and register on `AuthenticationBuilder` (`AddAtProtoServiceAuth`). No `With*`
verbs. Every extension method in a package uses the same receiver for the same kind of thing.

## Resources and lifetimes

**AG-26 — Whoever creates it disposes it.** A component that receives an `HttpClient`, store,
resolver or **key** never disposes it. A component that creates one owns it and implements
`IDisposable` or `IAsyncDisposable`. (0.8 is inconsistent: `ServiceAuthGenerator` disposes the
caller's key, `OAuthClient` does not.)

**AG-27 — No hidden background work.** A component never starts a timer, task or connection
the caller did not ask for. Background refresh, stream consumption and cache cleanup start from
explicit calls or hosted services.

**AG-28 — `TimeProvider` everywhere time matters.** Expiry checks, backoff, caching TTLs and
JWT timestamps read the clock from an injected `TimeProvider`, defaulting to
`TimeProvider.System`. Do not call `DateTime.UtcNow`.

## Security

**AG-29 — Secrets never reach logs, exceptions or `ToString()`.** Tokens, passwords, private
keys and DPoP proofs are redacted. Session and key types override `ToString()`.

**AG-30 — Untrusted input is bounded before it is parsed** (size, depth, element count, time)
with limits that are named constants in our code, documented in the relevant spec.

## Logging and diagnostics

**AG-31 — Source-generated logging** (`[LoggerMessage]`) with stable event IDs. Each package owns
an event-ID range listed in [02 — Architecture](02-architecture.md#diagnostics). Logs at
`Information` and above are rare and actionable; per-request detail is `Debug`/`Trace`.

**AG-32 — `ActivitySource` and `Meter` per package** (`ATProtoNet.Xrpc`, `ATProtoNet.Streaming`,
…) for distributed tracing and metrics. Instrument names follow OpenTelemetry semantic
conventions where they exist.

## Stability and documentation

**AG-33 — Experimental surface is marked.** APIs tracking an alpha protocol or an unspecced
upstream endpoint carry `[Experimental("ATPROTO0xx")]` with a diagnostic ID listed in
[06 — Decisions](06-decisions.md#experimental-diagnostic-ids). Callers opt in explicitly.

**AG-34 — Public API baselines.** Every packable project tracks its surface with
`Microsoft.CodeAnalysis.PublicApiAnalyzers`. A change to the surface shows up in review as a diff
of `PublicAPI.Unshipped.txt`.

**AG-35 — XML docs on every public member.** The `<summary>` is one sentence. `<remarks>` cites
the spec section (`<see href="https://atproto.com/specs/…"/>`) for protocol behaviour. Generated
code carries the Lexicon `description`.

**AG-36 — Samples compile.** Every C# block in `README.md` and `docs/` is compiled by the
doc-snippet project, as today.
