using System.Net;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>Tests for <see cref="BoundedContent.ReadBoundedAsync"/>.</summary>
public class BoundedContentTests
{
    private static byte[] Body(int length) => Enumerable.Range(0, length).Select(i => (byte)i).ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ReadBoundedAsync_DeclaredBodyAtOrUnderTheCeiling_IsReadWhole(int length)
    {
        var body = await new ByteArrayContent(Body(length)).ReadBoundedAsync(100, CancellationToken.None);

        Assert.Equal(Body(length), body!.Value.ToArray());
    }

    [Fact]
    public async Task ReadBoundedAsync_DeclaredBodyOverTheCeiling_IsRefusedWithoutReading()
    {
        var content = new CountingContent(Body(101), declareLength: true);

        Assert.Null(await content.ReadBoundedAsync(100, CancellationToken.None));
        Assert.False(content.Read);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task ReadBoundedAsync_UndeclaredBody_IsReadUpToExactlyTheCeiling(int length, bool accepted)
    {
        var body = await new CountingContent(Body(length), declareLength: false).ReadBoundedAsync(100, CancellationToken.None);

        Assert.Equal(accepted, body is not null);
    }

    [Fact]
    public async Task ReadBoundedAsync_UndeclaredBodyLargerThanTheFirstBuffer_GrowsToFitIt()
    {
        // With no declared length the read starts small, so a body of a few pages has to grow
        // the buffer, and must still come back intact.
        var expected = Body(50_000);

        var body = await new CountingContent(expected, declareLength: false).ReadBoundedAsync(256 * 1024, CancellationToken.None);

        Assert.Equal(expected, body!.Value.ToArray());
    }

    [Fact]
    public async Task ReadBoundedAsync_DeclaredLengthThatUnderstatesTheBody_IsStillCapped()
    {
        var content = new CountingContent(Body(10_000), declareLength: false);
        content.Headers.ContentLength = 10;

        Assert.Null(await content.ReadBoundedAsync(100, CancellationToken.None));
    }

    /// <summary>A body served from a stream, optionally without a declared length.</summary>
    private sealed class CountingContent(byte[] body, bool declareLength) : HttpContent
    {
        public bool Read { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            Read = true;
            return Task.FromResult<Stream>(new MemoryStream(body));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return declareLength;
        }
    }
}
