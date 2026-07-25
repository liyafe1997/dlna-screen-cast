using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.App.Diagnostics;

public interface IAppLogStore
{
    string GetSnapshot();
}

public sealed class AppLogStore : ILoggerProvider, IAppLogStore
{
    private static readonly Regex SessionTokenPattern = new(
        @"/(stream|hls)/[A-Fa-f0-9]{32,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int MaximumEntries = 500;
    private const int MaximumTextLength = 64 * 1024;
    private readonly object sync = new();
    private readonly Queue<string> entries = new();
    private int textLength;

    public ILogger CreateLogger(string categoryName) => new StoreLogger(this, categoryName);

    public string GetSnapshot()
    {
        lock (sync)
        {
            return entries.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, entries);
        }
    }

    public void Dispose()
    {
    }

    private void Append(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        string categoryName = category[(category.LastIndexOf('.') + 1)..];
        StringBuilder builder = new();
        builder.Append(DateTimeOffset.Now.ToString(
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(categoryName);
        if (eventId.Id != 0)
        {
            builder.Append(" (").Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append('/').Append(eventId.Name);
            }

            builder.Append(')');
        }

        builder.Append(": ").Append(Redact(message));
        if (exception is not null)
        {
            builder.AppendLine().Append(Redact(exception.ToString()));
        }

        string entry = builder.ToString();
        lock (sync)
        {
            entries.Enqueue(entry);
            textLength += entry.Length + Environment.NewLine.Length;
            while (entries.Count > MaximumEntries || textLength > MaximumTextLength)
            {
                string removed = entries.Dequeue();
                textLength -= removed.Length + Environment.NewLine.Length;
            }
        }
    }

    private static string Redact(string value) =>
        SessionTokenPattern.Replace(value, "/$1/<redacted>");

    private sealed class StoreLogger(AppLogStore owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Append(category, logLevel, eventId, formatter(state, exception), exception);
            }
        }
    }
}
