using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>The contract every <see cref="ISpaceAuthorityStore"/> must satisfy, held in memory.</summary>
public class InMemorySpaceAuthorityStoreTests : SpaceAuthorityStoreContractTests
{
    protected override ISpaceAuthorityStore CreateStore() => new InMemorySpaceAuthorityStore();

    protected override Task DeclareSpaceAsync(ISpaceAuthorityStore store, SpaceUri space)
    {
        ((InMemorySpaceAuthorityStore)store).DeclareSpace(space);
        return Task.CompletedTask;
    }

    protected override Task MarkDeletedAsync(ISpaceAuthorityStore store, SpaceUri space)
    {
        ((InMemorySpaceAuthorityStore)store).MarkDeleted(space);
        return Task.CompletedTask;
    }
}
