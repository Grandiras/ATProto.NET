using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

// The one request path every identity fetch takes: a per-fetch budget, a capped body, and failures
// reported as DidResolutionException.
internal static class IdentityFetch
{
    // The Accept header for a DID document.
    internal const string DidDocumentMediaTypes = "application/did+ld+json, application/json";

    // The most identity fetches in flight at once, process-wide. Resolution is driven by identifiers
    // other parties choose, so without a ceiling a burst of distinct DIDs or handles becomes a burst of
    // outbound connections; past it, a fetch waits within its own budget.
    internal const int MaxConcurrentFetches = 64;

    private static readonly SemaphoreSlim FetchSlots = new(MaxConcurrentFetches, MaxConcurrentFetches);

    // The outcome of a fetch that got an answer.
    //
    // Status: The final status.
    //
    // Body: The body of a success response; empty otherwise.
    //
    // FinalUri: The URL that answered, after any redirects the client followed.
    //
    // Location: The Location of a redirect response, resolved against the request URL.
    internal readonly record struct Result(HttpStatusCode Status, ReadOnlyMemory<byte> Body, Uri? FinalUri, Uri? Location)
    {
        public bool IsSuccess => (int)Status is >= 200 and < 300;

        public bool IsRedirect => (int)Status is 301 or 302 or 303 or 307 or 308;
    }

    // GETs url, reading a success body up to maxBytes.
    //
    // Throws DidResolutionException: The policy refused the connection, the host was unreachable or
    // broke off, the budget ran out (waiting for a fetch slot included), or the body was over the cap or
    // would not decode.
    //
    // Throws OperationCanceledException: The caller cancelled.
    internal static async Task<Result> GetAsync(
        HttpClient client,
        Uri url,
        string accept,
        int maxBytes,
        TimeSpan timeout,
        Did? did,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);

        var slot = false;
        try
        {
            await FetchSlots.WaitAsync(budget.Token).ConfigureAwait(false);
            slot = true;

            // HTTP/2 where the host offers it, so concurrent lookups against one host (the PLC directory,
            // the DNS-over-HTTPS endpoint) share a connection instead of each paying for a handshake.
            using var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            request.Headers.TryAddWithoutValidation("Accept", accept);

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                .ConfigureAwait(false);

            var finalUri = response.RequestMessage?.RequestUri;
            if (!response.IsSuccessStatusCode)
            {
                var location = response.Headers.Location is { } header ? new Uri(finalUri ?? url, header) : null;
                return new Result(response.StatusCode, ReadOnlyMemory<byte>.Empty, finalUri, location);
            }

            ReadOnlyMemory<byte>? body;
            try
            {
                body = await response.Content.ReadBoundedAsync(maxBytes, budget.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                // A body that does not decode under its Content-Encoding: gzip and deflate throw
                // InvalidDataException, Brotli InvalidOperationException.
                throw new DidResolutionException(
                    $"The response from {url.Host} could not be decoded: {ex.Message}",
                    DidResolutionErrorKind.InvalidDocument, did, ex);
            }

            if (body is null)
            {
                throw new DidResolutionException(
                    $"The response from {url.Host} is larger than the {maxBytes} bytes accepted.",
                    DidResolutionErrorKind.ResponseTooLarge, did);
            }

            return new Result(response.StatusCode, body.Value, finalUri, null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DidResolutionException(
                $"{url.Host} did not answer within {timeout.TotalSeconds:0.#} s.",
                DidResolutionErrorKind.Timeout, did, ex);
        }
        catch (HttpRequestException ex) when (IdentityNetworkPolicy.IsBlocked(ex))
        {
            throw new DidResolutionException(
                $"The identity fetch policy refused {url.Host}: {ex.InnerException?.Message ?? ex.Message}",
                DidResolutionErrorKind.Blocked, did, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException covers a body the host broke off mid-read (HttpIOException among them).
            throw new DidResolutionException(
                $"Could not read from {url.Host}: {ex.Message}", DidResolutionErrorKind.NetworkError, did, ex);
        }
        catch (UriFormatException ex)
        {
            throw new DidResolutionException(
                $"{url.Host} answered with an unusable redirect: {ex.Message}", DidResolutionErrorKind.HttpError, did, ex);
        }
        finally
        {
            if (slot)
                FetchSlots.Release();
        }
    }

    // Fetches and validates a DID document: 404 and 410 are reported as such, and the document must
    // parse and name did as its id.
    //
    // Throws DidResolutionException: Resolution failed.
    internal static async Task<DidDocument> GetDidDocumentAsync(
        HttpClient client, Uri url, Did did, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await GetAsync(client, url, DidDocumentMediaTypes, maxBytes, timeout, did, cancellationToken)
            .ConfigureAwait(false);

        // A DID document is served where the identifier says, or not at all. The SDK's handler
        // follows no redirects; a caller's client that did is refused here all the same.
        if (result.FinalUri is { } final && final != url)
        {
            throw new DidResolutionException(
                $"Resolving {did} was redirected to {final.Host}.", DidResolutionErrorKind.HttpError, did);
        }

        switch (result.Status)
        {
            case HttpStatusCode.NotFound:
                throw new DidResolutionException($"No DID document for {did}.", DidResolutionErrorKind.NotFound, did);
            case HttpStatusCode.Gone:
                throw new DidResolutionException($"{did} has been deactivated.", DidResolutionErrorKind.Deactivated, did);
        }

        if (!result.IsSuccess)
        {
            throw new DidResolutionException(
                $"Resolving {did} answered HTTP {(int)result.Status}.", DidResolutionErrorKind.HttpError, did);
        }

        DidDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DidDocument>(result.Body.Span, AtProtoJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new DidResolutionException(
                $"The DID document for {did} is malformed: {ex.Message}", DidResolutionErrorKind.InvalidDocument, did, ex);
        }

        if (document is null)
            throw new DidResolutionException($"The DID document for {did} is empty.", DidResolutionErrorKind.InvalidDocument, did);

        // A directory or host answering with a document for another DID could otherwise swap in
        // someone else's signing key or PDS.
        if (!IdsMatch(document.Id, did))
        {
            throw new DidResolutionException(
                $"The DID document for {did} names '{document.Id}' as its id.", DidResolutionErrorKind.InvalidDocument, did);
        }

        return document;
    }

    // Whether a document's id is exactly the DID asked for, as @atproto/identity compares it.
    //
    // A did:web embeds a DNS host, which DNS matches case-insensitively, so every casing of one fetches
    // the same document. Accepting the document for all of them would give one identity as many
    // spellings, and a signer could pick whichever it liked as its iss, a different principal name each
    // time. Held to its own id, a document answers for one.
    internal static bool IdsMatch(Did documentId, Did requested) =>
        string.Equals(documentId.Value, requested.Value, StringComparison.Ordinal);
}
