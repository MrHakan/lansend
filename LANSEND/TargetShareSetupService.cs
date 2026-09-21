using System.ComponentModel;
using System.Diagnostics;

namespace LANSEND;

internal static class TargetShareSetupService
{
    public static async Task ConfigureThisComputerAsync(CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "Enable-DirectTarget.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "Enable-DirectTarget.ps1 bulunamadı. LANSEND.exe ile Scripts klasörünü birlikte tutun.",
                scriptPath);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Hedef paylaşım kurulumu başlatılamadı.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Hedef paylaşım kurulumu tamamlanamadı (çıkış kodu: {process.ExitCode}).");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Yönetici izni iptal edildi.", exception, cancellationToken);
        }
    }
}
