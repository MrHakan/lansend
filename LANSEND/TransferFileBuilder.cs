namespace LANSEND;

internal static class TransferFileBuilder
{
    public static TransferFileBuildResult Build(IEnumerable<string> inputPaths)
    {
        var files = new List<TransferFile>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                var fullPath = Path.GetFullPath(inputPath);
                if (File.Exists(fullPath))
                {
                    AddFile(fullPath, Path.GetFileName(fullPath), files, seen, errors);
                    continue;
                }

                if (Directory.Exists(fullPath))
                {
                    AddDirectory(fullPath, files, seen, errors);
                    continue;
                }

                errors.Add($"Bulunamadı: {inputPath}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"Okunamadı: {inputPath} ({exception.Message})");
            }
        }

        return new TransferFileBuildResult
        {
            Files = files,
            Errors = errors
        };
    }

    private static void AddDirectory(
        string directoryPath,
        ICollection<TransferFile> files,
        ISet<string> seen,
        ICollection<string> errors)
    {
        var directoryName = new DirectoryInfo(directoryPath).Name;
        IEnumerable<string> entries;

        try
        {
            entries = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Klasör okunamadı: {directoryPath} ({exception.Message})");
            return;
        }

        try
        {
            foreach (var filePath in entries)
            {
                try
                {
                    var relative = Path.Combine(directoryName, Path.GetRelativePath(directoryPath, filePath));
                    AddFile(filePath, relative, files, seen, errors);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add($"Okunamadı: {filePath} ({exception.Message})");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Klasör taranırken hata oluştu: {directoryPath} ({exception.Message})");
        }
    }

    private static void AddFile(
        string fullPath,
        string relativePath,
        ICollection<TransferFile> files,
        ISet<string> seen,
        ICollection<string> errors)
    {
        if (!seen.Add(fullPath))
        {
            return;
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                errors.Add($"Bulunamadı: {fullPath}");
                return;
            }

            files.Add(new TransferFile
            {
                FullPath = info.FullName,
                RelativePath = relativePath,
                Length = info.Length
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add($"Okunamadı: {fullPath} ({exception.Message})");
        }
    }
}
