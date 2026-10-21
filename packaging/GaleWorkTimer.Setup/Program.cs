using System.Runtime.InteropServices;
using System.Text;
using GaleWorkTimer.Packaging;

namespace GaleWorkTimer.Setup;

internal static class Program
{
    private const uint MessageBoxInformation = 0x00000040;
    private const uint MessageBoxError = 0x00000010;
    private const uint FileMustExist = 0x00001000;
    private const uint PathMustExist = 0x00000800;
    private const uint NoChangeDirectory = 0x00000008;

    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--pack")
            return RunPack(args[1], args[2], args[3]);
        if (args.Length == 2 && args[0] == "--verify")
            return RunVerify(args[1]);
        if (args.Length == 2 && args[0] == "--sha256")
            return RunSha256(args[1]);
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
        var packagePath = args.Length == 1 ? args[0] : SelectPackage();
        if (string.IsNullOrWhiteSpace(packagePath))
            return 0;

        var result = manager.InstallPackage(packagePath, processPath!);
        if (result.Outcome is InstallOutcome.Installed or InstallOutcome.Updated)
        {
            ShowMessage($"Версия {result.InstalledVersion} установлена. Стабильный запуск: {result.StableEntryPath}",
                MessageBoxInformation);
            return 0;
        }
        ShowMessage(AddCopyInstruction(result.FailureReason ?? "Установка отклонена; текущая проверенная версия не изменена."), MessageBoxError);
        return 1;
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

    private static string? SelectPackage()
    {
        var fileName = new StringBuilder(32768);
        var fileTitle = new StringBuilder(1024);
        var dialog = new OpenFileName
        {
            StructSize = Marshal.SizeOf<OpenFileName>(),
            Filter = "GaleWorkTimer package (*.zip)\0*.zip\0All files (*.*)\0*.*\0\0",
            File = fileName,
            MaxFile = fileName.Capacity,
            FileTitle = fileTitle,
            MaxFileTitle = fileTitle.Capacity,
            Title = "Выбери пакет GaleWorkTimer",
            Flags = FileMustExist | PathMustExist | NoChangeDirectory
        };
        return GetOpenFileNameW(ref dialog) ? fileName.ToString() : null;
    }

    private static bool SamePath(string? left, string right) => left is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string AddCopyInstruction(string text) => text + "\n\nНажми Ctrl+C, чтобы скопировать сообщение команде.";

    private static void ShowMessage(string text, uint icon) =>
        MessageBoxW(IntPtr.Zero, text, "GaleWorkTimer", icon);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetOpenFileNameW")]
    private static extern bool GetOpenFileNameW(ref OpenFileName dialog);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public string Filter;
        public string? CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public StringBuilder File;
        public int MaxFile;
        public StringBuilder FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory;
        public string Title;
        public uint Flags;
        public short FileOffset;
        public short FileExtension;
        public string? DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public string? TemplateName;
        public IntPtr Reserved;
        public int Reserved2;
        public uint FlagsEx;
    }
}
