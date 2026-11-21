using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GaleWorkTimer.Setup;

internal static class DesktopShortcut
{
    [SupportedOSPlatform("windows")]
    internal static void Create(string stableEntryPath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(desktop))
            throw new IOException("Desktop directory is unavailable.");
        Directory.CreateDirectory(desktop);
        var shortcutPath = Path.Combine(desktop, "GaleWorkTimer.lnk");
        try
        {
            if ((File.GetAttributes(shortcutPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Shortcut path is a reparse point.");
        }
        catch (FileNotFoundException) { }

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = stableEntryPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(stableEntryPath)!;
                shortcut.Arguments = string.Empty;
                shortcut.Description = "GaleWorkTimer";
                shortcut.IconLocation = stableEntryPath + ",0";
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
