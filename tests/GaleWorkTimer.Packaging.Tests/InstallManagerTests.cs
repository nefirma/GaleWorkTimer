using System.IO.Compression;
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
        var dataBefore = Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
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
        Assert.Equal(dataBefore, Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes));
    }

    [Fact]
    public void Interrupted_update_recovers_previous_verified_payload_and_reports_a_safe_reason()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var baseline = new InstallManager(installRoot).InstallPackage(packageA, launcher);
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
    }

    [Theory]
    [InlineData(nameof(InstallBoundary.AfterTransactionTempWrite), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterTransactionJournalRename), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterStableEntryTempWrite), "2.0.0", "payload-B")]
    [InlineData(nameof(InstallBoundary.AfterStableEntryRename), "2.0.0", "payload-B")]
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
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        new InstallManager(installRoot).InstallPackage(packageB, launcher);
        File.Delete(Path.Combine(installRoot, "GaleWorkTimer.exe"));

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
    }

    [Theory]
    [InlineData(nameof(InstallBoundary.AfterStableEntryTempWrite))]
    [InlineData(nameof(InstallBoundary.AfterStableEntryRename))]
    public void Interrupted_initial_stable_entry_write_leaves_no_launchable_partial_payload(string boundaryName)
    {
        var boundary = Enum.Parse<InstallBoundary>(boundaryName);
        var package = CreatePackage("1.0.0", "partial"u8.ToArray());
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
    }

    [Theory]
    [InlineData(nameof(InstallBoundary.AfterTransactionTempWrite), 0, false)]
    [InlineData(nameof(InstallBoundary.AfterTransactionJournalRename), 0, false)]
    [InlineData(nameof(InstallBoundary.AfterStagedPayloadFileWrite), 1, false)]
    [InlineData(nameof(InstallBoundary.AfterStagedPayloadFileWrite), 2, false)]
    [InlineData(nameof(InstallBoundary.AfterStagedPayloadFileWrite), 3, false)]
    [InlineData(nameof(InstallBoundary.AfterStagedPayloadFileWrite), 4, false)]
    [InlineData(nameof(InstallBoundary.AfterPayloadManifestWrite), 0, false)]
    [InlineData(nameof(InstallBoundary.AfterCandidateSlotRename), 0, false)]
    [InlineData(nameof(InstallBoundary.AfterStateTempWrite), 0, false)]
    [InlineData(nameof(InstallBoundary.AfterStateRename), 0, true)]
    [InlineData(nameof(InstallBoundary.AfterTransactionDelete), 0, true)]
    public void Initial_install_recovery_does_not_invent_a_previous_version(
        string boundaryName, int stagedWriteNumber, bool commitCompleted)
    {
        var interruptedAt = Enum.Parse<InstallBoundary>(boundaryName);
        var package = CreatePackage("1.0.0", "partial"u8.ToArray());
        var stagedWrites = 0;
        var interrupted = new InstallManager(installRoot, boundary =>
        {
            if (boundary == interruptedAt &&
                (boundary != InstallBoundary.AfterStagedPayloadFileWrite || ++stagedWrites == stagedWriteNumber))
                throw new SimulatedInterruptionException();
        });
        Assert.Throws<SimulatedInterruptionException>(() => interrupted.InstallPackage(package, launcher));

        var recovered = new InstallManager(installRoot).Recover();
        if (commitCompleted)
        {
            Assert.Equal(InstallOutcome.Installed, recovered.Outcome);
            Assert.Equal("1.0.0", recovered.InstalledVersion);
            Assert.Equal("partial"u8.ToArray(), File.ReadAllBytes(recovered.ExecutablePath!));
        }
        else
        {
            Assert.Equal(InstallOutcome.Rejected, recovered.Outcome);
            Assert.Null(recovered.InstalledVersion);
            Assert.Null(recovered.ExecutablePath);
            Assert.False(File.Exists(Path.Combine(installRoot, ".install-state.json")));
            Assert.Empty(Directory.GetDirectories(installRoot, "payload-*", SearchOption.TopDirectoryOnly));
            Assert.Contains("прер", recovered.FailureReason!, StringComparison.OrdinalIgnoreCase);
        }
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
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var before = File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe"));
        var invalid = CreatePackage("2.0.0", "payload-B"u8.ToArray(), extraPath: unsafePath);

        var result = new InstallManager(installRoot).InstallPackage(invalid, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        Assert.False(File.Exists(Path.Combine(installRoot, "escaped.txt")));
        Assert.Equal("database-sentinel"u8.ToArray(), File.ReadAllBytes(Path.Combine(dataRoot, "data.db")));
    }

    [Fact]
    public void Symlink_archive_entry_is_rejected_before_switching()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var invalid = CreatePackage("2.0.0", "payload-B"u8.ToArray(), extraPath: "linked.txt",
            extraAttributes: unchecked((int)0xA1FF0000));

        var result = new InstallManager(installRoot).InstallPackage(invalid, launcher);

        Assert.Equal(InstallOutcome.Rejected, result.Outcome);
        Assert.Equal("payload-A"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
    }

    [Fact]
    public void Invalid_manifest_file_list_and_integrity_are_rejected()
    {
        var missing = CreatePackage("2.0.0", "payload-B"u8.ToArray(), omitRuntimeConfig: true);
        var badHash = CreatePackage("3.0.0", "payload-C"u8.ToArray(), tamperHash: true);

        Assert.Equal(InstallOutcome.Rejected, new InstallManager(installRoot).InstallPackage(missing, launcher).Outcome);
        Assert.Equal(InstallOutcome.Rejected, new InstallManager(installRoot).InstallPackage(badHash, launcher).Outcome);
        Assert.False(Directory.Exists(installRoot));
    }

    [Fact]
    public void Occupied_inactive_slot_rolls_back_without_touching_active_payload_or_data()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        new InstallManager(installRoot).InstallPackage(packageB, launcher);
        var dataBefore = Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);

        var blocked = new InstallManager(installRoot, boundary =>
        {
            if (boundary == InstallBoundary.BeforeInactiveSlotDelete)
                throw new IOException("simulated Windows sharing violation");
        }).InstallPackage(packageC, launcher);

        Assert.Equal(InstallOutcome.RolledBack, blocked.Outcome);
        Assert.Equal("payload-B"u8.ToArray(), File.ReadAllBytes(Path.Combine(installRoot, "payload-1", "GaleWorkTimer.exe")));
        Assert.Equal(dataBefore, Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes));
    }

    [Fact]
    public void Running_application_rejects_update_before_any_payload_or_data_mutation()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        new InstallManager(installRoot).InstallPackage(packageA, launcher);
        var payloadBefore = File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe"));
        var dataBefore = Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);

        var blocked = new InstallManager(installRoot, checkpoint: null, applicationRunning: () => true)
            .InstallPackage(packageB, launcher);

        Assert.Equal(InstallOutcome.Rejected, blocked.Outcome);
        Assert.Contains("APP_RUNNING", blocked.FailureReason!, StringComparison.Ordinal);
        Assert.Equal(payloadBefore, File.ReadAllBytes(Path.Combine(installRoot, "payload-0", "GaleWorkTimer.exe")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, "payload-1")));
        Assert.Equal(dataBefore, Directory.GetFiles(dataRoot).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes));
    }

    [Fact]
    public async Task Concurrent_updater_is_rejected_while_first_transaction_holds_the_install_lock()
    {
        var packageA = CreatePackage("1.0.0", "payload-A"u8.ToArray());
        var packageB = CreatePackage("2.0.0", "payload-B"u8.ToArray());
        var packageC = CreatePackage("3.0.0", "payload-C"u8.ToArray());
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
    }

    [Fact]
    public void Package_builder_produces_repeatable_verified_zip_of_the_complete_publish_tree()
    {
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
    }

    private string CreatePackage(string version, byte[] executable, string? extraPath = null,
        int? extraAttributes = null, bool tamperHash = false, bool omitRuntimeConfig = false)
    {
        var path = Path.Combine(directory, version + ".zip");
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

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
