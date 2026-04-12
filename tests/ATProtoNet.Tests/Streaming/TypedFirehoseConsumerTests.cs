using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class TypedFirehoseConsumerOptionsTests
{
    [Fact]
    public void ResolvedStreamId_UsesStreamId_WhenProvided()
    {
        var options = new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://bsky.network",
            StreamId = "my-custom-id",
        };

        Assert.Equal("my-custom-id", options.ResolvedStreamId);
    }

    [Fact]
    public void ResolvedStreamId_FallsBackToServiceUrl()
    {
        var options = new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://bsky.network",
        };

        Assert.Equal("wss://bsky.network", options.ResolvedStreamId);
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://bsky.network",
        };

        Assert.False(options.VerifyCids);
        Assert.False(options.VerifySignatures);
        Assert.Null(options.CollectionFilter);
        Assert.Null(options.CursorStore);
        Assert.Null(options.Verifier);
        Assert.Equal(100, options.CursorPersistInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReconnectDelay);
        Assert.Equal(10, options.MaxReconnectAttempts);
    }

    [Fact]
    public void TypedFirehoseConsumer_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TypedFirehoseConsumer(null!));
    }

    [Fact]
    public void TypedFirehoseConsumer_Dispose_DoesNotThrow()
    {
        var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://bsky.network",
        });

        consumer.Dispose();
        consumer.Dispose(); // Should not throw
    }
}
