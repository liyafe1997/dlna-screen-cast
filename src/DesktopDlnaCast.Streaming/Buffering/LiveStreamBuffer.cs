using System.Threading.Channels;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Streaming.Buffering;

public sealed class LiveStreamBuffer : IDisposable
{
    private readonly object gate = new();
    private readonly long maximumBytes;
    private readonly TimeSpan maximumDuration;
    private readonly int subscriberCapacity;
    private readonly LinkedList<MediaStreamChunk> chunks = new();
    private readonly Dictionary<long, Subscriber> subscribers = [];
    private long bufferedBytes;
    private long evictedChunks;
    private long disconnectedSlowSubscribers;
    private long nextSubscriberId;
    private TimeSpan? latestTimestamp;
    private Exception? completionError;
    private bool completed;
    private bool disposed;

    public LiveStreamBuffer(long maximumBytes, TimeSpan maximumDuration, int subscriberCapacity)
    {
        if (maximumBytes is < 188 or > 256L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (maximumDuration <= TimeSpan.Zero || maximumDuration > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        if (subscriberCapacity is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriberCapacity));
        }

        this.maximumBytes = maximumBytes;
        this.maximumDuration = maximumDuration;
        this.subscriberCapacity = subscriberCapacity;
    }

    public LiveStreamBufferStatistics Statistics
    {
        get
        {
            lock (gate)
            {
                return new(
                    chunks.Count,
                    bufferedBytes,
                    evictedChunks,
                    subscribers.Count,
                    disconnectedSlowSubscribers);
            }
        }
    }

    public void Append(MediaStreamChunk chunk)
    {
        if (chunk.Data.IsEmpty || chunk.Data.Length > maximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk));
        }

        if (chunk.Timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk));
        }

        byte[] ownedData = chunk.Data.ToArray();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed)
            {
                throw new InvalidOperationException("The live stream buffer is already complete.", completionError);
            }

            // MPEG-TS output callbacks are emitted in byte-stream order, but an
            // interleaved audio packet may carry a timestamp slightly behind the
            // preceding video packet. The timestamp here controls retention only;
            // preserve byte order and clamp its buffer metadata instead of
            // terminating an otherwise valid stream.
            TimeSpan normalizedTimestamp = latestTimestamp is TimeSpan previous &&
                                           chunk.Timestamp < previous
                ? previous
                : chunk.Timestamp;
            MediaStreamChunk ownedChunk = chunk with
            {
                Data = ownedData,
                Timestamp = normalizedTimestamp,
            };
            latestTimestamp = normalizedTimestamp;
            chunks.AddLast(ownedChunk);
            bufferedBytes += ownedData.Length;
            TrimUnsafe(normalizedTimestamp);

            List<long>? overflowed = null;
            foreach ((long id, Subscriber subscriber) in subscribers)
            {
                if (subscriber.AwaitingKeyframe)
                {
                    if (!ownedChunk.StartsAtRandomAccessPoint)
                    {
                        continue;
                    }

                    subscriber.AwaitingKeyframe = false;
                }

                if (!subscriber.Channel.Writer.TryWrite(ownedChunk))
                {
                    overflowed ??= [];
                    overflowed.Add(id);
                }
            }

            if (overflowed is not null)
            {
                foreach (long id in overflowed)
                {
                    Subscriber subscriber = subscribers[id];
                    subscribers.Remove(id);
                    disconnectedSlowSubscribers++;
                    subscriber.Channel.Writer.TryComplete(new InvalidOperationException(
                        "The live stream client was disconnected because its bounded queue overflowed."));
                }
            }
        }
    }

    public LiveStreamSubscription Subscribe(bool startAtLiveEdge = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed)
            {
                throw new InvalidOperationException("The live stream buffer is already complete.", completionError);
            }

            List<MediaStreamChunk> snapshot = [];
            if (!startAtLiveEdge)
            {
                LinkedListNode<MediaStreamChunk>? start = chunks.Last;
                while (start is not null && !start.Value.StartsAtRandomAccessPoint)
                {
                    start = start.Previous;
                }

                if (start is null)
                {
                    throw new InvalidOperationException("No keyframe-aware stream start point is buffered yet.");
                }

                for (LinkedListNode<MediaStreamChunk>? node = start; node is not null; node = node.Next)
                {
                    snapshot.Add(node.Value);
                }
            }

            Channel<MediaStreamChunk> channel = Channel.CreateBounded<MediaStreamChunk>(new BoundedChannelOptions(
                subscriberCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
            long id = ++nextSubscriberId;
            subscribers.Add(id, new(channel) { AwaitingKeyframe = startAtLiveEdge });
            return new(id, snapshot, channel.Reader, Unsubscribe);
        }
    }

    public void Complete(Exception? completionException = null)
    {
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            completionError = completionException;
            foreach (Subscriber subscriber in subscribers.Values)
            {
                subscriber.Channel.Writer.TryComplete(completionException);
            }

            subscribers.Clear();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            completed = true;
            foreach (Subscriber subscriber in subscribers.Values)
            {
                subscriber.Channel.Writer.TryComplete();
            }

            subscribers.Clear();
            chunks.Clear();
            bufferedBytes = 0;
            disposed = true;
        }
    }

    private void TrimUnsafe(TimeSpan newestTimestamp)
    {
        while (chunks.First is not null &&
               (bufferedBytes > maximumBytes || newestTimestamp - chunks.First.Value.Timestamp > maximumDuration))
        {
            bufferedBytes -= chunks.First.Value.Data.Length;
            chunks.RemoveFirst();
            evictedChunks++;
        }
    }

    private void Unsubscribe(long id)
    {
        lock (gate)
        {
            if (subscribers.Remove(id, out Subscriber? subscriber))
            {
                subscriber.Channel.Writer.TryComplete();
            }
        }
    }

    private sealed class Subscriber(Channel<MediaStreamChunk> channel)
    {
        public Channel<MediaStreamChunk> Channel { get; } = channel;

        public bool AwaitingKeyframe { get; set; }
    }
}
