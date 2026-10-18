using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using GaleWorkTimer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-UI-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");

    public MainWindowViewModelTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(TimerState.Idle, "Stopped")]
    [InlineData(TimerState.Running, "Running")]
    [InlineData(TimerState.Paused, "Paused")]
    public void Timer_status_label_changes_display_copy_not_domain_state(TimerState state, string label)
    {
        var row = new TimerRow(1, "Work", state);
        Assert.Equal(label, row.StatusText);
        Assert.Equal(state, row.State);
    }

    [Fact]
    public void Timer_list_exposes_only_valid_actions_in_stable_order()
    {
        using var service = new TimerService(DatabasePath);
        var idle = service.CreateTimer("Idle", 0).Id;
        var running = service.CreateTimer("Running", 0).Id;
        var paused = service.CreateTimer("Paused", 0).Id;
        service.Start(running, 1_000);
        service.Start(paused, 1_000);
        service.Pause(paused, 2_000);

        var model = new MainWindowViewModel(service, 2_000);
        Assert.Equal(new[] { paused, running, idle }, model.Timers.Select(t => t.Id));
        Assert.Equal(new[] { TimerState.Paused, TimerState.Running, TimerState.Idle }, model.Timers.Select(t => t.State));
        Assert.Equal(new[] { false, false, true }, model.Timers.Select(t => t.CanStart));
        Assert.Equal(new[] { false, true, false }, model.Timers.Select(t => t.CanPause));
        Assert.Equal(new[] { true, false, false }, model.Timers.Select(t => t.CanResume));
        Assert.Equal(new[] { true, true, false }, model.Timers.Select(t => t.CanStop));
    }

    [Fact]
    public void Application_actions_persist_independent_states_names_and_archive_across_reopening()
    {
        long a, b, c, sessionA;
        using (var service = new TimerService(DatabasePath))
        {
            var model = new MainWindowViewModel(service, 0);
            Assert.True(model.CreateTimer("  A  ", 0));
            Assert.True(model.CreateTimer("B", 0));
            Assert.True(model.CreateTimer("C", 0));
            (a, b, c) = (model.Timers[2].Id, model.Timers[1].Id, model.Timers[0].Id);
            Assert.Equal("A", model.Timers[2].Name);
            Assert.True(model.StartTimer(a, 1_000));
            Assert.True(model.StartTimer(b, 1_000));
            Assert.True(model.StartTimer(c, 1_000));
            Assert.All(model.Timers, row => Assert.Equal(TimerState.Running, row.State));
            sessionA = Assert.Single(service.GetTimerSessions(a)).Id;
            Assert.True(model.PauseTimer(a, 2_000));
            Assert.Equal(TimerState.Running, model.Timers[1].State);
            Assert.True(model.StopTimer(b, 3_000));
            Assert.True(model.RenameTimer(a, "  Renamed A  ", 3_000));
            Assert.True(model.ArchiveTimer(b, 4_000));
        }

        using (var reopened = new TimerService(DatabasePath))
        {
            var model = new MainWindowViewModel(reopened, 4_000);
            Assert.Equal(new[] { c, a }, model.Timers.Select(t => t.Id));
            Assert.Equal("Renamed A", model.Timers[1].Name);
            Assert.Equal(TimerState.Paused, model.Timers[1].State);
            Assert.Equal(TimerState.Running, model.Timers[0].State);
            Assert.Equal(b, Assert.Single(model.ArchivedTimers).Id);
            Assert.Single(reopened.GetTimerSessions(b));
            Assert.True(model.RestoreTimer(b, 5_000));
            Assert.Empty(model.ArchivedTimers);
            Assert.Equal(new[] { c, b, a }, model.Timers.Select(t => t.Id));
            Assert.True(model.ResumeTimer(a, 6_000));
            Assert.Equal(sessionA, Assert.Single(reopened.GetTimerSessions(a)).Id);
            Assert.True(model.StopTimer(a, 7_000));
            Assert.True(model.PauseTimer(c, 7_000));
            Assert.True(model.StopTimer(c, 8_000));
        }

        using var restored = new TimerService(DatabasePath);
        var restoredModel = new MainWindowViewModel(restored, 8_000);
        Assert.Equal(new[] { c, b, a }, restoredModel.Timers.Select(t => t.Id));
        Assert.All(restoredModel.Timers, row => Assert.Equal(TimerState.Idle, row.State));
        Assert.Equal("Renamed A", restoredModel.Timers[2].Name);
        Assert.Empty(restoredModel.ArchivedTimers);
    }

    [Fact]
    public void Invalid_inputs_archive_and_database_write_failure_do_not_appear_successful()
    {
        using var service = new TimerService(DatabasePath);
        var model = new MainWindowViewModel(service, 0);
        Assert.False(model.CreateTimer("   ", 0));
        Assert.Empty(model.Timers);
        Assert.True(model.CreateTimer("Work", 0));
        var id = Assert.Single(model.Timers).Id;
        Assert.False(model.RenameTimer(id, "  ", 1_000));
        Assert.Equal("Work", Assert.Single(model.Timers).Name);
        Assert.True(model.StartTimer(id, 1_000));
        Assert.False(model.ArchiveTimer(id, 2_000));
        Assert.Equal("Stop this timer before removing it from the list.", model.ErrorMessage);
        Assert.Equal(TimerState.Running, Assert.Single(model.Timers).State);
        Assert.Empty(model.ArchivedTimers);
        Assert.False(model.StartTimer(id, 2_000));
        Assert.Equal(TimerState.Running, Assert.Single(model.Timers).State);
        var before = service.GetTimer(id);

        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Pooling = false
        }.ToString());
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER fail_update BEFORE UPDATE ON timers
            BEGIN SELECT RAISE(ABORT, 'internal SQL failure /private/path'); END;
            """;
        command.ExecuteNonQuery();
        Assert.False(model.PauseTimer(id, 3_000));
        Assert.Equal(TimerState.Running, Assert.Single(model.Timers).State);
        Assert.Equal(before, service.GetTimer(id));
        Assert.Equal(TimeSpan.FromSeconds(3), service.GetSessionDuration(Assert.Single(service.GetTimerSessions(id)).Id, 4_000));
        Assert.NotEmpty(model.ErrorMessage);
        Assert.DoesNotContain("SQL", model.ErrorMessage);
        Assert.DoesNotContain("/private/path", model.ErrorMessage);
    }

    [Fact]
    public void History_projects_all_sessions_newest_first_with_current_names_and_pause_totals()
    {
        using var service = new TimerService(DatabasePath);
        var closed = service.CreateTimer("Old name", 0).Id;
        var paused = service.CreateTimer("Paused", 0).Id;
        var running = service.CreateTimer("Running", 0).Id;
        var closedSession = service.Start(closed, 0);
        service.Pause(closed, 10_000);
        service.Resume(closed, 30_000);
        service.Pause(closed, 50_000);
        service.Stop(closed, 80_000);
        var pausedSession = service.Start(paused, 90_000);
        service.Pause(paused, 100_000);
        var runningSession = service.Start(running, 110_000);
        service.RenameTimer(closed, "Current name", 140_000);
        service.ArchiveTimer(closed, 145_000);

        var model = new MainWindowViewModel(service, 150_000);
        Assert.True(model.RefreshHistory(150_000));
        Assert.Equal(new[] { runningSession, pausedSession, closedSession }, model.History.Select(h => h.Snapshot.Session.Id));
        Assert.Equal(new[] { "Running", "Paused", "Stopped" }, model.History.Select(h => h.Status));
        Assert.Equal(new[] { 40_000L, 10_000L, 30_000L }, model.History.Select(h => h.Snapshot.ActiveDuration.Ticks / TimeSpan.TicksPerMillisecond));
        Assert.Equal(new[] { 0L, 50_000L, 50_000L }, model.History.Select(h => h.Snapshot.PausedDuration.Ticks / TimeSpan.TicksPerMillisecond));
        Assert.Equal(new[] { 0, 1, 2 }, model.History.Select(h => h.Snapshot.PauseCount));
        Assert.Equal("Current name", model.History[2].TimerName);
        Assert.Equal(80_000, model.History[2].Snapshot.Session.StoppedAtMs);
        Assert.Null(model.History[0].Snapshot.Session.StoppedAtMs);
        Assert.Equal("Running", model.History[0].StopText);
        Assert.Equal("Paused", model.History[1].StopText);
        Assert.Equal(150_000, model.HistorySnapshotAtMs);
    }

    [Fact]
    public void Running_history_is_an_explicit_immutable_snapshot_until_refresh()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        service.Start(timerId, 1_000);
        service.Pause(timerId, 2_000);
        service.Resume(timerId, 5_000);
        var model = new MainWindowViewModel(service, 5_000);
        Assert.True(model.RefreshHistory(8_000));
        var row = Assert.Single(model.History);
        Assert.Equal("Running", row.Status);
        Assert.Equal(TimeSpan.FromSeconds(4), row.Snapshot.ActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), row.Snapshot.PausedDuration);
        Assert.Equal(1, row.Snapshot.PauseCount);

        Assert.True(model.PauseTimer(timerId, 9_000));
        Assert.Same(row, Assert.Single(model.History));
        Assert.Equal(8_000, model.HistorySnapshotAtMs);
        Assert.Equal("Running", row.Status);
        Assert.False(model.RefreshHistory(8_000)); // Cannot snapshot before the new Pause.
        Assert.Same(row, Assert.Single(model.History));
        Assert.NotEmpty(model.ErrorMessage);
        Assert.True(model.RefreshHistory(10_000));
        row = Assert.Single(model.History);
        Assert.Equal("Paused", row.Status);
        Assert.Equal(TimeSpan.FromSeconds(5), row.Snapshot.ActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), row.Snapshot.PausedDuration);
        Assert.Equal(2, row.Snapshot.PauseCount);
        Assert.Equal(10_000, model.HistorySnapshotAtMs);
        Assert.Equal("1 h 05 min", HistoryRow.FormatDuration(TimeSpan.FromMinutes(65)));
        Assert.Equal("42 min", HistoryRow.FormatDuration(TimeSpan.FromMinutes(42)));
    }

    [Fact]
    public void Timer_accents_and_history_statuses_are_independent_and_edit_updates_existing_snapshots()
    {
        using var service = new TimerService(DatabasePath);
        var model = new MainWindowViewModel(service, 0);
        Assert.True(model.CreateTimer("A", 0, "green"));
        Assert.True(model.CreateTimer("B", 0, "red"));
        Assert.True(model.CreateTimer("C", 0, "blue"));
        var (a, b, c) = (model.Timers[2].Id, model.Timers[1].Id, model.Timers[0].Id);
        Assert.True(model.StartTimer(a, 1000));
        Assert.True(model.PauseTimer(a, 2000));
        Assert.True(model.StartTimer(b, 1000));
        Assert.True(model.StartTimer(c, 1000));
        Assert.True(model.StopTimer(c, 2000));
        Assert.True(model.RefreshHistory(4000));
        var paused = model.History.Single(h => h.Snapshot.Session.TimerId == a);
        var running = model.History.Single(h => h.Snapshot.Session.TimerId == b);
        var stopped = model.History.Single(h => h.Snapshot.Session.TimerId == c);
        Assert.Equal(("green", "Paused", false, true, false), (paused.ColorKey, paused.Status, paused.IsStopped, paused.IsPaused, paused.IsRunning));
        Assert.Equal(("red", "Running", false, false, true), (running.ColorKey, running.Status, running.IsStopped, running.IsPaused, running.IsRunning));
        Assert.Equal(("blue", "Stopped", true, false, false), (stopped.ColorKey, stopped.Status, stopped.IsStopped, stopped.IsPaused, stopped.IsRunning));

        Assert.True(model.EditTimer(a, "Edited A", "purple", 5000));
        Assert.Equal("purple", model.Timers.Single(t => t.Id == a).ColorKey);
        var edited = model.History.Single(h => h.Snapshot.Session.TimerId == a);
        Assert.Equal("purple", edited.ColorKey);
        Assert.Equal("Edited A", edited.TimerName);
        Assert.Equal("Paused", edited.Status);
        Assert.Same(paused.Snapshot, edited.Snapshot);
        Assert.Equal(4000, model.HistorySnapshotAtMs); // Metadata edit must not tick duration.
        Assert.True(model.ArchiveTimer(c, 5000));
        Assert.True(model.EditTimer(c, "Archived C", "orange", 6000));
        Assert.Equal("orange", Assert.Single(model.ArchivedTimers).ColorKey);
        Assert.Equal("orange", model.History.Single(h => h.Snapshot.Session.TimerId == c).ColorKey);
        Assert.False(model.EditTimer(c, "Bad", "unknown", 7000));
        Assert.Equal("orange", Assert.Single(model.ArchivedTimers).ColorKey);
        Assert.Equal("Archived C", model.History.Single(h => h.Snapshot.Session.TimerId == c).TimerName);
        Assert.Equal("cyan", model.SuggestedColorKey);
    }

    [Fact]
    public void Timer_total_projection_is_a_shared_static_snapshot_and_preserves_session_history()
    {
        const long minute = 60_000;
        using var service = new TimerService(DatabasePath);
        var a = service.CreateTimer("Work", 0, "purple").Id;
        var b = service.CreateTimer("Other", 0, "red").Id;
        service.Start(a, 0);
        service.Stop(a, 120 * minute);
        service.Start(a, 150 * minute);
        service.Start(b, 120 * minute);
        var model = new MainWindowViewModel(service, 180 * minute);
        var row = model.Timers.Single(t => t.Id == a);
        Assert.Equal(("Work", "purple", "Running", "2 h 30 min"),
            (row.Name, row.ColorKey, row.StatusText, row.TotalText));
        Assert.Equal(TimeSpan.FromMinutes(150), row.Total);
        Assert.Equal(TimeSpan.FromHours(1), model.Timers.Single(t => t.Id == b).Total);
        Assert.Equal(180 * minute, model.TimersSnapshotAtMs);
        Assert.True(model.RefreshHistory(190 * minute));
        var history = model.History.Single(h => h.Snapshot.Session.TimerId == b);
        Assert.Same(row, model.Timers.Single(t => t.Id == a)); // History refresh does not tick Timer Total.
        Assert.Equal(TimeSpan.FromMinutes(150), row.Total);
        Assert.True(model.RefreshTimers(195 * minute));
        Assert.Equal("2 h 45 min", model.Timers.Single(t => t.Id == a).TotalText);
        Assert.Equal("1 h 15 min", model.Timers.Single(t => t.Id == b).TotalText);
        Assert.True(model.PauseTimer(a, 200 * minute));
        Assert.Equal("2 h 50 min", model.Timers.Single(t => t.Id == a).TotalText);
        Assert.True(model.RefreshTimers(260 * minute));
        Assert.Equal("2 h 50 min", model.Timers.Single(t => t.Id == a).TotalText);
        Assert.False(model.RefreshTimers(199 * minute));
        Assert.Equal(260 * minute, model.TimersSnapshotAtMs);
        Assert.True(model.ResumeTimer(a, 260 * minute));
        Assert.True(model.StopTimer(a, 280 * minute));
        Assert.Equal("3 h 10 min", model.Timers.Single(t => t.Id == a).TotalText);
        Assert.True(model.ArchiveTimer(a, 300 * minute));
        Assert.Equal("3 h 10 min", Assert.Single(model.ArchivedTimers).TotalText);
        Assert.Same(history, model.History.Single(h => h.Snapshot.Session.TimerId == b));
        Assert.True(model.RefreshHistory(300 * minute));
        Assert.Equal(2, model.History.Count(h => h.Snapshot.Session.TimerId == a));
        Assert.Equal("0 min", HistoryRow.FormatDuration(TimeSpan.Zero));
        Assert.Equal("126 h 15 min", HistoryRow.FormatDuration(TimeSpan.FromMinutes(7575)));
    }

    [Fact]
    public void Delete_removes_only_matching_history_and_preserves_other_snapshots_and_failed_views()
    {
        using var service = new TimerService(DatabasePath);
        var a = service.CreateTimer("Delete", 0).Id;
        var b = service.CreateTimer("Keep", 0).Id;
        service.Start(a, 1000);
        service.Pause(a, 2000);
        service.Stop(a, 3000);
        service.Start(b, 1000);
        var model = new MainWindowViewModel(service, 4000);
        Assert.True(model.Timers.Single(t => t.Id == a).CanDelete);
        Assert.False(model.Timers.Single(t => t.Id == b).CanDelete);
        Assert.False(model.DeleteTimer(b, 4000));
        Assert.Equal("Stop this timer before deleting it.", model.ErrorMessage);
        Assert.True(model.PauseTimer(b, 5000));
        Assert.False(model.Timers.Single(t => t.Id == b).CanDelete);
        Assert.False(model.DeleteTimer(b, 5000));
        Assert.True(model.ArchiveTimer(a, 5000));
        Assert.True(Assert.Single(model.ArchivedTimers).CanDelete);
        Assert.True(model.RefreshHistory(6000));
        var kept = model.History.Single(h => h.Snapshot.Session.TimerId == b);
        using (var database = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_delete BEFORE DELETE ON timers BEGIN SELECT RAISE(ABORT, 'private SQL details'); END;";
            command.ExecuteNonQuery();
            Assert.False(model.DeleteTimer(a, 7000));
            Assert.Single(model.ArchivedTimers);
            Assert.Equal(2, model.History.Count);
            Assert.DoesNotContain("SQL", model.ErrorMessage);
            command.CommandText = "DROP TRIGGER fail_delete;";
            command.ExecuteNonQuery();
        }
        Assert.True(model.DeleteTimer(a, 8000));
        Assert.Empty(model.ArchivedTimers);
        Assert.Equal(b, Assert.Single(model.Timers).Id);
        Assert.Same(kept, Assert.Single(model.History));
        Assert.Equal(6000, model.HistorySnapshotAtMs);
        Assert.True(model.RefreshHistory(9000));
        Assert.Equal(b, Assert.Single(model.History).Snapshot.Session.TimerId);
    }

    [Fact]
    public void Active_and_archived_lists_use_creation_descending_then_id_without_rewriting_metadata()
    {
        using var service = new TimerService(DatabasePath);
        var a = service.CreateTimer("A", 10_000).Id;
        var b = service.CreateTimer("B", 20_000).Id;
        var c = service.CreateTimer("C", 30_000).Id;
        var model = new MainWindowViewModel(service, 30_000);
        Assert.Equal(new[] { c, b, a }, model.Timers.Select(t => t.Id));
        // Insert a lower timestamp after a higher one: reversing IDs alone is incorrect.
        var earlier = service.CreateTimer("Earlier", 15_000).Id;
        var tie = service.CreateTimer("Same time as C", 30_000).Id;
        var metadata = service.GetTimers().ToArray();
        Assert.True(model.RefreshTimers(30_000));
        Assert.Equal(new[] { tie, c, b, earlier, a }, model.Timers.Select(t => t.Id));
        Assert.Equal(metadata, service.GetTimers().ToArray());
        foreach (var id in new[] { a, b, c, earlier, tie })
            Assert.True(model.ArchiveTimer(id, 40_000));
        Assert.Empty(model.Timers);
        Assert.Equal(new[] { tie, c, b, earlier, a }, model.ArchivedTimers.Select(t => t.Id));
        using var reopened = new TimerService(DatabasePath);
        var restored = new MainWindowViewModel(reopened, 50_000);
        Assert.Equal(new[] { tie, c, b, earlier, a }, restored.ArchivedTimers.Select(t => t.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Copy_passes_only_exact_name_and_handles_sync_or_async_failure_without_refresh(int failure)
    {
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("  FamilyLine Android QA — Галя  ", 0).Id;
        service.Start(id, 1000);
        var model = new MainWindowViewModel(service, 5000);
        Assert.True(model.RefreshHistory(5000));
        var row = Assert.Single(model.Timers);
        var history = Assert.Single(model.History);
        var metadata = service.GetTimer(id);
        string? received = null;
        var writes = 0;
        Task Write(string text)
        {
            received = text;
            writes++;
            if (failure == 1) throw new InvalidOperationException("private clipboard details");
            return failure == 2 ? Task.FromException(new InvalidOperationException("private clipboard details"))
                : Task.CompletedTask;
        }
        Assert.Equal(failure == 0, await model.CopyTimerNameAsync(row, Write));
        Assert.Equal("FamilyLine Android QA — Галя", received);
        Assert.Equal(1, writes);
        Assert.Equal(failure == 0 ? "" : "Could not copy the timer name. Please try again.", model.ErrorMessage);
        Assert.Same(row, Assert.Single(model.Timers));
        Assert.Same(history, Assert.Single(model.History));
        Assert.Equal(5000, model.TimersSnapshotAtMs);
        Assert.Equal(metadata, service.GetTimer(id));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
