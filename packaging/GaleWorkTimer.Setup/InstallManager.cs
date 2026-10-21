using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GaleWorkTimer.Packaging;

public enum InstallOutcome
{
    Installed,
    Updated,
    RecoveredPrevious,
    RolledBack,
    Rejected
}

public sealed record InstallResult(
    InstallOutcome Outcome,
    string? InstalledVersion,
    string StableEntryPath,
    string? ExecutablePath,
    string? FailureReason);

internal enum InstallBoundary
{
    AfterTransactionTempWrite,
    AfterTransactionJournalRename,
    AfterStableEntryTempWrite,
    AfterStableEntryRename,
    AfterStagedPayloadFileWrite,
    AfterPayloadManifestWrite,
    BeforeInactiveSlotDelete,
    AfterInactiveSlotDelete,
    AfterCandidateSlotRename,
    AfterStateTempWrite,
    AfterStateRename,
    AfterTransactionDelete
}

internal sealed class SimulatedInterruptionException : Exception;

public sealed class InstallManager
{
    private const string StableEntryName = "GaleWorkTimer.exe";
    private const string PayloadManifestName = ".gwt-package-manifest.json";
    private const string TransactionName = ".update-transaction.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string installRoot;
    private readonly string statePath;
    private readonly string transactionPath;
    private readonly Action<InstallBoundary>? checkpoint;
    private readonly Func<bool> applicationRunning;

    public InstallManager(string installRoot)
        : this(installRoot, null, null)
    {
    }

    internal InstallManager(string installRoot, Action<InstallBoundary>? checkpoint,
        Func<bool>? applicationRunning = null)
    {
        this.installRoot = Path.GetFullPath(installRoot);
        statePath = Path.Combine(this.installRoot, ".install-state.json");
        transactionPath = Path.Combine(this.installRoot, TransactionName);
        this.checkpoint = checkpoint;
        this.applicationRunning = applicationRunning ?? HasRunningApplication;
    }

    public string StableEntryPath => Path.Combine(installRoot, StableEntryName);

    public static string DefaultInstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "GaleWorkTimer");

    public InstallResult InstallPackage(string packagePath, string launcherPath)
    {
        string? stagingPath = null;
        try
        {
            var verified = ReadAndVerifyPackage(packagePath);
            Directory.CreateDirectory(installRoot);
            EnsureNotReparse(installRoot);
            var lockPath = Path.Combine(installRoot, ".install.lock");
            EnsureNotReparse(lockPath);
            using var installLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            if (applicationRunning())
                return Rejected("[APP_RUNNING] Закрой GaleWorkTimer перед установкой или обновлением.");
            var recovery = RecoverCore();
            if (recovery.Blocked)
                return Rejected(recovery.Reason ?? "[STATE_INVALID] Не удалось проверить текущую установку.");
            if (recovery.RecoveredPrevious)
                return Result(InstallOutcome.RecoveredPrevious, recovery.State, recovery.Reason);
            var current = recovery.State;
            if (current is not null && current.ManifestSha256 == verified.ManifestSha256)
            {
                EnsureStableEntry(launcherPath);
                var existingPath = PayloadPath(current.ActiveSlot);
                return new InstallResult(InstallOutcome.Updated, current.Version, StableEntryPath,
                    Path.Combine(existingPath, StableEntryName), null);
            }

            var targetSlot = current is null ? 0 : 1 - current.ActiveSlot;
            var targetPath = PayloadPath(targetSlot);
            EnsureNotReparse(targetPath);
            var transaction = new UpdateTransaction(
                current?.ActiveSlot,
                targetSlot,
                verified.Manifest.Version,
                verified.ManifestSha256);
            WriteAtomic(transactionPath, JsonSerializer.SerializeToUtf8Bytes(transaction, JsonOptions),
                InstallBoundary.AfterTransactionTempWrite, InstallBoundary.AfterTransactionJournalRename);
            EnsureStableEntry(launcherPath);

            stagingPath = Path.Combine(installRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            ExtractPackage(packagePath, stagingPath, verified, checkpoint);
            WriteDurably(Path.Combine(stagingPath, PayloadManifestName), verified.ManifestBytes);
            checkpoint?.Invoke(InstallBoundary.AfterPayloadManifestWrite);

            if (Directory.Exists(targetPath))
            {
                checkpoint?.Invoke(InstallBoundary.BeforeInactiveSlotDelete);
                DeleteDirectoryWithoutFollowingLinks(targetPath);
                checkpoint?.Invoke(InstallBoundary.AfterInactiveSlotDelete);
            }
            Directory.Move(stagingPath, targetPath);
            stagingPath = null;
            checkpoint?.Invoke(InstallBoundary.AfterCandidateSlotRename);

            var newState = new InstallState(
                targetSlot,
                current?.ActiveSlot,
                verified.Manifest.Version,
                verified.ManifestSha256);
            WriteAtomic(statePath, JsonSerializer.SerializeToUtf8Bytes(newState, JsonOptions),
                InstallBoundary.AfterStateTempWrite, InstallBoundary.AfterStateRename);
            File.Delete(transactionPath);
            checkpoint?.Invoke(InstallBoundary.AfterTransactionDelete);

            return new InstallResult(
                current is null ? InstallOutcome.Installed : InstallOutcome.Updated,
                newState.Version,
                StableEntryPath,
                Path.Combine(targetPath, StableEntryName),
                null);
        }
        catch (SimulatedInterruptionException)
        {
            throw;
        }
        catch (PackageValidationException exception)
        {
            if (File.Exists(transactionPath) || File.Exists(transactionPath + ".tmp"))
                return ResultAfterFailure(exception.Message);
            return Rejected(exception.Message);
        }
        catch (IOException)
        {
            return ResultAfterFailure("[INSTALL_IO] Не удалось безопасно изменить установку. Закрой программу и повтори попытку.");
        }
        catch (UnauthorizedAccessException)
        {
            return ResultAfterFailure("[INSTALL_ACCESS] Нет доступа к каталогу установки. Проверь права текущего пользователя.");
        }
        finally
        {
            if (stagingPath is not null && Directory.Exists(stagingPath))
                TryDeleteDirectory(stagingPath);
        }
    }

    public InstallResult Recover()
    {
        if (!Directory.Exists(installRoot))
            return Rejected("[NO_INSTALL] Завершённая установка не найдена.");
        try
        {
            EnsureNotReparse(installRoot);
            var lockPath = Path.Combine(installRoot, ".install.lock");
            EnsureNotReparse(lockPath);
            using var installLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            var recovery = RecoverCore();
            if (recovery.Blocked)
                return Rejected(recovery.Reason ?? "[STATE_INVALID] Не удалось проверить текущую установку.");
            if (recovery.State is null)
                return Rejected(recovery.Reason ?? "[NO_INSTALL] Завершённая установка не найдена.");
            return Result(recovery.RecoveredPrevious
                    ? InstallOutcome.RecoveredPrevious
                    : recovery.State.PreviousSlot is null ? InstallOutcome.Installed : InstallOutcome.Updated,
                recovery.State,
                recovery.Reason);
        }
        catch (IOException)
        {
            return Rejected("[INSTALL_BUSY] Другой установщик уже выполняет операцию. Повтори попытку позже.");
        }
        catch (UnauthorizedAccessException)
        {
            return Rejected("[INSTALL_ACCESS] Нет доступа к каталогу установки.");
        }
        catch (PackageValidationException exception)
        {
            return Rejected(exception.Message);
        }
    }

    public InstallResult LaunchStableEntry(string[] args)
    {
        if (!Directory.Exists(installRoot))
            return Rejected("[NO_INSTALL] Завершённая установка не найдена.");
        try
        {
            EnsureNotReparse(installRoot);
            var lockPath = Path.Combine(installRoot, ".install.lock");
            EnsureNotReparse(lockPath);
            using var installLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            var recovery = RecoverCore();
            if (recovery.Blocked || recovery.State is null)
                return Rejected(recovery.Reason ?? "[PAYLOAD_INVALID] Проверенная версия не найдена; приложение не запущено.");

            var executable = Path.Combine(PayloadPath(recovery.State.ActiveSlot), StableEntryName);
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            foreach (var argument in args)
                startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
            return Result(recovery.RecoveredPrevious
                    ? InstallOutcome.RecoveredPrevious
                    : recovery.State.PreviousSlot is null ? InstallOutcome.Installed : InstallOutcome.Updated,
                recovery.State,
                recovery.Reason);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Rejected("[LAUNCH_FAILED] Не удалось запустить проверенную версию приложения.");
        }
        catch (PackageValidationException exception)
        {
            return Rejected(exception.Message);
        }
    }

    private InstallResult Rejected(string reason) =>
        new(InstallOutcome.Rejected, null, StableEntryPath, null,
            reason.StartsWith('[') ? reason : "[INSTALL_REJECTED] " + reason);

    private string PayloadPath(int slot) => Path.Combine(installRoot, $"payload-{slot}");

    private InstallResult ResultAfterFailure(string reason)
    {
        var result = Recover();
        if (result.Outcome == InstallOutcome.RecoveredPrevious)
            return result with
            {
                Outcome = InstallOutcome.RolledBack,
                FailureReason = "[UPDATE_ROLLED_BACK] Обновление не завершено; восстановлена предыдущая проверенная версия. Причина: " + reason
            };
        if (result.Outcome is InstallOutcome.Installed or InstallOutcome.Updated)
            return result;
        return Rejected(reason);
    }

    private InstallResult Result(InstallOutcome outcome, InstallState? state, string? reason) =>
        new(outcome, state?.Version, StableEntryPath,
            state is null ? null : Path.Combine(PayloadPath(state.ActiveSlot), StableEntryName), reason);

    private RecoverySnapshot RecoverCore()
    {
        CleanupStagingDirectories();
        var transactionTempPath = transactionPath + ".tmp";
        var stateTempPath = statePath + ".tmp";
        var stableEntryTempPath = StableEntryPath + ".tmp";
        EnsureNotReparse(statePath);
        EnsureNotReparse(stateTempPath);
        EnsureNotReparse(transactionPath);
        EnsureNotReparse(transactionTempPath);
        EnsureNotReparse(stableEntryTempPath);
        var hasPendingFiles = File.Exists(transactionPath) || File.Exists(transactionTempPath) ||
                              File.Exists(stateTempPath) || File.Exists(stableEntryTempPath);
        var stateFileExists = File.Exists(statePath);
        var state = TryReadState();
        var transaction = TryReadTransaction(File.Exists(transactionPath) ? transactionPath : transactionTempPath);

        if (!hasPendingFiles)
        {
            if (!stateFileExists)
                return new RecoverySnapshot(null, false, false, null);
            if (state is null)
                return new RecoverySnapshot(null, false, true, "[STATE_CORRUPT] Состояние установки повреждено; приложение не запущено.");
            if (IsValidSlot(state.ActiveSlot, state))
                return new RecoverySnapshot(state, false, false, null);
            if (state.PreviousSlot is int previousSlot && IsValidSlot(previousSlot))
            {
                var recovered = StateForSlot(previousSlot, null);
                WriteAtomic(statePath, JsonSerializer.SerializeToUtf8Bytes(recovered, JsonOptions), null, null);
                return new RecoverySnapshot(recovered, true, false,
                    "[ACTIVE_CORRUPT] Текущая версия повреждена; восстановлена предыдущая проверенная версия.");
            }
            return new RecoverySnapshot(null, false, true, "[PAYLOAD_INVALID] Проверенная версия не найдена; приложение не запущено.");
        }

        if (transaction is not null && state is not null && state.ActiveSlot == transaction.TargetSlot &&
            state.Version == transaction.Version && state.ManifestSha256 == transaction.ManifestSha256 &&
            IsValidSlot(transaction.TargetSlot, state))
        {
            CleanupPendingFiles(transactionTempPath, stateTempPath);
            return new RecoverySnapshot(state, false, false, null);
        }

        if (transaction?.PreviousSlot is int journalPrevious && IsValidSlot(journalPrevious))
        {
            var recovered = state is not null && state.ActiveSlot == journalPrevious && IsValidSlot(journalPrevious, state)
                ? state
                : StateForSlot(journalPrevious, null);
            if (state is null || state.ActiveSlot != journalPrevious || state.Version != recovered.Version)
                WriteAtomic(statePath, JsonSerializer.SerializeToUtf8Bytes(recovered, JsonOptions), null, null);
            TryDeleteDirectory(PayloadPath(transaction.TargetSlot));
            CleanupPendingFiles(transactionTempPath, stateTempPath);
            return new RecoverySnapshot(recovered, true, false,
                "[UPDATE_INTERRUPTED] Обновление прервано; восстановлена предыдущая проверенная версия.");
        }

        if (state is not null && IsValidSlot(state.ActiveSlot, state))
        {
            CleanupPendingFiles(transactionTempPath, stateTempPath);
            return new RecoverySnapshot(state, true, false,
                "[UPDATE_INTERRUPTED] Обновление прервано; сохранена текущая проверенная версия.");
        }

        if (transaction is not null && transaction.PreviousSlot is null)
        {
            TryDeleteDirectory(PayloadPath(transaction.TargetSlot));
            CleanupPendingFiles(transactionTempPath, stateTempPath);
            return new RecoverySnapshot(null, false, false,
                "[INITIAL_INTERRUPTED] Первоначальная установка прервана; запусти установщик повторно.");
        }

        if (state is null && !stateFileExists)
        {
            CleanupPendingFiles(transactionTempPath, stateTempPath);
            return new RecoverySnapshot(null, false, false,
                "[INITIAL_INTERRUPTED] Первоначальная установка не завершена; запусти установщик повторно.");
        }

        return new RecoverySnapshot(null, false, true,
            "[PAYLOAD_INVALID] Не удалось подтвердить целостность установленной версии; приложение не запущено.");
    }

    private InstallState? TryReadState()
    {
        try
        {
            if (!File.Exists(statePath))
                return null;
            var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllBytes(statePath), JsonOptions);
            return state is not null && state.ActiveSlot is >= 0 and <= 1 &&
                   state.PreviousSlot is null or >= 0 and <= 1 && state.PreviousSlot != state.ActiveSlot &&
                   !string.IsNullOrWhiteSpace(state.Version) && IsSha256(state.ManifestSha256)
                ? state
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static UpdateTransaction? TryReadTransaction(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var transaction = JsonSerializer.Deserialize<UpdateTransaction>(File.ReadAllBytes(path), JsonOptions);
            return transaction is not null && transaction.TargetSlot is >= 0 and <= 1 &&
                   transaction.PreviousSlot is null or >= 0 and <= 1 &&
                   transaction.PreviousSlot != transaction.TargetSlot &&
                   !string.IsNullOrWhiteSpace(transaction.Version) && IsSha256(transaction.ManifestSha256)
                ? transaction
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool IsValidSlot(int slot, InstallState? expectedState = null)
    {
        try
        {
            var slotPath = PayloadPath(slot);
            if (!Directory.Exists(slotPath) ||
                (File.GetAttributes(slotPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            var manifestPath = Path.Combine(slotPath, PayloadManifestName);
            if (!File.Exists(manifestPath) ||
                (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, JsonOptions);
            ValidateManifest(manifest);
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            if (expectedState is not null &&
                (expectedState.Version != manifest!.Version || expectedState.ManifestSha256 != manifestHash))
                return false;

            var expected = manifest!.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var actual = EnumerateFilesWithoutLinks(slotPath)
                .Select(path => Path.GetRelativePath(slotPath, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(path => !path.Equals(PayloadManifestName, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(expected.Keys))
                return false;

            foreach (var file in manifest.Files)
            {
                var path = Path.Combine(slotPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
                using var stream = File.OpenRead(path);
                var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash),
                        Convert.FromHexString(file.Sha256)))
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or PackageValidationException or FormatException)
        {
            return false;
        }
    }

    private InstallState StateForSlot(int slot, int? previousSlot)
    {
        var bytes = File.ReadAllBytes(Path.Combine(PayloadPath(slot), PayloadManifestName));
        var manifest = JsonSerializer.Deserialize<PackageManifest>(bytes, JsonOptions)!;
        return new InstallState(slot, previousSlot, manifest.Version,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private void EnsureStableEntry(string launcherPath)
    {
        EnsureNotReparse(StableEntryPath);
        if (File.Exists(StableEntryPath))
        {
            if ((File.GetAttributes(StableEntryPath) & FileAttributes.ReparsePoint) != 0)
                throw new PackageValidationException("Стабильный путь установки занят небезопасной ссылкой.");
            return;
        }
        if (!File.Exists(launcherPath))
            throw new PackageValidationException("Файл установщика отсутствует.");
        EnsureNotReparse(launcherPath);
        var launcherTemp = StableEntryPath + ".tmp";
        EnsureNotReparse(launcherTemp);
        File.Copy(launcherPath, launcherTemp, overwrite: true);
        checkpoint?.Invoke(InstallBoundary.AfterStableEntryTempWrite);
        File.Move(launcherTemp, StableEntryPath, overwrite: true);
        checkpoint?.Invoke(InstallBoundary.AfterStableEntryRename);
    }

    private static void EnsureNotReparse(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new PackageValidationException("Путь установки содержит ссылку; операция отклонена.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static bool HasRunningApplication()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("GaleWorkTimer");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }

        try
        {
            return processes.Any(process => process.Id != Environment.ProcessId);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private void WriteAtomic(string path, byte[] bytes, InstallBoundary? afterTempWrite, InstallBoundary? afterRename)
    {
        var tempPath = path + ".tmp";
        EnsureNotReparse(path);
        EnsureNotReparse(tempPath);
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (afterTempWrite is not null)
            checkpoint?.Invoke(afterTempWrite.Value);
        File.Move(tempPath, path, overwrite: true);
        if (afterRename is not null)
            checkpoint?.Invoke(afterRename.Value);
    }

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void CleanupStagingDirectories()
    {
        foreach (var path in Directory.EnumerateDirectories(installRoot, ".staging-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    TryDeleteDirectory(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void CleanupPendingFiles(string transactionTempPath, string stateTempPath)
    {
        TryDeleteFile(transactionPath);
        TryDeleteFile(transactionTempPath);
        TryDeleteFile(stateTempPath);
        TryDeleteFile(StableEntryPath + ".tmp");
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
                DeleteDirectoryWithoutFollowingLinks(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteDirectoryWithoutFollowingLinks(string root)
    {
        if (File.Exists(root) && (File.GetAttributes(root) & FileAttributes.Directory) == 0)
        {
            File.Delete(root);
            return;
        }

        var pending = new Stack<(string Path, bool VisitChildren)>();
        pending.Push((root, false));
        while (pending.TryPop(out var item))
        {
            var attributes = File.GetAttributes(item.Path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                    Directory.Delete(item.Path);
                else
                    File.Delete(item.Path);
                continue;
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                File.Delete(item.Path);
                continue;
            }
            if (item.VisitChildren)
            {
                Directory.Delete(item.Path);
                continue;
            }

            pending.Push((item.Path, true));
            foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path))
                pending.Push((entry, false));
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new PackageValidationException("Установленный payload содержит ссылку.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
                else
                    yield return path;
            }
        }
    }

    private sealed record RecoverySnapshot(InstallState? State, bool RecoveredPrevious, bool Blocked, string? Reason);

    private static VerifiedPackage ReadAndVerifyPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new PackageValidationException("Файл пакета не найден.");

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntries = archive.Entries.Where(entry => entry.FullName == "manifest.json").ToArray();
            if (manifestEntries.Length != 1 || manifestEntries[0].Length > 4 * 1024 * 1024)
                throw new PackageValidationException("Пакет не содержит корректный manifest.json.");

            var manifestEntry = manifestEntries[0];
            ValidateArchiveEntry(manifestEntry);
            using var manifestStream = manifestEntry.Open();
            using var manifestBuffer = new MemoryStream();
            manifestStream.CopyTo(manifestBuffer);
            var manifestBytes = manifestBuffer.ToArray();
            var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, JsonOptions);
            ValidateManifest(manifest);

            var filesByPath = manifest!.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var archiveFiles = archive.Entries.Where(entry => entry.FullName != "manifest.json").ToArray();
            if (archiveFiles.Length != filesByPath.Count)
                throw new PackageValidationException("Состав архива не совпадает с manifest.json.");

            long totalBytes = 0;
            foreach (var entry in archiveFiles)
            {
                if (!filesByPath.TryGetValue(entry.FullName, out var file) || entry.FullName != file.Path)
                    throw new PackageValidationException("В архиве обнаружен незаявленный или небезопасный файл.");
                ValidateArchiveEntry(entry);
                totalBytes = checked(totalBytes + entry.Length);
                if (totalBytes > 1024L * 1024 * 1024)
                    throw new PackageValidationException("Размер распакованного пакета превышает безопасный предел.");

                using var entryStream = entry.Open();
                var digest = Convert.ToHexString(SHA256.HashData(entryStream)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(digest), Convert.FromHexString(file.Sha256)))
                    throw new PackageValidationException("Контрольная сумма файла пакета не совпала.");
            }

            return new VerifiedPackage(manifest!, manifestBytes,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
        }
        catch (PackageValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException or
                                             OverflowException or NotSupportedException or ArgumentException)
        {
            throw new PackageValidationException("Архив повреждён или имеет неподдерживаемый формат.");
        }
    }

    private static void ValidateManifest(PackageManifest? manifest)
    {
        if (manifest is null || !Version.TryParse(manifest.Version, out _) ||
            manifest.Files is null || manifest.Files.Count == 0)
            throw new PackageValidationException("Manifest пакета неполон или содержит неверную версию.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null || !IsSafeRelativePath(file.Path) ||
                file.Path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                file.Path.Equals(PayloadManifestName, StringComparison.OrdinalIgnoreCase) ||
                !IsSha256(file.Sha256) || !names.Add(file.Path))
                throw new PackageValidationException("Manifest содержит повторный или небезопасный путь.");
        }

        foreach (var required in new[]
                 { StableEntryName, "GaleWorkTimer.dll", "GaleWorkTimer.deps.json", "GaleWorkTimer.runtimeconfig.json" })
        {
            if (!names.Contains(required))
                throw new PackageValidationException("В пакете отсутствует обязательный файл приложения.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') ||
            path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) ||
            path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || Path.IsPathRooted(path))
            return false;

        var segments = path.Split('/');
        return segments.All(segment =>
            segment.Length > 0 && segment is not "." and not ".." &&
            segment.Length <= 255 &&
            !segment.EndsWith(' ') && !segment.EndsWith('.') &&
            !IsReservedWindowsName(segment));
    }

    internal static bool IsSafePackagePath(string path) => IsSafeRelativePath(path);

    internal static PackageBuildResult VerifyPackageArchive(string packagePath)
    {
        var verified = ReadAndVerifyPackage(packagePath);
        using var stream = File.OpenRead(packagePath);
        return new PackageBuildResult(verified.Manifest.Version, verified.Manifest.Files.Count,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = segment.Split('.')[0];
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        var dosAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (entry.FullName.EndsWith('/') || unixType == 0xA000 ||
            (dosAttributes & FileAttributes.ReparsePoint) != 0)
            throw new PackageValidationException("Пакет содержит ссылку или запись не-файлового типа.");
    }

    private static void ExtractPackage(string packagePath, string stagingPath, VerifiedPackage verified,
        Action<InstallBoundary>? checkpoint)
    {
        Directory.CreateDirectory(stagingPath);
        EnsureNotReparse(stagingPath);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null || archive.Entries.Count != verified.Manifest.Files.Count + 1)
            throw new PackageValidationException("Состав архива изменился после проверки.");
        ValidateArchiveEntry(manifestEntry);
        using (var manifestStream = manifestEntry.Open())
        using (var manifestBuffer = new MemoryStream())
        {
            manifestStream.CopyTo(manifestBuffer);
            if (!manifestBuffer.ToArray().AsSpan().SequenceEqual(verified.ManifestBytes))
                throw new PackageValidationException("Manifest изменился после проверки.");
        }

        foreach (var file in verified.Manifest.Files)
        {
            var entry = archive.GetEntry(file.Path)
                ?? throw new PackageValidationException("Пакет неполон.");
            ValidateArchiveEntry(entry);
            var target = Path.GetFullPath(Path.Combine(stagingPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new PackageValidationException("Путь пакета выходит за временный каталог.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                output.Write(buffer, 0, count);
                hash.AppendData(buffer, 0, count);
            }
            output.Flush(flushToDisk: true);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual), Convert.FromHexString(file.Sha256)))
                throw new PackageValidationException("Контрольная сумма файла пакета не совпала.");
            checkpoint?.Invoke(InstallBoundary.AfterStagedPayloadFileWrite);
        }
    }

    private sealed record PackageManifest(string Version, List<PackageFile> Files);
    private sealed record PackageFile(string Path, string Sha256);
    private sealed record InstallState(int ActiveSlot, int? PreviousSlot, string Version, string ManifestSha256);
    private sealed record UpdateTransaction(int? PreviousSlot, int TargetSlot, string Version, string ManifestSha256);
    private sealed record VerifiedPackage(PackageManifest Manifest, byte[] ManifestBytes, string ManifestSha256);

    private sealed class PackageValidationException(string message) : Exception("[PACKAGE_INVALID] " + message);
}
