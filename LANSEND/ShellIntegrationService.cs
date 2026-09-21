using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LANSEND;

internal static class ShellIntegrationService
{
    public static bool EnsureSendToShortcut(out string? error)
    {
        error = null;
        object? shell = null;
        object? shortcut = null;
        try
        {
            var executablePath = Application.ExecutablePath;
            var sendToDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "SendTo");
            Directory.CreateDirectory(sendToDirectory);

            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows kısayol servisi bulunamadı.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows kısayol servisi başlatılamadı.");
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(Path.Combine(sendToDirectory, "LANSEND.lnk"));
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = executablePath;
            dynamicShortcut.Arguments = string.Empty;
            dynamicShortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
            dynamicShortcut.IconLocation = $"{executablePath},0";
            dynamicShortcut.Description = "LANSEND ile yerel ağda dosya gönder";
            dynamicShortcut.Save();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
