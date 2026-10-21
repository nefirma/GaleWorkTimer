using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GaleWorkTimer.Packaging;
using Xunit;

namespace GaleWorkTimer.Packaging.Tests;

public sealed class InstallManagerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-Packaging-" + Guid.NewGuid());
    private readonly string installRoot;
    private readonly string dataRoot;
    private readonly string launcher;

    public InstallManagerTests()
    {
        installRoot = Path.Combine(directory, "Programs", "GaleWorkTimer");
        dataRoot = Path.Combine(directory, "NeFirma", "GaleWorkTimer");
        Directory.CreateDirectory(dataRoot);
        launcher = Path.Combine(directory, "GaleWorkTimer-Setup.exe");
        File.WriteAllBytes(launcher, "launcher"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dataRoot, "data.db"), "database-sentinel"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dataRoot, "data.db-wal"), "wal-sentinel"u8.ToArray());
        File.WriteAllBytes(Path.Combine(dataRoot, "data.db-shm"), "shm-sentinel"u8.ToArray());
    }

    [Fact]
    public void Install_and_update_select_verified_payload_at_one_stable_entry_without_touching_data()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var manager = new InstallManager(installRoot);

        var installed = manager.InstallPackage(packageA, launcher);
        var stableEntry = Path.Combine(installRoot, "GaleWorkTimer.exe");
        Assert.Equal(InstallOutcome.Installed, installed.Outcome);
        Assert.Equal("1.0.0", installed.InstalledVersion);
        Assert.Equal(stableEntry, installed.StableEntryPath);
        Assert.Equal("launcher"u8.ToArray(), File.ReadAllBytes(stableEntry));
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(installed.ExecutablePath!));

        var updated = manager.InstallPackage(packageB, launcher);
        Assert.Equal(InstallOutcome.Updated, updated.Outcome);
        Assert.Equal("2.0.0", updated.InstalledVersion);
        Assert.Equal(stableEntry, updated.StableEntryPath);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(updated.ExecutablePath!));

        var repeated = manager.InstallPackage(packageB, launcher);
        Assert.Equal(InstallOutcome.Updated, repeated.Outcome);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(repeated.ExecutablePath!));
        Assert.Equal(2, Directory.GetDirectories(installRoot, "payload-*", SearchOption.TopDirectoryOnly).Length);
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [InlineData(false, "INSTALL_IO")]
    [InlineData(true, "INSTALL_ACCESS")]
    public void Pre_transaction_failure_rejects_update_and_preserves_installed_payload_and_data(
        bool unauthorized, string expectedReason)
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var packageB = CreatePackage("1.0.0", "payload-B"u8.ToArray(), packageFileName: "candidate-same-version.zip");
        var dataBefore = CaptureDataSentinels();
        var failing = new InstallManager(installRoot, checkpoint: null, applicationRunning: () =>
            throw (unauthorized ? new UnauthorizedAccessException() : new IOException()));

        var result = failing.InstallPackage(packageB, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Contains(expectedReason, result.FailureReason!, StringComparison.Ordinal);
        Assert.Null(result.InstalledVersion);
        Assert.False(File.Exists(Path.Combine(installRoot, ".update-transaction.json")));
        var installed = new InstallManager(installRoot).Recover();
        Assert.Equal("1.0.0", installed.InstalledVersion);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(installed.ExecutablePath!));
        Assert.Equal(baseline.StableEntryPath, result.StableEntryPath);
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Failure_before_transaction_journal_rename_is_rejected_not_rolled_back()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var failing = new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.AfterTransactionTempWrite)
                throw new IOException("simulated failure before journal rename");
        });

        var result = failing.InstallPackage(packageB, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Contains("INSTALL_IO", result.FailureReason!, StringComparison.Ordinal);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        Assert.Equal(baseline.StableEntryPath, result.StableEntryPath);
        Assert.False(File.Exists(Path.Combine(installRoot, ".update-transaction.json")));
        Assert.False(File.Exists(Path.Combine(installRoot, ".update-transaction.json.tmp")));
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [MemberData(nameof(UpdateCrashCases))]
    public async Task Abrupt_update_interruption_at_each_persistent_boundary_recovers_through_stable_entry(
        string boundaryName, int stagedWriteNumber, string expectedVersion, string expectedPayload, bool committed)
    {
        CrashChildIfRequested();
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
        Assert.Equal(InstallOutcome.Installed, baseline.Outcome);
        var updated = new InstallManager(installRoot).InstallPackage(packageB, launcher);
        Assert.Equal(InstallOutcome.Updated, updated.Outcome);
        var stableEntryBytes = File.ReadAllBytes(baseline.StableEntryPath);

        await RunAbruptInstallChild(nameof(Abrupt_update_interruption_at_each_persistent_boundary_recovers_through_stable_entry),
            boundaryName, packageC, stagedWriteNumber);

        var started = new List<ProcessStartInfo>();
        var result = new InstallManager(installRoot, checkpoint: null, startProcess: started.Add)
            .LaunchStableEntry([]);
        var expectedOutcome = committed ? InstallOutcome.Updated : InstallOutcome.RecoveredPrevious;

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedVersion, result.InstalledVersion);
        Assert.Equal(committed, result.FailureReason is null);
        if (!committed)
        {
            Assert.Contains("UPDATE_INTERRUPTED", result.FailureReason!, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(baseline.StableEntryPath, result.StableEntryPath);
        Assert.Equal(stableEntryBytes, File.ReadAllBytes(result.StableEntryPath));
        Assert.Equal(Path.Combine(installRoot, expectedVersion == "2.0.0" ? "payload-1" : "payload-0", "GaleWorkTimer.exe"),
            started.Single().FileName);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expectedPayload), File.ReadAllBytes(started.Single().FileName));
        Assert.Empty(Directory.GetDirectories(installRoot, ".staging-*", SearchOption.TopDirectoryOnly));
        AssertDataSentinels(dataBefore);
    }

    public static IEnumerable<object[]> UpdateCrashCases()
    {
        foreach (var boundary in new[]
                 {
                     InstallBoundary.AfterTransactionTempWrite,
                     InstallBoundary.AfterTransactionJournalRename,
                     InstallBoundary.AfterPayloadManifestWrite,
                     InstallBoundary.BeforeInactiveSlotDelete,
                     InstallBoundary.AfterInactiveSlotDelete,
                     InstallBoundary.AfterCandidateSlotRename,
                     InstallBoundary.AfterStateTempWrite
                 })
            yield return [boundary.ToString(), 0, "2.0.0", "payload-B", false];

        for (var writeNumber = 1; writeNumber <= 4; writeNumber++)
            yield return [InstallBoundary.AfterStagedPayloadFileWrite.ToString(), writeNumber,
                "2.0.0", "payload-B", false];

        yield return [InstallBoundary.AfterStateRename.ToString(), 0, "3.0.0", "payload-C", true];
        yield return [InstallBoundary.AfterTransactionDelete.ToString(), 0, "3.0.0", "payload-C", true];
    }

    [Fact]
    public void Interrupted_update_recovers_previous_verified_payload_and_reports_a_safe_reason()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var stableEntryBytes = File.ReadAllBytes(baseline.StableEntryPath);
        Assert.Equal(InstallOutcome.Installed, baseline.Outcome);

        var interrupted = new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.AfterCandidateSlotRename)
                throw new SimulatedInterruptionException();
        });
        Assert.Throws<SimulatedInterruptionException>(() => interrupted.InstallPackage(packageB, launcher));

        var recovered = new InstallManager(installRoot).Recover();
        Assert.Equal(InstallOutcome.RecoveredPrevious, recovered.Outcome);
        Assert.Equal("1.0.0", recovered.InstalledVersion);
        Assert.Equal(baseline.StableEntryPath, recovered.StableEntryPath);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(recovered.ExecutablePath!));
        Assert.Contains("прерван", recovered.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directory, recovered.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stableEntryBytes, File.ReadAllBytes(recovered.StableEntryPath));
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [InlineData(nameof(InstallBoundary.AfterTransactionTempWrite), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterTransactionJournalRename), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterPayloadManifestWrite), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterInactiveSlotDelete), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterCandidateSlotRename), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterStateTempWrite), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterStateRename), "3.0.0", "payload-C")]
    [InlineData(nameof(InstallBoundary.AfterTransactionDelete), "3.0.0", "payload-C")]
    public void Recovery_selects_last_committed_payload_at_each_persistent_boundary(
        string interruptedAtName, string expectedVersion, string expectedPayload)
    {
        var interruptedAt = Enum.Parse<InstallBoundary>(interruptedAtName);
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var stableEntryBytes = File.ReadAllBytes(baseline.StableEntryPath);
        new InstallManager(installRoot).InstallPackage(packageB, launcher);

        var interrupted = new InstallManager(installRoot, boundary =>
        {
            if (boundary == interruptedAt)
                throw new SimulatedInterruptionException();
        });
        Assert.Throws<SimulatedInterruptionException>(() => interrupted.InstallPackage(packageC, launcher));

        var recovered = new InstallManager(installRoot).Recover();
        Assert.Equal(expectedVersion, recovered.InstalledVersion);
        Assert.Equal(expectedPayload, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(recovered.ExecutablePath!)));
        if (expectedVersion == "2.0.0")
            Assert.Equal(InstallOutcome.RecoveredPrevious, recovered.Outcome);
        else
            Assert.Equal(InstallOutcome.Updated, recovered.Outcome);
        Assert.Equal(stableEntryBytes, File.ReadAllBytes(baseline.StableEntryPath));
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [InlineData(nameof(InstallBoundary.AfterStableEntryTempWrite))]
    [InlineData(nameof(InstallBoundary.AfterStableEntryRename))]
    public void Interrupted_initial_stable_entry_write_leaves_no_launchable_partial_payload(string boundaryName)
    {
        var boundary = Enum.Parse<InstallBoundary>(boundaryName);
        var package = CreatePackage("1.0.0", "partial"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        var interrupted = new InstallManager(installRoot, point =>
        {
            if (point == boundary)
                throw new SimulatedInterruptionException();
        });

        Assert.Throws<SimulatedInterruptionException>(() => interrupted.InstallPackage(package, launcher));
        var recovered = new InstallManager(installRoot).Recover();

        Assert.Equal(InstallOutcome.Rejected, recovered.Outcome);
        Assert.Null(recovered.ExecutablePath);
        Assert.False(File.Exists(Path.Combine(installRoot, ".install-state.json")));
        Assert.Empty(Directory.GetDirectories(installRoot, "payload-*", SearchOption.TopDirectoryOnly));
        Assert.Contains("прерв", recovered.FailureReason!, StringComparison.OrdinalIgnoreCase);
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Interruption_after_each_staged_payload_file_write_recovers_previous_payload(int writeNumber)
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        new InstallManager(installRoot).InstallPackage(packageB, launcher);
        var writes = 0;
        var interrupted = new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.AfterStagedPayloadFileWrite && ++writes == writeNumber)
                throw new SimulatedInterruptionException();
        });

        Assert.Throws<SimulatedInterruptionException>(() => interrupted.InstallPackage(packageC, launcher));
        var recovered = new InstallManager(installRoot).Recover();

        Assert.Equal(InstallOutcome.RecoveredPrevious, recovered.Outcome);
        Assert.Equal("2.0.0", recovered.InstalledVersion);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(recovered.ExecutablePath!));
        Assert.Contains("прерван", recovered.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(installRoot, ".staging-*", SearchOption.TopDirectoryOnly));
        AssertDataSentinels(dataBefore);
    }

    [Theory]
    [MemberData(nameof(InitialCrashCases))]
    public async Task Abrupt_initial_install_interruption_recovers_through_stable_entry_and_allows_retry(
        string boundaryName, int stagedWriteNumber, bool committed, bool stableEntryExpected)
    {
        CrashChildIfRequested();
        var package = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var dataBefore = CaptureDataSentinels();

        await RunAbruptInstallChild(nameof(Abrupt_initial_install_interruption_recovers_through_stable_entry_and_allows_retry),
            boundaryName, package, stagedWriteNumber);

        Assert.Equal(stableEntryExpected, File.Exists(Path.Combine(installRoot, "GaleWorkTimer.exe")));
        var started = new List<ProcessStartInfo>();
        var launch = new InstallManager(installRoot, checkpoint: null, startProcess: started.Add)
            .LaunchStableEntry([]);

        if (committed)
        {
            Assert.Equal(InstallOutcome.Installed, launch.Outcome);
            Assert.Equal("1.0.0", launch.InstalledVersion);
            Assert.Null(launch.FailureReason);
            Assert.Single(started);
            Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(started.Single().FileName));
        }
        else
        {
            Assert.Equal(InstallOutcome.Rejected, launch.Outcome);
            Assert.Null(launch.InstalledVersion);
            Assert.Null(launch.ExecutablePath);
            Assert.Empty(started);
            Assert.Contains("INITIAL_INTERRUPTED", launch.FailureReason!, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, launch.FailureReason!, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(installRoot, ".install-state.json")));
            Assert.Empty(Directory.GetDirectories(installRoot, "payload-*", SearchOption.TopDirectoryOnly));
        }

        Assert.Empty(Directory.GetDirectories(installRoot, ".staging-*", SearchOption.TopDirectoryOnly));
        var retry = new InstallManager(installRoot).InstallPackage(package, launcher);
        Assert.Equal(committed ? InstallOutcome.Updated : InstallOutcome.Installed, retry.Outcome);
        Assert.Equal("1.0.0", retry.InstalledVersion);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(retry.ExecutablePath!));
        Assert.Equal(1, Directory.GetDirectories(installRoot, "payload-*", SearchOption.TopDirectoryOnly).Length);
        Assert.Equal("launcher"u8.ToArray(), File.ReadAllBytes(retry.StableEntryPath));
        AssertDataSentinels(dataBefore);
    }

    public static IEnumerable<object[]> InitialCrashCases()
    {
        yield return [InstallBoundary.AfterTransactionTempWrite.ToString(), 0, false, false];
        yield return [InstallBoundary.AfterTransactionJournalRename.ToString(), 0, false, false];
        yield return [InstallBoundary.AfterStableEntryTempWrite.ToString(), 0, false, false];
        yield return [InstallBoundary.AfterStableEntryRename.ToString(), 0, false, true];
        for (var writeNumber = 1; writeNumber <= 4; writeNumber++)
            yield return [InstallBoundary.AfterStagedPayloadFileWrite.ToString(), writeNumber, false, true];
        yield return [InstallBoundary.AfterPayloadManifestWrite.ToString(), 0, false, true];
        yield return [InstallBoundary.AfterCandidateSlotRename.ToString(), 0, false, true];
        yield return [InstallBoundary.AfterStateTempWrite.ToString(), 0, false, true];
        yield return [InstallBoundary.AfterStateRename.ToString(), 0, true, true];
        yield return [InstallBoundary.AfterTransactionDelete.ToString(), 0, true, true];
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("nested\\escape.txt")]
    [InlineData("CON.txt")]
    [InlineData("bad?.txt")]
    public void Unsafe_archive_paths_are_rejected_without_changing_installed_payload(string unsafePath)
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var before = File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe"));
        var invalid = CreatePackage("2.0.0", "payload-B"u8.ToArray(), extraPath: unsafePath);

        var result = new InstallManager(installRoot).InstallPackage(invalid, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        Assert.False(File.Exists(Path.Combine(installRoot, "escaped.txt")));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Symlink_archive_entry_is_rejected_before_switching()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var invalid = CreatePackage("2.0.0", "payload-B"u8.ToArray(), extraPath: "linked.txt",
            extraAttributes: unchecked((int)0xA1FF0000));

        var result = new InstallManager(installRoot).InstallPackage(invalid, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Invalid_manifest_file_list_and_integrity_are_rejected()
    {
        var dataBefore = CaptureDataSentinels();
        var missing = CreatePackage("2.0.0", "payload-B"u8.ToArray(), omitRuntimeConfig: true);
        var badHash = CreatePackage("3.0.0", "payload-C"u8.ToArray(), tamperHash: true);

        Assert.Equal(InstallOutcome.Rejected, new InstallManager(installRoot).InstallPackage(missing, launcher).Outcome);
        Assert.Equal(InstallOutcome.Rejected, new InstallManager(installRoot).InstallPackage(badHash, launcher).Outcome);
        Assert.False(Directory.Exists(installRoot));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Occupied_inactive_slot_rolls_back_without_touching_active_payload_or_data()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        new InstallManager(installRoot).InstallPackage(packageB, launcher);

        var blocked = new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.BeforeInactiveSlotDelete)
                throw new IOException("simulated Windows sharing violation");
        }).InstallPackage(packageC, launcher);

        Assert.Equal(InstallOutcome.RolledBack, blocked.Outcome);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-1", "GaleWorkTimer.exe")));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Running_application_rejects_update_before_any_payload_or_data_mutation()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var payloadBefore = File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe"));
        var dataBefore = CaptureDataSentinels();

        var blocked = new InstallManager(installRoot, checkpoint: null, applicationRunning: () => true)
            .InstallPackage(packageB, launcher);

        Assert.Equal(InstallOutcome.Rejected, blocked.Outcome);
        Assert.Contains("APP_RUNNING", blocked.FailureReason!, StringComparison.Ordinal);
        Assert.Equal(payloadBefore, File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, "payload-1")));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public async Task Concurrent_updater_is_rejected_while_first_transaction_holds_the_install_lock()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        var dataBefore = CaptureDataSentinels();
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var first = Task.Run(() => new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.AfterTransactionJournalRename)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("test release timed out");
            }
        }).InstallPackage(packageB, launcher));

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var concurrent = new InstallManager(installRoot).InstallPackage(packageC, launcher);
        release.Set();
        var completed = await first;

        Assert.Equal(InstallOutcome.Rejected, concurrent.Outcome);
        Assert.Equal(InstallOutcome.Updated, completed.Outcome);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-1", "GaleWorkTimer.exe")));
        AssertDataSentinels(dataBefore);
    }

    [Fact]
    public void Package_builder_produces_repeatable_verified_zip_of_the_complete_publish_tree()
    {
        var dataBefore = CaptureDataSentinels();
        var publishRoot = Path.Combine(directory, "publish");
        Directory.CreateDirectory(Path.Combine(publishRoot, "runtimes", "win-x64"));
        File.WriteAllBytes(Path.Combine(publishRoot, "GaleWorkTimer.exe"), "apphost"u8.ToArray());
        File.WriteAllBytes(Path.Combine(publishRoot, "GaleWorkTimer.dll"), "app"u8.ToArray());
        File.WriteAllBytes(Path.Combine(publishRoot, "GaleWorkTimer.deps.json"), "deps"u8.ToArray());
        File.WriteAllBytes(Path.Combine(publishRoot, "GaleWorkTimer.runtimeconfig.json"), "runtime"u8.ToArray());
        File.WriteAllBytes(Path.Combine(publishRoot, "runtimes", "win-x64", "native.dll"), "native"u8.ToArray());
        var firstPath = Path.Combine(directory, "first.zip");
        var secondPath = Path.Combine(directory, "second.zip");

        var first = PackageBuilder.CreatePackage(publishRoot, firstPath, "2.6.0");
        var second = PackageBuilder.CreatePackage(publishRoot, secondPath, "2.6.0");
        var verified = PackageBuilder.VerifyPackage(firstPath);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.Sha256, verified.Sha256);
        Assert.Equal(first.Sha256, PackageBuilder.ComputeFileSha256(firstPath));
        Assert.Equal("2.6.0", verified.Version);
        Assert.Equal(5, verified.FileCount);
        var installed = new InstallManager(installRoot).InstallPackage(firstPath, launcher);
        Assert.Equal(InstallOutcome.Installed, installed.Outcome);
        Assert.Equal("native"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot,
            "payload-0", "runtimes", "win-x64", "native.dll")));
        AssertDataSentinels(dataBefore);
    }

    private string CreatePackage(string version, byte[] executable, string? extraPath = null,
        int? extraAttributes = null, bool tamperHash = false, bool omitRuntimeConfig = false,
        string? packageFileName = null)
    {
        var path = Path.Combine(directory, packageFileName ?? version + ".zip");
        var files = new Dictionary<string, byte[]>
        {
            ["GaleWorkTimer.exe"] = executable,
            ["GaleWorkTimer.dll"] = "app"u8.ToArray(),
            ["GaleWorkTimer.deps.json"] = "{}"u8.ToArray(),
            ["GaleWorkTimer.runtimeconfig.json"] = "{}"u8.ToArray()
        };
        if (omitRuntimeConfig)
            files.Remove("GaleWorkTimer.runtimeconfig.json");
        if (extraPath is not null)
            files[extraPath] = "untrusted-extra"u8.ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version,
            files = files.Select(pair => new
            {
                path = pair.Key,
                sha256 = tamperHash && pair.Key == "GaleWorkTimer.dll"
                    ? new string('0', 64)
                    : Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant()
            })
        });
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files.Append(new KeyValuePair<string, byte[]>("manifest.json", manifest)))
        {
            var zipEntry = archive.CreateEntry(name);
            if (name == extraPath && extraAttributes is not null)
                zipEntry.ExternalAttributes = extraAttributes.Value;
            using var entry = zipEntry.Open();
            entry.Write(bytes);
        }
        return path;
    }

    private const string CrashChildKey = "GWT_PACKAGING_CRASH_CHILD";
    private const string CrashInstallRootKey = "GWT_PACKAGING_CRASH_INSTALL_ROOT";
    private const string CrashPackagePathKey = "GWT_PACKAGING_CRASH_PACKAGE_PATH";
    private const string CrashLauncherPathKey = "GWT_PACKAGING_CRASH_LAUNCHER_PATH";
    private const string CrashBoundaryKey = "GWT_PACKAGING_CRASH_BOUNDARY";
    private const string CrashWriteNumberKey = "GWT_PACKAGING_CRASH_WRITE_NUMBER";
    private const string CrashMarkerPathKey = "GWT_PACKAGING_CRASH_MARKER_PATH";

    private void CrashChildIfRequested()
    {
        if (Environment.GetEnvironmentVariable(CrashChildKey) != "1")
            return;

        var boundary = Enum.Parse<InstallBoundary>(Environment.GetEnvironmentVariable(CrashBoundaryKey)!);
        var writeNumber = int.Parse(Environment.GetEnvironmentVariable(CrashWriteNumberKey)!);
        var markerPath = Environment.GetEnvironmentVariable(CrashMarkerPathKey)!;
        var stagedWrites = 0;
        var manager = new InstallManager(Environment.GetEnvironmentVariable(CrashInstallRootKey)!, point =>
        {
            if (point != boundary)
                return;
            if (boundary == InstallBoundary.AfterStagedPayloadFileWrite && ++stagedWrites != writeNumber)
                return;
            File.WriteAllText(markerPath, boundary == InstallBoundary.AfterStagedPayloadFileWrite
                ? $"{boundary}:{stagedWrites}"
                : boundary.ToString());
            Environment.FailFast("GWT_TEST_ABRUPT_EXIT");
        }, applicationRunning: () => false);
        manager.InstallPackage(Environment.GetEnvironmentVariable(CrashPackagePathKey)!,
            Environment.GetEnvironmentVariable(CrashLauncherPathKey)!);
        throw new InvalidOperationException("The configured abrupt termination boundary was not reached.");
    }

    private async Task RunAbruptInstallChild(string testName, string boundary, string packagePath,
        int stagedWriteNumber = 0)
    {
        var assemblyDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var projectDirectory = assemblyDirectory.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException("Test project directory was not found.");
        var projectPath = Path.Combine(projectDirectory.FullName, "GaleWorkTimer.Packaging.Tests.csproj");
        var configuration = assemblyDirectory.Parent!.Name;
        var markerPath = Path.Combine(directory, "abrupt-exit-marker");
        var startInfo = new ProcessStartInfo(DotnetHostPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add("FullyQualifiedName=GaleWorkTimer.Packaging.Tests.InstallManagerTests." + testName);
        startInfo.Environment[CrashChildKey] = "1";
        startInfo.Environment[CrashInstallRootKey] = installRoot;
        startInfo.Environment[CrashPackagePathKey] = packagePath;
        startInfo.Environment[CrashLauncherPathKey] = launcher;
        startInfo.Environment[CrashBoundaryKey] = boundary;
        startInfo.Environment[CrashWriteNumberKey] = stagedWriteNumber.ToString();
        startInfo.Environment[CrashMarkerPathKey] = markerPath;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Abrupt termination child process did not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromMinutes(2))) != exitTask)
        {
            process.Kill(entireProcessTree: true);
            await exitTask;
            throw new TimeoutException("Abrupt termination child process did not exit.");
        }
        await Task.WhenAll(outputTask, errorTask);

        Assert.NotEqual(0, process.ExitCode);
        Assert.True(File.Exists(markerPath), "The child did not reach the configured persistent boundary.");
        var expectedMarker = stagedWriteNumber == 0 ? boundary : $"{boundary}:{stagedWriteNumber}";
        Assert.Equal(expectedMarker, File.ReadAllText(markerPath));
        Assert.Contains("GWT_TEST_ABRUPT_EXIT", outputTask.Result + errorTask.Result, StringComparison.Ordinal);
    }

    private static string DotnetHostPath()
    {
        var runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent?.FullName;
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty, executable),
            Path.Combine(runtimeRoot ?? string.Empty, executable)
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? executable;
    }

    private Dictionary<string, byte[]> CaptureDataSentinels() =>
        Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);

    private void AssertDataSentinels(Dictionary<string, byte[]> expected)
    {
        var actual = CaptureDataSentinels();
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (name, bytes) in expected)
            Assert.Equal(bytes, actual[name]);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
