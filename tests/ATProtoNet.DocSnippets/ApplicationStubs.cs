// The types every application a sample lives in already has, and the samples refer to by the
// names the project templates give them.

using Microsoft.AspNetCore.Components;

/// <summary>The application's entry point class, as in a <c>Program.cs</c> with top-level statements.</summary>
public partial class Program;

/// <summary>A Blazor Web App's root component.</summary>
public sealed class App : ComponentBase;

namespace Projects
{
    /// <summary>The web project an Aspire AppHost references, as the AppHost SDK generates it.</summary>
    public sealed class Web : Aspire.Hosting.IProjectMetadata
    {
        /// <inheritdoc />
        public string ProjectPath => "../Web/Web.csproj";
    }

    /// <summary>The API project an Aspire AppHost references, as the AppHost SDK generates it.</summary>
    public sealed class MyApi : Aspire.Hosting.IProjectMetadata
    {
        /// <inheritdoc />
        public string ProjectPath => "../MyApi/MyApi.csproj";
    }
}

namespace DocSnippets
{
    using ATProtoNet.Lexicon.Com.AtProto.Space;
    using ATProtoNet.Server.Spaces;
    using ATProtoNet.Spaces;

    /// <summary>
    /// The repo host an application implements over its own store, which the Spaces guide
    /// registers by this name. The guide shows it in part; its registration compiles against this.
    /// </summary>
    public sealed class MyRepoHost : ISpaceRepoHost
    {
        /// <inheritdoc />
        public Task<bool> HostsAccountAsync(Did did, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<GetSpaceRecordResponse?> GetRecordAsync(
            SpaceUri space, Did repoDid, Nsid collection, RecordKey rkey, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ListSpaceRecordsResponse> ListRecordsAsync(
            SpaceUri space, Did repoDid, Nsid? collection, bool reverse, bool excludeValues, int limit,
            string? cursor, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<SignedSpaceCommit?> GetLatestCommitAsync(
            SpaceUri space, Did repoDid, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<Stream?> GetRepoAsync(
            SpaceUri space, Did repoDid, bool excludeValues, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ListSpaceRepoOpsResponse?> ListRepoOpsAsync(
            SpaceUri space, Did repoDid, Tid? since, bool excludeValues, int limit, string? cursor,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ListSpaceBlobsResponse> ListBlobsAsync(
            SpaceUri space, Did repoDid, Tid? since, int limit, string? cursor,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <inheritdoc />
        public Task<SpaceBlobContent?> GetBlobAsync(
            SpaceUri space, Did repoDid, Cid cid, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
