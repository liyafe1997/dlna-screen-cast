namespace DesktopDlnaCast.Upnp.Http;

internal static class BoundedHttpContentReader
{
    public static async Task<MemoryStream> ReadAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The HTTP response exceeds the configured size limit.");
        }

        int initialCapacity = content.Headers.ContentLength is > 0 and <= int.MaxValue
            ? (int)content.Headers.ContentLength.Value
            : Math.Min(maximumBytes, 16 * 1024);
        MemoryStream destination = new(initialCapacity);
        try
        {
            await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[16 * 1024];
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new InvalidDataException("The HTTP response exceeds the configured size limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            destination.Position = 0;
            return destination;
        }
        catch
        {
            await destination.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
