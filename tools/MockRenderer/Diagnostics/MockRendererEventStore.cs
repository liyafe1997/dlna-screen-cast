using System.Collections.Concurrent;

namespace DesktopDlnaCast.MockRenderer.Diagnostics;

public sealed class MockRendererEventStore
{
    private const int MaximumEvents = 1000;
    private const int MaximumValueLength = 4096;
    private readonly ConcurrentQueue<MockRendererEvent> events = new();
    private long sequence;

    public void Record(string type, params (string Key, string? Value)[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Dictionary<string, string> safeData = new(StringComparer.Ordinal);
        foreach ((string key, string? value) in data)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            string bounded = value.Length > MaximumValueLength ? value[..MaximumValueLength] : value;
            safeData[key] = DiagnosticRedactor.RedactTokens(bounded);
        }

        events.Enqueue(new(
            Interlocked.Increment(ref sequence),
            DateTimeOffset.UtcNow,
            type,
            safeData));
        while (events.Count > MaximumEvents)
        {
            events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<MockRendererEvent> Snapshot() => events.ToArray();
}

