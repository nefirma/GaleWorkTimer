using System.Runtime.InteropServices;
using GaleWorkTimer.Packaging;

namespace GaleWorkTimer.Setup;

internal static class Program
{
    private const uint MessageBoxInformation = 0x00000040;
    private const uint MessageBoxError = 0x00000010;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            var reason = AddCopyInstruction($"[SETUP_FAILED:{exception.GetType().Name}] Не удалось выполнить установку или проверку GaleWorkTimer.");
            if (OperatingSystem.IsWindows())
                ShowMessage(reason, MessageBoxError);
            else
                Console.Error.WriteLine(reason);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 4 && args[0] == "--pack")
            return RunPack(args[1], args[2], args[3]);
        if (args.Length == 2 && args[0] == "--verify")
            return RunVerify(args[1]);
        if (args.Length == 2 && args[0] == "--sha256")
            return RunSha256(args[1]);
        if (args.Length == 1 && args[0] == "--verify-bundled")
        {
            using var package = OpenBundledPackage();
            return WithBundledPackage(package, RunVerify);
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The installer and launcher run on Windows; package creation and verification run on Linux.");
            return 2;
        }

        var manager = new InstallManager(InstallManager.DefaultInstallRoot);
        var processPath = Environment.ProcessPath;
        if (SamePath(processPath, manager.StableEntryPath))
        {
            var launch = manager.LaunchStableEntry(args);
            if (launch.Outcome == InstallOutcome.RecoveredPrevious)
                ShowMessage(AddCopyInstruction(launch.FailureReason ?? "Предыдущая проверенная версия восстановлена."), MessageBoxInformation);
            else if (launch.Outcome == InstallOutcome.Rejected)
                ShowMessage(AddCopyInstruction(launch.FailureReason ?? "Не удалось запустить приложение."), MessageBoxError);
            return launch.Outcome == InstallOutcome.Rejected ? 1 : 0;
        }

        if (!string.Equals(Path.GetFileName(processPath), "GaleWorkTimer.Setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            ShowMessage("Для установки запусти опубликованный GaleWorkTimer.Setup.exe без .NET SDK.", MessageBoxError);
            return 2;
        }

        if (args.Length > 1)
        {
            ShowMessage("Укажи один ZIP-пакет обновления.", MessageBoxError);
            return 2;
        }
        InstallResult result;
        if (args.Length == 1)
            result = manager.InstallPackage(args[0], processPath!);
        else
        {
            using var package = OpenBundledPackage();
            result = InstallBundledPackage(manager, processPath!, package);
        }
        if (result.Outcome is InstallOutcome.Installed or InstallOutcome.Updated)
        {
            ShowMessage($"Версия {result.InstalledVersion} установлена. Стабильный запуск: {result.StableEntryPath}",
                MessageBoxInformation);
            return 0;
        }
        ShowMessage(AddCopyInstruction(result.FailureReason ?? "Установка отклонена; текущая проверенная версия не изменена."), MessageBoxError);
        return 1;
    }

    private static Stream OpenBundledPackage() =>
        typeof(Program).Assembly.GetManifestResourceStream("GaleWorkTimer.Package.zip")
        ?? throw new InvalidDataException("The installer has no bundled package.");

    internal static InstallResult InstallBundledPackage(InstallManager manager, string launcherPath, Stream package) =>
        WithBundledPackage(package, path => manager.InstallPackage(path, launcherPath));

    private static T WithBundledPackage<T>(Stream package, Func<string, T> action)
    {
        var directory = Directory.CreateTempSubdirectory("GaleWorkTimer-Setup-");
        var path = Path.Combine(directory.FullName, "package.zip");
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                package.CopyTo(output);
            return action(path);
        }
        finally
        {
            try
            {
                File.Delete(path);
                directory.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked temporary package must not turn a committed installation into a reported failure.
            }
        }
    }

    private static int RunPack(string publishDirectory, string packagePath, string version)
    {
        try
        {
            var result = PackageBuilder.CreatePackage(publishDirectory, packagePath, version);
            Console.WriteLine($"version={result.Version} files={result.FileCount} sha256={result.Sha256}");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Package creation failed.");
            return 1;
        }
    }

    private static int RunVerify(string packagePath)
    {
        try
        {
            var result = PackageBuilder.VerifyPackage(packagePath);
            Console.WriteLine($"version={result.Version} files={result.FileCount} sha256={result.Sha256}");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Package verification failed.");
            return 1;
        }
    }

    private static int RunSha256(string path)
    {
        try
        {
            Console.WriteLine(PackageBuilder.ComputeFileSha256(path));
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("SHA-256 calculation failed.");
            return 1;
        }
    }

    private static bool SamePath(string? left, string right) => left is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string AddCopyInstruction(string text) => text + "\n\nНажми Ctrl+C, чтобы скопировать сообщение команде.";

    private static void ShowMessage(string text, uint icon) =>
        MessageBoxW(IntPtr.Zero, text, "GaleWorkTimer", icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

}
