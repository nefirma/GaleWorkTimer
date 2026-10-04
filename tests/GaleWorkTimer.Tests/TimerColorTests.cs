using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerColorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-Color-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");
    public TimerColorTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Version_one_migration_preserves_all_timer_session_and_pause_fields()
    {
        using (var database = OpenDatabase())
        {
            using var command = database.CreateCommand();
            // Accepted v1 schema, independent of the production initializer under test.
            command.CommandText = """
                CREATE TABLE timers (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL CHECK (length(trim(name)) > 0),
                    is_archived INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
                    sort_order INTEGER NOT NULL,
                    created_at_ms INTEGER NOT NULL CHECK (created_at_ms >= 0),
                    updated_at_ms INTEGER NOT NULL CHECK (updated_at_ms >= created_at_ms)
                );
                CREATE TABLE sessions (
                    id INTEGER PRIMARY KEY,
                    timer_id INTEGER NOT NULL REFERENCES timers(id),
                    started_at_ms INTEGER NOT NULL CHECK (started_at_ms >= 0),
                    stopped_at_ms INTEGER CHECK (stopped_at_ms > started_at_ms),
                    created_at_ms INTEGER NOT NULL CHECK (created_at_ms >= 0),
                    updated_at_ms INTEGER NOT NULL CHECK (updated_at_ms >= created_at_ms)
                );
                CREATE UNIQUE INDEX one_open_session_per_timer
                    ON sessions(timer_id) WHERE stopped_at_ms IS NULL;
                CREATE INDEX timer_session_history ON sessions(timer_id, started_at_ms, id);
                CREATE TABLE pauses (
                    id INTEGER PRIMARY KEY,
                    session_id INTEGER NOT NULL REFERENCES sessions(id),
                    paused_at_ms INTEGER NOT NULL CHECK (paused_at_ms >= 0),
                    resumed_at_ms INTEGER CHECK (resumed_at_ms > paused_at_ms)
                );
                CREATE UNIQUE INDEX one_open_pause_per_session
                    ON pauses(session_id) WHERE resumed_at_ms IS NULL;
                CREATE INDEX session_pause_history ON pauses(session_id, paused_at_ms, id);
                INSERT INTO timers VALUES (7, 'Existing work', 0, 3, 0, 5000);
                INSERT INTO sessions VALUES (11, 7, 0, 3000, 0, 3000), (12, 7, 4000, NULL, 4000, 5000);
                INSERT INTO pauses VALUES (21, 11, 1000, 2000), (22, 12, 5000, NULL);
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        var before = LegacyRows();
        using (var service = new TimerService(DatabasePath))
        {
            var timer = Assert.Single(service.GetTimers());
            Assert.Equal(new TimerInfo(7, "Existing work", false, 3, 0, 5000, "blue"), timer);
            Assert.Equal("blue", timer.ColorKey);
            Assert.Equal(2, service.GetTimerSessions(7).Count);
            Assert.Equal(TimerState.Paused, service.GetTimerState(7));
            Assert.Equal(TimeSpan.FromSeconds(2), service.GetSessionDuration(11));
            var open = service.GetSessionSnapshot(12, 7000);
            Assert.Equal(TimeSpan.FromSeconds(1), open.ActiveDuration);
            Assert.Equal(TimeSpan.FromSeconds(2), open.PausedDuration);
            Assert.Equal(1, open.PauseCount);
        }
        Assert.Equal(before, LegacyRows());
        using var databaseAfter = OpenDatabase();
        using var version = databaseAfter.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, version.ExecuteScalar());
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal("blue", reopened.GetTimer(7).ColorKey);
        Assert.Equal(before, LegacyRows());
    }

    [Fact]
    public void Color_create_and_atomic_edit_survive_reopening_without_replacing_sessions()
    {
        long timerId, sessionId;
        using (var service = new TimerService(DatabasePath))
        {
            timerId = service.CreateTimer("Work", 0, "purple").Id;
            sessionId = service.Start(timerId, 1000);
            service.Pause(timerId, 2000);
        }
        SessionInfo session;
        using (var service = new TimerService(DatabasePath))
        {
            Assert.Equal("purple", service.GetTimer(timerId).ColorKey);
            session = Assert.Single(service.GetTimerSessions(timerId));
            service.EditTimer(timerId, "  Edited  ", "orange", 3000);
            Assert.Equal("Edited", service.GetTimer(timerId).Name);
            Assert.Equal("orange", service.GetTimer(timerId).ColorKey);
            service.RenameTimer(timerId, "Renamed", 4000);
            Assert.Equal("orange", service.GetTimer(timerId).ColorKey);
        }
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal("Renamed", reopened.GetTimer(timerId).Name);
        Assert.Equal("orange", reopened.GetTimer(timerId).ColorKey);
        Assert.Equal(session, Assert.Single(reopened.GetTimerSessions(timerId)));
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(timerId));
    }

    [Fact]
    public void Unknown_color_and_invalid_edit_are_rejected_without_any_persisted_change()
    {
        using var service = new TimerService(DatabasePath);
        var timer = service.CreateTimer("Work", 0, "teal");
        Assert.Throws<ArgumentException>(() => service.CreateTimer("Bad", 1000, "#00ff00"));
        Assert.Throws<ArgumentException>(() => service.EditTimer(timer.Id, "Changed", "unknown", 1000));
        Assert.Throws<ArgumentException>(() => service.EditTimer(timer.Id, "  ", "pink", 1000));
        Assert.Equal(timer, Assert.Single(service.GetTimers()));
        Assert.Empty(service.GetTimerSessions(timer.Id));
    }

    [Fact]
    public void Default_color_cycles_by_active_timer_count_and_new_database_is_version_two()
    {
        using var service = new TimerService(DatabasePath);
        Assert.Equal("blue", service.CreateTimer("A", 0).ColorKey);
        var b = service.CreateTimer("B", 0);
        Assert.Equal("purple", b.ColorKey);
        service.ArchiveTimer(b.Id, 1000);
        Assert.Equal("purple", service.CreateTimer("C", 1000).ColorKey);
        for (var i = 2; i <= TimerPalette.Colors.Count; i++)
            Assert.Equal(TimerPalette.Colors[i % TimerPalette.Colors.Count].ColorKey,
                service.CreateTimer("Next", 1000).ColorKey);
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    private string[] LegacyRows()
    {
        using var database = OpenDatabase();
        var rows = new List<string>();
        foreach (var sql in new[]
        {
            "SELECT id, name, is_archived, sort_order, created_at_ms, updated_at_ms FROM timers ORDER BY id;",
            "SELECT * FROM sessions ORDER BY id;", "SELECT * FROM pauses ORDER BY id;"
        })
        {
            using var command = database.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        }
        return rows.ToArray();
    }

    private SqliteConnection OpenDatabase()
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, Pooling = false
        }.ToString());
        database.Open();
        return database;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
