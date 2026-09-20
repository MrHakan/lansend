using System.Text;
using System.Text.Json;

namespace LANSEND;

internal static class ProtocolIo
{
    public static async Task SendJsonLineAsync<T>(Stream stream, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, AppConstants.JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(oneByte.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (oneByte[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
            }

            buffer.WriteByte(oneByte[0]);
            if (buffer.Length > AppConstants.MaxProtocolLineBytes)
            {
                throw new InvalidDataException("LANSEND protocol line is too large.");
            }
        }
    }

    public static async Task CopyExactlyAsync(
        Stream source,
        Stream destination,
        long bytesToCopy,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        var remaining = bytesToCopy;

        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The sender closed the connection before the file was complete.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }
}
