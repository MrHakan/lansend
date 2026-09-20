using System.Text.Json;

namespace LANSEND;

internal static class AppConstants
{
    public const string Protocol = "LANSEND/1";
    public const int DiscoveryPort = 43821;
    public const int TransferPort = 43822;
    public const string MutexName = "LANSEND_SINGLE_INSTANCE_V1";
    public const string ControlPipeName = "LANSEND_CONTROL_V1";
    public const int DiscoveryIntervalMilliseconds = 2500;
    public const int PeerTimeoutSeconds = 9;
    public const int MaxProtocolLineBytes = 1024 * 1024;
    public const int MaxFileCount = 10_000;
    public const long MaxFileSize = 50L * 1024 * 1024 * 1024;
    public const long MaxTransferSize = 200L * 1024 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
