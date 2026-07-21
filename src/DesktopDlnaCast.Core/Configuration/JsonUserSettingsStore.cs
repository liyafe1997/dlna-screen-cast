using System.Text.Json;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.Core.Configuration;

public sealed class JsonUserSettingsStore : IUserSettingsStore, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogSettingsReadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(120, nameof(LogSettingsReadFailed)),
            "User settings could not be read from {SettingsPath}; defaults will be used");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string settingsPath;
    private readonly ILogger<JsonUserSettingsStore> logger;
    private readonly SemaphoreSlim accessLock = new(1, 1);

    public JsonUserSettingsStore(string settingsPath, ILogger<JsonUserSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new();
            }

            await using FileStream stream = new(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<UserSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false) ?? new();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            LogSettingsReadFailed(logger, settingsPath, exception);
            return new();
        }
        finally
        {
            accessLock.Release();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            string directory = Path.GetDirectoryName(settingsPath) ??
                throw new InvalidOperationException("The user settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            accessLock.Release();
        }
    }

    public void Dispose()
    {
        accessLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
