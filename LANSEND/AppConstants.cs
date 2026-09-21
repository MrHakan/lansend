using System.Text.Json;

namespace LANSEND;

internal static class AppConstants
{
    public const string MutexName = "LANSEND_SINGLE_INSTANCE_V1";
    public const string ControlPipeName = "LANSEND_CONTROL_V1";
    public const string DefaultShareName = "LANSEND";
    public const int SmbPort = 445;
    public const int PingTimeoutMilliseconds = 320;
    public const int PortProbeTimeoutMilliseconds = 260;
    public const int DnsLookupTimeoutMilliseconds = 650;
    public const int MaxScanHostsPerAdapter = 512;
    public const int MaxTotalScanHosts = 1024;
    public const int ScanParallelism = 48;

    public static readonly int[] DiscoveryPorts =
    [
        SmbPort,
        139,   // NetBIOS
        135,   // Windows RPC
        3389,  // Remote Desktop
        80,    // HTTP
        443,   // HTTPS
        22,    // SSH
        53,    // DNS
        631,   // IPP printers
        62078  // Apple device service
    ];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
