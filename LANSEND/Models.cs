using System.Text.Json.Serialization;

namespace LANSEND;

public sealed class DeviceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ShareName { get; set; } = AppConstants.DefaultShareName;

    [JsonIgnore]
    public bool IsOnline { get; set; }

    [JsonIgnore]
    public bool IsSmbAvailable { get; set; }

    [JsonIgnore]
    public string DiscoveryMethod { get; set; } = string.Empty;

    [JsonIgnore]
    public string Status => !IsOnline
        ? "Çevrimdışı"
        : IsSmbAvailable
            ? "Çevrimiçi • SMB hazır"
            : $"Çevrimiçi{(string.IsNullOrWhiteSpace(DiscoveryMethod) ? string.Empty : $" • {DiscoveryMethod}")}";

    [JsonIgnore]
    public string UncPath => $@"\\{IpAddress}\{(string.IsNullOrWhiteSpace(ShareName) ? AppConstants.DefaultShareName : ShareName)}";
}

public sealed class TransferFile
{
    public string FullPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }

    public string DisplayName => Path.GetFileName(RelativePath);
}

public sealed class TransferProgress
{
    public long BytesSent { get; init; }
    public long TotalBytes { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public double Percentage => TotalBytes <= 0 ? 100 : BytesSent * 100d / TotalBytes;
}

internal sealed class ControlRequest
{
    public string Command { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
}

internal sealed class SmbCredentials
{
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class TransferFileBuildResult
{
    public IReadOnlyList<TransferFile> Files { get; init; } = Array.Empty<TransferFile>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public long TotalBytes => Files.Sum(file => file.Length);
}
