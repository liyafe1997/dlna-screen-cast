using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Streaming.Buffering;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Buffering;

public sealed class LiveStreamBufferTests
{
    [Fact]
    public async Task SubscriptionStartsAtLatestRandomAccessPointThenReceivesLiveChunks()
    {
        using LiveStreamBuffer buffer = new(1880, TimeSpan.FromSeconds(5), 4);
        byte[] first = Packet(1);
        buffer.Append(new(first, TimeSpan.Zero, StartsAtRandomAccessPoint: true));
        first[0] = 99;
        buffer.Append(new(Packet(2), TimeSpan.FromSeconds(1), StartsAtRandomAccessPoint: false));
        buffer.Append(new(Packet(3), TimeSpan.FromSeconds(2), StartsAtRandomAccessPoint: true));
        buffer.Append(new(Packet(4), TimeSpan.FromSeconds(3), StartsAtRandomAccessPoint: false));
        await using LiveStreamSubscription subscription = buffer.Subscribe();
        await using IAsyncEnumerator<MediaStreamChunk> reader = subscription.ReadAllAsync().GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(3, reader.Current.Data.Span[0]);
        Assert.True(reader.Current.StartsAtRandomAccessPoint);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(4, reader.Current.Data.Span[0]);

        buffer.Append(new(Packet(5), TimeSpan.FromSeconds(4), StartsAtRandomAccessPoint: false));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(5, reader.Current.Data.Span[0]);
    }

    [Fact]
    public void BufferEvictsByByteAndTimeLimitsWithoutGrowingUnbounded()
    {
        using LiveStreamBuffer buffer = new(188 * 3, TimeSpan.FromSeconds(2), 2);
        for (int index = 0; index < 8; index++)
        {
            buffer.Append(new(
                Packet((byte)index),
                TimeSpan.FromSeconds(index),
                StartsAtRandomAccessPoint: index % 2 == 0));
        }

        LiveStreamBufferStatistics statistics = buffer.Statistics;
        Assert.InRange(statistics.BufferedBytes, 188, 188 * 3);
        Assert.InRange(statistics.BufferedChunks, 1, 3);
        Assert.True(statistics.EvictedChunks >= 5);
    }

    [Fact]
    public void SlowSubscriberIsDisconnectedWhenItsBoundedQueueOverflows()
    {
        using LiveStreamBuffer buffer = new(1880, TimeSpan.FromSeconds(5), 1);
        buffer.Append(new(Packet(1), TimeSpan.Zero, StartsAtRandomAccessPoint: true));
        _ = buffer.Subscribe();

        buffer.Append(new(Packet(2), TimeSpan.FromMilliseconds(1), StartsAtRandomAccessPoint: false));
        buffer.Append(new(Packet(3), TimeSpan.FromMilliseconds(2), StartsAtRandomAccessPoint: false));

        Assert.Equal(0, buffer.Statistics.ActiveSubscribers);
        Assert.Equal(1, buffer.Statistics.DisconnectedSlowSubscribers);
    }

    [Fact]
    public void RejectsNonMonotonicTimestampsAndSubscriptionBeforeFirstKeyframe()
    {
        using LiveStreamBuffer buffer = new(1880, TimeSpan.FromSeconds(5), 2);
        buffer.Append(new(Packet(1), TimeSpan.FromSeconds(1), StartsAtRandomAccessPoint: false));
        Assert.Throws<InvalidOperationException>(() => buffer.Subscribe());
        Assert.Throws<InvalidDataException>(() => buffer.Append(new(
            Packet(2),
            TimeSpan.Zero,
            StartsAtRandomAccessPoint: true)));
    }

    [Fact]
    public async Task LiveEdgeSubscriptionSkipsBacklogAndStartsAtNextKeyframe()
    {
        using LiveStreamBuffer buffer = new(1880, TimeSpan.FromSeconds(5), 4);
        buffer.Append(new(Packet(1), TimeSpan.Zero, StartsAtRandomAccessPoint: true));
        buffer.Append(new(Packet(2), TimeSpan.FromMilliseconds(33), StartsAtRandomAccessPoint: false));
        await using LiveStreamSubscription subscription = buffer.Subscribe(startAtLiveEdge: true);
        await using IAsyncEnumerator<MediaStreamChunk> reader = subscription.ReadAllAsync().GetAsyncEnumerator();

        buffer.Append(new(Packet(3), TimeSpan.FromMilliseconds(66), StartsAtRandomAccessPoint: false));
        buffer.Append(new(Packet(4), TimeSpan.FromSeconds(1), StartsAtRandomAccessPoint: true));
        buffer.Append(new(Packet(5), TimeSpan.FromMilliseconds(1033), StartsAtRandomAccessPoint: false));

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(4, reader.Current.Data.Span[0]);
        Assert.True(reader.Current.StartsAtRandomAccessPoint);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(5, reader.Current.Data.Span[0]);
    }

    [Fact]
    public async Task LiveEdgeSubscriptionIsAcceptedBeforeTheFirstKeyframeIsBuffered()
    {
        using LiveStreamBuffer buffer = new(1880, TimeSpan.FromSeconds(5), 4);
        buffer.Append(new(Packet(1), TimeSpan.Zero, StartsAtRandomAccessPoint: false));

        await using (LiveStreamSubscription subscription = buffer.Subscribe(startAtLiveEdge: true))
        {
            Assert.Equal(1, buffer.Statistics.ActiveSubscribers);
        }

        Assert.Equal(0, buffer.Statistics.ActiveSubscribers);
    }

    private static byte[] Packet(byte marker)
    {
        byte[] packet = new byte[188];
        packet[0] = marker;
        return packet;
    }
}
