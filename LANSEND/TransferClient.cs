using System.Net.Sockets;
using System.Text.Json;

namespace LANSEND;

internal sealed class TransferClient
{
    public async Task SendAsync(
        PeerInfo peer,
        IReadOnlyList<TransferFile> files,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Gönderilecek dosya bulunamadı.");
        }

        var totalBytes = files.Sum(file => file.Length);
        using var client = new TcpClient
        {
            NoDelay = true
        };
        await client.ConnectAsync(peer.IpAddress, peer.TransferPort, cancellationToken);

        await using var stream = client.GetStream();
        var offer = new TransferOffer
        {
            TransferId = Guid.NewGuid().ToString("N"),
            SenderDeviceName = Environment.MachineName,
            SenderUserName = Environment.UserName,
            Files = files.Select(file => new TransferItemInfo
            {
                RelativePath = file.RelativePath,
                Length = file.Length
            }).ToList(),
            TotalBytes = totalBytes
        };

        await ProtocolIo.SendJsonLineAsync(stream, offer, cancellationToken);
        var decisionLine = await ProtocolIo.ReadLineAsync(stream, cancellationToken)
            ?? throw new IOException("Alıcı cihaz bağlantıyı kapattı.");
        var decision = JsonSerializer.Deserialize<TransferResponse>(decisionLine, AppConstants.JsonOptions)
            ?? throw new InvalidDataException("Alıcı cihazdan geçersiz yanıt geldi.");

        if (!decision.Accepted)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(decision.Message)
                ? "Aktarım alıcı tarafından reddedildi."
                : decision.Message);
        }

        var sentBytes = 0L;
        var buffer = new byte[128 * 1024];

        foreach (var file in files)
        {
            await using var input = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: buffer.Length,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var remaining = file.Length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Dosya aktarım sırasında değişti: {file.FullPath}");
                }

                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                sentBytes += read;
                progress?.Report(new TransferProgress
                {
                    BytesSent = sentBytes,
                    TotalBytes = totalBytes,
                    CurrentFile = file.DisplayName
                });
            }
        }

        await stream.FlushAsync(cancellationToken);
        var completionLine = await ProtocolIo.ReadLineAsync(stream, cancellationToken)
            ?? throw new IOException("Alıcı cihaz aktarım tamamlandı yanıtı vermedi.");
        var completion = JsonSerializer.Deserialize<TransferResponse>(completionLine, AppConstants.JsonOptions);
        if (completion is null || !completion.Accepted || completion.Type != "complete")
        {
            throw new IOException(completion?.Message ?? "Alıcı cihaz dosyaları kaydedemedi.");
        }
    }
}
