using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class FirehoseCursorStoreTests
{
    [Fact]
    public async Task InMemory_GetCursor_ReturnsNull_WhenNotStored()
    {
        var store = new InMemoryFirehoseCursorStore();
        var result = await store.GetCursorAsync("stream1");
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_StoreThenGet_ReturnsCursor()
    {
        var store = new InMemoryFirehoseCursorStore();
        await store.StoreCursorAsync("stream1", 42);
        var result = await store.GetCursorAsync("stream1");
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task InMemory_StoreOverwrites_PreviousValue()
    {
        var store = new InMemoryFirehoseCursorStore();
        await store.StoreCursorAsync("stream1", 10);
        await store.StoreCursorAsync("stream1", 20);
        var result = await store.GetCursorAsync("stream1");
        Assert.Equal(20, result);
    }

    [Fact]
    public async Task InMemory_DifferentStreamIds_AreIndependent()
    {
        var store = new InMemoryFirehoseCursorStore();
        await store.StoreCursorAsync("stream1", 100);
        await store.StoreCursorAsync("stream2", 200);

        Assert.Equal(100, await store.GetCursorAsync("stream1"));
        Assert.Equal(200, await store.GetCursorAsync("stream2"));
    }
}
