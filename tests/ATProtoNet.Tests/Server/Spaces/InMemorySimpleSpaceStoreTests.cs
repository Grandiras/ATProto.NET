using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>The contract every <see cref="ISimpleSpaceStore"/> must satisfy, held in memory.</summary>
public sealed class InMemorySimpleSpaceStoreTests : SimpleSpaceStoreContractTests
{
    protected override ISimpleSpaceStore CreateStore() => new InMemorySimpleSpaceStore();
}
