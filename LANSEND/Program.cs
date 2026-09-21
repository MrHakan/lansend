using System.Windows.Forms;

namespace LANSEND;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, AppConstants.MutexName, out var isFirstInstance);
        var paths = args
            .Where(argument => !argument.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            .Where(argument => File.Exists(argument) || Directory.Exists(argument))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!isFirstInstance)
        {
            ControlPipeService.TryForwardAsync(paths, CancellationToken.None).GetAwaiter().GetResult();
            return;
        }

        ShellIntegrationService.EnsureSendToShortcut(out _);

        var startHidden = args.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
        var discovery = new LanDeviceDiscoveryService();
        var transfer = new DirectSmbTransferService();
        var profileStore = new DeviceProfileStore();
        using var controlPipe = new ControlPipeService();
        using var form = new MainForm(discovery, transfer, profileStore, controlPipe, paths, startHidden);

        controlPipe.Start();
        Application.Run(form);
    }
}
