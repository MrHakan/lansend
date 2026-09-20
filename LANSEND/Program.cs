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

        var startHidden = args.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
        using var discovery = new PeerDiscoveryService();
        using var server = new TransferServer();
        var transferClient = new TransferClient();
        using var controlPipe = new ControlPipeService();
        using var form = new MainForm(discovery, server, transferClient, controlPipe, paths, startHidden);

        discovery.Start();
        server.Start();
        controlPipe.Start();
        Application.Run(form);
    }
}
