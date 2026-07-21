using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Streaming.Buffering;

public sealed class LiveStreamSubscription : IAsyncDisposable
{
    private readonly IReadOnlyList<MediaStreamChunk> startupSnapshot;
    private readonly ChannelReader<MediaStreamChunk> reader;
    private readonly Action<long> unsubscribe;
    private readonly long id;
    private int disposed;

    internal LiveStreamSubscription(
        long id,
        IReadOnlyList<MediaStreamChunk> startupSnapshot,
        ChannelReader<MediaStreamChunk> reader,
        Action<long> unsubscribe)
    {
        this.id = id;
        this.startupSnapshot = startupSnapshot;
        this.reader = reader;
        this.unsubscribe = unsubscribe;
    }

    public async IAsyncEnumerable<MediaStreamChunk> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        foreach (MediaStreamChunk chunk in startupSnapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }

        await foreach (MediaStreamChunk chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            unsubscribe(id);
        }

        return ValueTask.CompletedTask;
    }
}
