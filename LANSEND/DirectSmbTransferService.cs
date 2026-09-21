using System.ComponentModel;

namespace LANSEND;

internal sealed class DirectSmbTransferService
{
    public async Task SendAsync(
        DeviceProfile target,
        IReadOnlyList<TransferFile> files,
        SmbCredentials? credentials,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Gönderilecek dosya bulunamadı.");
        }

        IDisposable? connection = null;
        try
        {
            if (credentials is not null)
            {
                try
                {
                    connection = SmbConnection.Connect(target.UncPath, credentials);
                }
                catch (Win32Exception exception)
                {
                    throw new SmbAuthenticationRequiredException(
                        $"{target.UncPath} paylaşımına bağlanılamadı: {exception.Message}",
                        exception);
                }
            }

            await EnsureRootIsReadyAsync(target, cancellationToken);
            var totalBytes = files.Sum(file => file.Length);
            var sentBytes = 0L;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CreateDestinationPath(target.UncPath, file.RelativePath);
                await CopyFileAsync(file, destination, totalBytes, sentBytes, progress, cancellationToken);
                sentBytes += file.Length;
            }
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private static Task EnsureRootIsReadyAsync(DeviceProfile target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(target.UncPath);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SmbAuthenticationRequiredException(
                $"{target.UncPath} paylaşımına erişilemedi. Hedef bilgisayarda Enable-DirectTarget.ps1 bir kez çalıştırılmalı veya SMB kimlik bilgileri girilmeli.",
                exception);
        }
    }

    private static async Task CopyFileAsync(
        TransferFile source,
        string destination,
        long totalBytes,
        long alreadySent,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryDestination = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        var buffer = new byte[128 * 1024];
        var copied = 0L;

        try
        {
            await using (var input = new FileStream(
                             source.FullPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporaryDestination,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (copied < source.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = (int)Math.Min(buffer.Length, source.Length - copied);
                    var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                    if (read == 0)
                    {
                        throw new EndOfStreamException($"Dosya aktarım sırasında değişti: {source.FullPath}");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    progress?.Report(new TransferProgress
                    {
                        BytesSent = alreadySent + copied,
                        TotalBytes = totalBytes,
                        CurrentFile = source.DisplayName
                    });
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryDestination, destination);
            if (source.Length == 0)
            {
                progress?.Report(new TransferProgress
                {
                    BytesSent = alreadySent,
                    TotalBytes = totalBytes,
                    CurrentFile = source.DisplayName
                });
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryDestination))
                {
                    File.Delete(temporaryDestination);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static string CreateDestinationPath(string root, string relativePath)
    {
        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Geçersiz hedef dosya yolu.");
        }

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("Geçersiz hedef klasör yolu.");
        }

        Directory.CreateDirectory(directory);
        return GetAvailablePath(destination);
    }

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index <= 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Hedefte uygun dosya adı bulunamadı: {path}");
    }
}

internal sealed class SmbAuthenticationRequiredException : IOException
{
    public SmbAuthenticationRequiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
