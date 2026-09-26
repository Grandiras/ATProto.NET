using ATProtoNet.Streaming;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Streaming;

/// <summary>The contract every <see cref="IRepoSyncStateStore"/> must satisfy.</summary>
public sealed class InMemoryRepoSyncStateStoreTests : RepoSyncStateStoreContractTests
{
    protected override IRepoSyncStateStore CreateStore() => new InMemoryRepoSyncStateStore();
}
