using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LANSEND;

internal sealed class TransferServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public Func<IncomingTransferRequest, Task<bool>>? ConfirmTransferAsync { get; set; }
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, AppConstants.TransferPort);
        _listener.Start();
        _ = AcceptLoopAsync(_listener, _cancellation.Token);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        using (client)
        {
            client.NoDelay = true;
            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
            var cancellationToken = connectionCancellation.Token;

            try
            {
                await using var stream = client.GetStream();
                var offerLine = await ProtocolIo.ReadLineAsync(stream, cancellationToken);
                if (string.IsNullOrWhiteSpace(offerLine))
                {
                    return;
                }

                var offer = JsonSerializer.Deserialize<TransferOffer>(offerLine, AppConstants.JsonOptions);
                if (!IsValidOffer(offer, out var validationError))
                {
                    await ProtocolIo.SendJsonLineAsync(stream, new TransferResponse
                    {
                        Type = "decision",
                        Accepted = false,
                        Message = validationError
                    }, cancellationToken);
                    return;
                }

                var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "Bilinmeyen IP";
                var request = new IncomingTransferRequest
                {
                    DeviceName = offer!.SenderDeviceName,
                    UserName = offer.SenderUserName,
                    RemoteIpAddress = remoteIp,
                    Files = offer.Files,
                    TotalBytes = offer.TotalBytes
                };

                var accepted = ConfirmTransferAsync is null || await ConfirmTransferAsync(request);
                if (!accepted)
                {
                    await ProtocolIo.SendJsonLineAsync(stream, new TransferResponse
                    {
                        Type = "decision",
                        Accepted = false,
                        Message = "Aktarım alıcı tarafından reddedildi."
                    }, cancellationToken);
                    return;
                }

                await ProtocolIo.SendJsonLineAsync(stream, new TransferResponse
                {
                    Type = "decision",
                    Accepted = true,
                    Message = "Aktarım kabul edildi."
                }, cancellationToken);

                var receivedPaths = await ReceiveFilesAsync(stream, offer.Files, cancellationToken);
                await ProtocolIo.SendJsonLineAsync(stream, new TransferResponse
                {
                    Type = "complete",
                    Accepted = true,
                    Message = "Dosyalar Documents\\LANSEND klasörüne kaydedildi."
                }, cancellationToken);

                TransferCompleted?.Invoke(this, new TransferCompletedEventArgs
                {
                    RemoteIpAddress = remoteIp,
                    ReceivedPaths = receivedPaths
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                try
                {
                    await ProtocolIo.SendJsonLineAsync(client.GetStream(), new TransferResponse
                    {
                        Type = "complete",
                        Accepted = false,
                        Message = exception.Message
                    }, CancellationToken.None);
                }
                catch
                {
                    // The peer may have already disconnected.
                }
            }
        }
    }

    private static bool IsValidOffer(TransferOffer? offer, out string error)
    {
        if (offer is null || offer.Protocol != AppConstants.Protocol)
        {
            error = "Geçersiz LANSEND aktarım isteği.";
            return false;
        }

        if (offer.Files.Count == 0 || offer.Files.Count > AppConstants.MaxFileCount)
        {
            error = "Dosya sayısı geçersiz.";
            return false;
        }

        var calculatedTotal = 0L;
        foreach (var file in offer.Files)
        {
            if (file.Length < 0 || file.Length > AppConstants.MaxFileSize || string.IsNullOrWhiteSpace(file.RelativePath))
            {
                error = "Dosya bilgileri geçersiz.";
                return false;
            }

            try
            {
                calculatedTotal = checked(calculatedTotal + file.Length);
            }
            catch (OverflowException)
            {
                error = "Toplam aktarım boyutu geçersiz.";
                return false;
            }
        }

        if (calculatedTotal > AppConstants.MaxTransferSize || calculatedTotal != offer.TotalBytes)
        {
            error = "Toplam aktarım boyutu geçersiz.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static async Task<IReadOnlyList<string>> ReceiveFilesAsync(
        Stream stream,
        IReadOnlyList<TransferItemInfo> files,
        CancellationToken cancellationToken)
    {
        var destinationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LANSEND");
        Directory.CreateDirectory(destinationRoot);

        var rootWithSeparator = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var receivedPaths = new List<string>(files.Count);

        foreach (var item in files)
        {
            var safeRelativePath = SanitizeRelativePath(item.RelativePath);
            var candidate = Path.GetFullPath(Path.Combine(destinationRoot, safeRelativePath));
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Dosya yolu güvenlik kontrolünü geçemedi.");
            }

            var finalPath = GetAvailablePath(candidate);
            var directory = Path.GetDirectoryName(finalPath) ?? destinationRoot;
            Directory.CreateDirectory(directory);
            var temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";

            try
            {
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await ProtocolIo.CopyExactlyAsync(stream, output, item.Length, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, finalPath);
                receivedPaths.Add(finalPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        return receivedPaths;
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        var pieces = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var safePieces = pieces
            .Where(piece => piece is not "." and not "..")
            .Select(SanitizePathPart)
            .Where(piece => piece.Length > 0)
            .ToArray();

        return safePieces.Length == 0 ? "unnamed-file" : Path.Combine(safePieces);
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (sanitized.Length == 0)
        {
            return "unnamed-file";
        }

        return sanitized.Length > 180 ? sanitized[..180] : sanitized;
    }

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({counter++}){extension}");
        }
        while (File.Exists(candidate));

        return candidate;
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
    }
}
