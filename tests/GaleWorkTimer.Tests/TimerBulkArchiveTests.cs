using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using GaleWorkTimer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerBulkArchiveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-BulkArchive-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");

    public TimerBulkArchiveTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Archive_all_archives_only_stopped_timers_and_preserves_metadata_sessions_pauses_and_totals()
    {
        long zeroSession, completed, running, paused, archived;
        TimerInfo[] timersBefore;
        SessionInfo[][] sessionsBefore;
        (long SessionId, long PausedAt, long? ResumedAt)[] pausesBefore;
        IReadOnlyDictionary<long, TimeSpan> totalsBefore;
        using (var service = new TimerService(DatabasePath))
        {
            zeroSession = service.CreateTimer("No sessions", 10, "green").Id;
            completed = service.CreateTimer("Completed", 20, "red").Id;
            service.Start(completed, 100);
            service.Pause(completed, 200);
            service.Resume(completed, 300);
            service.Stop(completed, 400);
            service.Start(completed, 500);
            service.Pause(completed, 600);
            service.Stop(completed, 700);
            running = service.CreateTimer("Running", 30).Id;
            service.Start(running, 1_000);
            paused = service.CreateTimer("Paused", 40).Id;
            service.Start(paused, 1_000);
            service.Pause(paused, 2_000);
            archived = service.CreateTimer("Already archived", 50).Id;
            service.ArchiveTimer(archived, 60);

            timersBefore = service.GetTimers().ToArray();
            sessionsBefore = timersBefore.Select(timer => service.GetTimerSessions(timer.Id).ToArray()).ToArray();
            pausesBefore = ReadPauses();
            totalsBefore = service.GetTimerTotals(8_000);

            service.ArchiveAllStopped(8_000);

            Assert.True(service.GetTimer(zeroSession).IsArchived);
            Assert.True(service.GetTimer(completed).IsArchived);
            Assert.Equal(TimerState.Running, service.GetTimerState(running));
            Assert.Equal(TimerState.Paused, service.GetTimerState(paused));
            Assert.True(service.GetTimer(archived).IsArchived);
            Assert.Equal(totalsBefore.OrderBy(pair => pair.Key), service.GetTimerTotals(8_000).OrderBy(pair => pair.Key));
            Assert.Equal(pausesBefore, ReadPauses());
            Assert.Equal(sessionsBefore.SelectMany(sessions => sessions), service.GetTimers().SelectMany(timer => service.GetTimerSessions(timer.Id)));
            foreach (var before in timersBefore)
            {
                var after = service.GetTimer(before.Id);
                var shouldArchive = before.Id == zeroSession || before.Id == completed;
                Assert.Equal(before with
                {
                    IsArchived = before.IsArchived || shouldArchive,
                    UpdatedAtMs = shouldArchive ? 8_000 : before.UpdatedAtMs
                }, after);
            }
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.True(reopened.GetTimer(zeroSession).IsArchived);
        Assert.True(reopened.GetTimer(completed).IsArchived);
        Assert.Equal(TimerState.Running, reopened.GetTimerState(running));
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(paused));
        Assert.Equal(pausesBefore, ReadPauses());
        Assert.Equal(sessionsBefore.SelectMany(sessions => sessions), reopened.GetTimers().SelectMany(timer => reopened.GetTimerSessions(timer.Id)));
        Assert.Equal(totalsBefore.OrderBy(pair => pair.Key), reopened.GetTimerTotals(8_000).OrderBy(pair => pair.Key));
        var model = new MainWindowViewModel(reopened, 8_000);
        Assert.Equal(new[] { archived, completed, zeroSession }, model.ArchivedTimers.Select(timer => timer.Id));
        Assert.True(model.RefreshHistory(8_000));
        Assert.Equal(2, model.History.Count(row => row.Snapshot.Session.TimerId == completed));
        Assert.Equal(TimeSpan.FromMilliseconds(300), model.History
            .Where(row => row.Snapshot.Session.TimerId == completed)
            .Aggregate(TimeSpan.Zero, (sum, row) => sum + row.Snapshot.ActiveDuration));
        Assert.True(model.RestoreTimer(zeroSession, 9_000));
        Assert.False(reopened.GetTimer(zeroSession).IsArchived);
    }

    [Fact]
    public void Empty_repeat_and_invalid_timestamp_calls_are_safe_and_atomic()
    {
        using var service = new TimerService(DatabasePath);
        var running = service.CreateTimer("Running", 100).Id;
        service.Start(running, 200);
        var paused = service.CreateTimer("Paused", 100).Id;
        service.Start(paused, 200);
        service.Pause(paused, 300);
        service.ArchiveAllStopped(1_000);
        Assert.False(service.GetTimer(running).IsArchived);
        Assert.False(service.GetTimer(paused).IsArchived);

        var first = service.CreateTimer("First", 1_000).Id;
        var second = service.CreateTimer("Second", 2_000).Id;
        var firstBefore = service.GetTimer(first);
        var secondBefore = service.GetTimer(second);
        var staleTimestamp = Assert.Throws<TimerDomainException>(() => service.ArchiveAllStopped(1_500));
        Assert.Equal(TimerError.InvalidTimestamp, staleTimestamp.Error);
        Assert.Equal(firstBefore, service.GetTimer(first));
        Assert.Equal(secondBefore, service.GetTimer(second));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.ArchiveAllStopped(-1));
        Assert.Equal(firstBefore, service.GetTimer(first));

        service.ArchiveAllStopped(2_000);
        var archivedAt = service.GetTimer(first);
        service.ArchiveAllStopped(3_000);
        Assert.Equal(archivedAt, service.GetTimer(first));
        Assert.True(service.GetTimer(second).IsArchived);
    }

    [Fact]
    public void Failure_after_a_candidate_update_rolls_back_the_entire_bulk_archive()
    {
        using var service = new TimerService(DatabasePath);
        var ids = new[]
        {
            service.CreateTimer("One", 10).Id,
            service.CreateTimer("Two", 20).Id,
            service.CreateTimer("Three", 30).Id
        };
        var before = ids.Select(service.GetTimer).ToArray();
        using (var database = OpenDatabase())
        using (var command = database.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE archive_attempts(timer_id INTEGER NOT NULL);
                CREATE TRIGGER fail_second_archive AFTER UPDATE OF is_archived ON timers
                WHEN NEW.is_archived = 1
                BEGIN
                    INSERT INTO archive_attempts VALUES (NEW.id);
                    SELECT CASE WHEN (SELECT count(*) FROM archive_attempts) = 2
                        THEN RAISE(ABORT, 'injected bulk write failure') END;
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => service.ArchiveAllStopped(1_000));

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(before, ids.Select(reopened.GetTimer).ToArray());
    }

    private (long SessionId, long PausedAt, long? ResumedAt)[] ReadPauses()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT session_id, paused_at_ms, resumed_at_ms FROM pauses ORDER BY session_id, paused_at_ms;";
        using var reader = command.ExecuteReader();
        var pauses = new List<(long SessionId, long PausedAt, long? ResumedAt)>();
        while (reader.Read())
            pauses.Add((reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        return pauses.ToArray();
    }

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
