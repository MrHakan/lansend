namespace LANSEND;

public sealed class PeerInfo
{
    public string Id { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int TransferPort { get; init; } = AppConstants.TransferPort;
    public DateTime LastSeenUtc { get; init; }

    public string DisplayName => $"{DeviceName} ({UserName})";
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

public sealed class IncomingTransferRequest
{
    public string DeviceName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string RemoteIpAddress { get; init; } = string.Empty;
    public IReadOnlyList<TransferItemInfo> Files { get; init; } = Array.Empty<TransferItemInfo>();
    public long TotalBytes { get; init; }
}

public sealed class TransferItemInfo
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
}

public sealed class TransferCompletedEventArgs : EventArgs
{
    public string RemoteIpAddress { get; init; } = string.Empty;
    public IReadOnlyList<string> ReceivedPaths { get; init; } = Array.Empty<string>();
}

internal sealed class DiscoveryMessage
{
    public string Protocol { get; set; } = AppConstants.Protocol;
    public string InstanceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int TransferPort { get; set; } = AppConstants.TransferPort;
}

internal sealed class TransferOffer
{
    public string Protocol { get; set; } = AppConstants.Protocol;
    public string TransferId { get; set; } = string.Empty;
    public string SenderDeviceName { get; set; } = string.Empty;
    public string SenderUserName { get; set; } = string.Empty;
    public List<TransferItemInfo> Files { get; set; } = new();
    public long TotalBytes { get; set; }
}

internal sealed class TransferResponse
{
    public string Type { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class ControlRequest
{
    public string Command { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
}

public sealed class TransferFileBuildResult
{
    public IReadOnlyList<TransferFile> Files { get; init; } = Array.Empty<TransferFile>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public long TotalBytes => Files.Sum(file => file.Length);
}
