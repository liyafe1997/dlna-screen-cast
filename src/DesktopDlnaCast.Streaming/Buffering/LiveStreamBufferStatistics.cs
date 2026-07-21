namespace DesktopDlnaCast.Streaming.Buffering;

public sealed record LiveStreamBufferStatistics(
    int BufferedChunks,
    long BufferedBytes,
    long EvictedChunks,
    int ActiveSubscribers,
    long DisconnectedSlowSubscribers);
