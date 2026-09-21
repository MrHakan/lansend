using System.Text.Json;

namespace LANSEND;

internal static class AppConstants
{
    public const string MutexName = "LANSEND_SINGLE_INSTANCE_V1";
    public const string ControlPipeName = "LANSEND_CONTROL_V1";
    public const string DefaultShareName = "LANSEND";
    public const int SmbPort = 445;
    public const int SmbProbeTimeoutMilliseconds = 350;
    public const int MaxScanHosts = 512;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
