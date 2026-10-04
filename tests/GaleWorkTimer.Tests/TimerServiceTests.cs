using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "timers.db");

    public TimerServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Created_timer_survives_reopening_database()
    {
        long id;
        using (var service = new TimerService(DatabasePath))
        {
            var timer = service.CreateTimer("Work", 1_000);
            id = timer.Id;
            Assert.Equal("Work", timer.Name);
            Assert.False(timer.IsArchived);
            Assert.Equal(0, timer.SortOrder);
            Assert.Equal(1_000, timer.CreatedAtMs);
            Assert.Equal(1_000, timer.UpdatedAtMs);
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal("Work", reopened.GetTimer(id).Name);
    }

    [Fact]
    public void Valid_transitions_derive_state_including_stop_while_paused()
    {
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("Work", 0).Id;
        Assert.Equal(TimerState.Idle, service.GetTimerState(id));
        service.Start(id, 1_000);
        Assert.Equal(TimerState.Running, service.GetTimerState(id));
        service.Pause(id, 1_000); // A pause may start at the session's start.
        Assert.Equal(TimerState.Paused, service.GetTimerState(id));
        service.Resume(id, 3_000);
        Assert.Equal(TimerState.Running, service.GetTimerState(id));
        service.Stop(id, 3_000); // Stop may equal the last resume, but not Start.
        Assert.Equal(TimerState.Idle, service.GetTimerState(id));
        service.Start(id, 5_000);
        service.Pause(id, 6_000);
        service.Stop(id, 7_000);
        Assert.Equal(TimerState.Idle, service.GetTimerState(id));
    }

    [Fact]
    public void One_pause_excludes_25_minutes_from_two_hour_session()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 0);
        service.Pause(timerId, 45 * 60_000);
        service.Resume(timerId, 70 * 60_000);
        service.Stop(timerId, 120 * 60_000);

        Assert.Equal(TimeSpan.FromMinutes(95), service.GetSessionDuration(sessionId));
        var session = Assert.Single(service.GetTimerSessions(timerId));
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(timerId, session.TimerId);
        Assert.Equal(0, session.StartedAtMs);
        Assert.Equal(120 * 60_000, session.StoppedAtMs);
    }

    [Fact]
    public void Rename_and_archive_preserve_history_and_can_be_reversed_after_restart()
    {
        long timerId;
        long sessionId;
        using (var service = new TimerService(DatabasePath))
        {
            timerId = service.CreateTimer("Work", 0).Id;
            sessionId = service.Start(timerId, 1_000);
            service.Stop(timerId, 61_000);
            service.RenameTimer(timerId, "Renamed", 62_000);
            service.ArchiveTimer(timerId, 63_000);
            Assert.Equal(TimerState.Idle, service.GetTimerState(timerId));
        }

        using var reopened = new TimerService(DatabasePath);
        var timer = reopened.GetTimer(timerId);
        Assert.Equal("Renamed", timer.Name);
        Assert.True(timer.IsArchived);
        Assert.Equal(0, timer.CreatedAtMs);
        Assert.Equal(63_000, timer.UpdatedAtMs);
        Assert.Equal(sessionId, Assert.Single(reopened.GetTimerSessions(timerId)).Id);
        Assert.Equal(TimeSpan.FromMinutes(1), reopened.GetSessionDuration(sessionId));
        reopened.UnarchiveTimer(timerId, 64_000);
        Assert.False(reopened.GetTimer(timerId).IsArchived);
        Assert.Equal(TimerState.Idle, reopened.GetTimerState(timerId));
    }

    [Theory]
    [InlineData(TimerState.Idle, "Pause")]
    [InlineData(TimerState.Idle, "Resume")]
    [InlineData(TimerState.Idle, "Stop")]
    [InlineData(TimerState.Running, "Start")]
    [InlineData(TimerState.Running, "Resume")]
    [InlineData(TimerState.Paused, "Start")]
    [InlineData(TimerState.Paused, "Pause")]
    public void Invalid_transition_preserves_persistent_state(TimerState state, string operation)
    {
        long timerId;
        TimerInfo before;
        SessionInfo[] sessions;
        using (var service = new TimerService(DatabasePath))
        {
            timerId = service.CreateTimer("Work", 0).Id;
            if (state != TimerState.Idle) service.Start(timerId, 1_000);
            if (state == TimerState.Paused) service.Pause(timerId, 2_000);
            before = service.GetTimer(timerId);
            sessions = service.GetTimerSessions(timerId).ToArray();
            var error = Assert.Throws<TimerDomainException>(() => Apply(service, timerId, operation, 3_000));
            Assert.Equal(TimerError.InvalidTransition, error.Error);
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(before, reopened.GetTimer(timerId));
        Assert.Equal(sessions, reopened.GetTimerSessions(timerId).ToArray());
        Assert.Equal(state, reopened.GetTimerState(timerId));
        if (state != TimerState.Idle)
        {
            reopened.Stop(timerId, 4_000);
            Assert.Equal(TimeSpan.FromMilliseconds(state == TimerState.Paused ? 1_000 : 3_000),
                reopened.GetSessionDuration(sessions[0].Id));
        }
    }

    [Theory]
    [InlineData(false, "Stop")]
    [InlineData(true, "Resume")]
    [InlineData(true, "Stop")]
    public void Equal_endpoint_is_rejected_and_open_intervals_survive_reopening(bool paused, string operation)
    {
        long timerId, sessionId;
        TimerInfo timer;
        SessionInfo session;
        using (var service = new TimerService(DatabasePath))
        {
            timerId = service.CreateTimer("Work", 1_000).Id;
            sessionId = service.Start(timerId, 1_000);
            if (paused) service.Pause(timerId, 2_000);
            timer = service.GetTimer(timerId);
            session = Assert.Single(service.GetTimerSessions(timerId));
            var error = Assert.Throws<TimerDomainException>(() =>
                Apply(service, timerId, operation, paused ? 2_000 : 1_000));
            Assert.Equal(TimerError.InvalidTimestamp, error.Error);
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(timer, reopened.GetTimer(timerId));
        var persistedSession = Assert.Single(reopened.GetTimerSessions(timerId));
        Assert.Equal(session, persistedSession);
        Assert.Null(persistedSession.StoppedAtMs);
        Assert.Equal(paused ? TimerState.Paused : TimerState.Running, reopened.GetTimerState(timerId));
        Assert.Equal(TimeSpan.FromSeconds(paused ? 1 : 2), reopened.GetSessionDuration(sessionId, 3_000));

        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT paused_at_ms, resumed_at_ms FROM pauses WHERE session_id = $session;";
        command.Parameters.AddWithValue("$session", sessionId);
        using var reader = command.ExecuteReader();
        Assert.Equal(paused, reader.Read());
        if (paused)
        {
            Assert.Equal(2_000, reader.GetInt64(0));
            Assert.True(reader.IsDBNull(1));
            Assert.False(reader.Read());
        }
    }

    [Fact]
    public void Session_crossing_utc_midnight_has_sixty_minutes_of_active_time()
    {
        var startMs = new DateTimeOffset(2026, 10, 3, 23, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var stopMs = new DateTimeOffset(2026, 10, 4, 0, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", startMs).Id;
        var sessionId = service.Start(timerId, startMs);
        service.Stop(timerId, stopMs);
        Assert.Equal(TimeSpan.FromMinutes(60), service.GetSessionDuration(sessionId));
    }

    [Fact]
    public void Session_crossing_spring_dst_uses_elapsed_utc_time_not_local_clock_labels()
    {
        // New York, 2026-03-08: 02:00 EST jumps to 03:00 EDT.
        // 01:30 -05:00 = 06:30 UTC; 03:30 -04:00 = 07:30 UTC.
        var start = new DateTimeOffset(2026, 3, 8, 1, 30, 0, TimeSpan.FromHours(-5));
        var stop = new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4));
        var startMs = start.ToUnixTimeMilliseconds();
        var stopMs = stop.ToUnixTimeMilliseconds();
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", startMs).Id;
        var sessionId = service.Start(timerId, startMs);
        service.Stop(timerId, stopMs);
        Assert.Equal(TimeSpan.FromHours(2), stop.DateTime - start.DateTime);
        Assert.Equal(TimeSpan.FromMinutes(60), service.GetSessionDuration(sessionId));
    }

    [Fact]
    public void Simple_start_stop_preserves_millisecond_precision()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 1_000);
        service.Stop(timerId, 61_123);
        Assert.Equal(TimeSpan.FromMilliseconds(60_123), service.GetSessionDuration(sessionId));
    }

    [Fact]
    public void Multiple_pauses_and_stop_while_paused_exclude_all_paused_time()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 0);
        service.Pause(timerId, 10_000);
        service.Resume(timerId, 30_000);
        service.Pause(timerId, 50_000);
        service.Resume(timerId, 80_000);
        service.Pause(timerId, 85_000);
        Assert.Equal(TimeSpan.FromSeconds(35), service.GetSessionDuration(sessionId));
        Assert.Equal(TimeSpan.FromSeconds(35), service.GetSessionDuration(sessionId, 500_000));
        service.Stop(timerId, 600_000);
        Assert.Equal(TimeSpan.FromSeconds(35), service.GetSessionDuration(sessionId));
        Assert.Equal(TimerState.Idle, service.GetTimerState(timerId));

        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT p.resumed_at_ms, s.stopped_at_ms FROM pauses p
            JOIN sessions s ON s.id = p.session_id ORDER BY p.id DESC LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(600_000, reader.GetInt64(0));
        Assert.Equal(600_000, reader.GetInt64(1));
    }

    [Fact]
    public void Running_duration_uses_explicit_snapshot_and_does_not_write_elapsed_time()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 1_000);
        var timer = service.GetTimer(timerId);
        var session = Assert.Single(service.GetTimerSessions(timerId));
        Assert.Throws<ArgumentException>(() => service.GetSessionDuration(sessionId));
        Assert.Equal(TimeSpan.FromSeconds(2), service.GetSessionDuration(sessionId, 3_000));
        Assert.Equal(timer, service.GetTimer(timerId));
        Assert.Equal(session, Assert.Single(service.GetTimerSessions(timerId)));
        service.Pause(timerId, 4_000);
        service.Resume(timerId, 6_000);
        Assert.Equal(TimeSpan.FromSeconds(7), service.GetSessionDuration(sessionId, 10_000));
        Assert.Equal(TimerState.Running, service.GetTimerState(timerId));
        Assert.Equal(6_000, Assert.Single(service.GetTimerSessions(timerId)).UpdatedAtMs);
    }

    [Theory]
    [InlineData(TimerState.Running)]
    [InlineData(TimerState.Paused)]
    public void Single_active_timer_state_and_duration_survive_service_reconstruction(TimerState state)
    {
        long timerId, sessionId;
        using (var service = new TimerService(DatabasePath))
        {
            timerId = service.CreateTimer("Work", 0).Id;
            sessionId = service.Start(timerId, 1_000);
            if (state == TimerState.Paused) service.Pause(timerId, 3_000);
        }
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(state, reopened.GetTimerState(timerId));
        Assert.Equal(TimeSpan.FromSeconds(state == TimerState.Running ? 9 : 2),
            reopened.GetSessionDuration(sessionId, 10_000));
        if (state == TimerState.Paused) reopened.Resume(timerId, 11_000);
        reopened.Stop(timerId, 12_000);
        Assert.Equal(TimeSpan.FromSeconds(state == TimerState.Running ? 11 : 3),
            reopened.GetSessionDuration(sessionId));
        Assert.Equal(TimerState.Idle, reopened.GetTimerState(timerId));
    }

    [Fact]
    public void Two_running_timers_and_one_paused_timer_remain_independent_after_restart()
    {
        long a, b, c, sessionA, sessionB, sessionC;
        using (var service = new TimerService(DatabasePath))
        {
            a = service.CreateTimer("A", 0).Id;
            b = service.CreateTimer("B", 0).Id;
            c = service.CreateTimer("C", 0).Id;
            sessionA = service.Start(a, 1_000);
            sessionB = service.Start(b, 2_000);
            sessionC = service.Start(c, 3_000);
            service.Pause(c, 5_000);
            Assert.Equal(TimerState.Running, service.GetTimerState(a));
            Assert.Equal(TimerState.Running, service.GetTimerState(b));
            Assert.Equal(TimerState.Paused, service.GetTimerState(c));
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(TimerState.Running, reopened.GetTimerState(a));
        Assert.Equal(TimerState.Running, reopened.GetTimerState(b));
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(c));
        Assert.Equal(TimeSpan.FromSeconds(9), reopened.GetSessionDuration(sessionA, 10_000));
        Assert.Equal(TimeSpan.FromSeconds(8), reopened.GetSessionDuration(sessionB, 10_000));
        Assert.Equal(TimeSpan.FromSeconds(2), reopened.GetSessionDuration(sessionC, 10_000));
        reopened.Pause(a, 10_000);
        reopened.Stop(b, 10_000);
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(a));
        Assert.Equal(TimerState.Idle, reopened.GetTimerState(b));
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(c));
        reopened.Resume(c, 11_000);
        Assert.Equal(TimeSpan.FromSeconds(3), reopened.GetSessionDuration(sessionC, 12_000));
        Assert.Equal(TimerState.Paused, reopened.GetTimerState(a));
        Assert.Equal(TimerState.Idle, reopened.GetTimerState(b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_refuses_any_unfinished_session_without_changing_it(bool paused)
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 1_000);
        if (paused) service.Pause(timerId, 2_000);
        var before = service.GetTimer(timerId);
        var error = Assert.Throws<TimerDomainException>(() => service.ArchiveTimer(timerId, 3_000));
        Assert.Equal(TimerError.ActiveTimerCannotBeArchived, error.Error);
        Assert.Equal(before, service.GetTimer(timerId));
        Assert.Equal(paused ? TimerState.Paused : TimerState.Running, service.GetTimerState(timerId));
        Assert.Equal(sessionId, Assert.Single(service.GetTimerSessions(timerId)).Id);
    }

    [Fact]
    public void Archived_timer_must_be_unarchived_before_start()
    {
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("Work", 0).Id;
        service.ArchiveTimer(id, 1_000);
        var before = service.GetTimer(id);
        var error = Assert.Throws<TimerDomainException>(() => service.Start(id, 2_000));
        Assert.Equal(TimerError.ArchivedTimerCannotStart, error.Error);
        Assert.Equal(before, service.GetTimer(id));
        Assert.Empty(service.GetTimerSessions(id));
        service.UnarchiveTimer(id, 3_000);
        service.Start(id, 4_000);
        Assert.Equal(TimerState.Running, service.GetTimerState(id));
    }

    [Fact]
    public void Database_enforces_unique_open_intervals_and_foreign_keys()
    {
        using var service = new TimerService(DatabasePath);
        var timerId = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(timerId, 1_000);
        service.Pause(timerId, 2_000);
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar());
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "UPDATE sessions SET stopped_at_ms = started_at_ms WHERE id = $session;";
        command.Parameters.AddWithValue("$session", sessionId);
        Assert.Equal(275, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteExtendedErrorCode);
        command.CommandText = "UPDATE pauses SET resumed_at_ms = paused_at_ms WHERE session_id = $session;";
        Assert.Equal(275, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteExtendedErrorCode);
        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO sessions(timer_id, started_at_ms, created_at_ms, updated_at_ms)
            VALUES ($timer, 3000, 3000, 3000);
            """;
        command.Parameters.AddWithValue("$timer", timerId);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
        command.CommandText = "INSERT INTO pauses(session_id, paused_at_ms) VALUES ($session, 3000);";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$session", sessionId);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
        command.CommandText = "INSERT INTO pauses(session_id, paused_at_ms) VALUES (-1, 3000);";
        command.Parameters.Clear();
        Assert.Equal(19, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
        Assert.Equal(TimerState.Paused, service.GetTimerState(timerId));
        Assert.Equal(TimeSpan.FromSeconds(1), service.GetSessionDuration(sessionId));
    }

    [Theory]
    [InlineData("Start", TimerState.Idle)]
    [InlineData("Pause", TimerState.Running)]
    [InlineData("Resume", TimerState.Paused)]
    [InlineData("Stop", TimerState.Running)]
    [InlineData("Stop", TimerState.Paused)]
    public void Persistence_failure_rolls_back_entire_transition(string operation, TimerState initialState)
    {
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("Work", 0).Id;
        if (initialState != TimerState.Idle) service.Start(id, 1_000);
        if (initialState == TimerState.Paused) service.Pause(id, 2_000);
        var timer = service.GetTimer(id);
        var sessions = service.GetTimerSessions(id).ToArray();
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        // The timer update is last: fail after earlier interval writes to prove rollback.
        command.CommandText = """
            CREATE TRIGGER fail_timer_update BEFORE UPDATE ON timers
            BEGIN SELECT RAISE(ABORT, 'test write failure'); END;
            """;
        command.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => Apply(service, id, operation, 3_000));
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(timer, reopened.GetTimer(id));
        Assert.Equal(sessions, reopened.GetTimerSessions(id).ToArray());
        Assert.Equal(initialState, reopened.GetTimerState(id));
        if (sessions.Length > 0)
            Assert.Equal(TimeSpan.FromMilliseconds(initialState == TimerState.Paused ? 1_000 : 3_000),
                reopened.GetSessionDuration(sessions[0].Id, 4_000));
    }

    [Fact]
    public void Backward_timestamp_is_rejected_without_mutation()
    {
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("Work", 0).Id;
        var sessionId = service.Start(id, 2_000);
        var before = service.GetTimer(id);
        var error = Assert.Throws<TimerDomainException>(() => service.Pause(id, 1_000));
        Assert.Equal(TimerError.InvalidTimestamp, error.Error);
        Assert.Equal(before, service.GetTimer(id));
        Assert.Equal(TimerState.Running, service.GetTimerState(id));
        service.Pause(id, 3_000);
        error = Assert.Throws<TimerDomainException>(() => service.Resume(id, 2_000));
        Assert.Equal(TimerError.InvalidTimestamp, error.Error);
        Assert.Equal(TimerState.Paused, service.GetTimerState(id));
        error = Assert.Throws<TimerDomainException>(() => service.GetSessionDuration(sessionId, 2_000));
        Assert.Equal(TimerError.InvalidTimestamp, error.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_are_rejected_without_changing_timer(string name)
    {
        using var service = new TimerService(DatabasePath);
        var timer = service.CreateTimer("Work", 0);
        Assert.Throws<ArgumentException>(() => service.CreateTimer(name, 1_000));
        Assert.Throws<ArgumentException>(() => service.RenameTimer(timer.Id, name, 1_000));
        Assert.Equal(timer, service.GetTimer(timer.Id));
        Assert.Equal(1, service.CreateTimer("Next", 2_000).SortOrder);
    }

    [Fact]
    public void Missing_entities_return_clear_domain_errors()
    {
        using var service = new TimerService(DatabasePath);
        Assert.Equal(TimerError.TimerNotFound,
            Assert.Throws<TimerDomainException>(() => service.Start(999, 0)).Error);
        Assert.Equal(TimerError.TimerNotFound,
            Assert.Throws<TimerDomainException>(() => service.GetTimerState(999)).Error);
        Assert.Equal(TimerError.SessionNotFound,
            Assert.Throws<TimerDomainException>(() => service.GetSessionDuration(999)).Error);
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

    private static void Apply(TimerService service, long timerId, string operation, long atMs)
    {
        switch (operation)
        {
            case "Start": service.Start(timerId, atMs); break;
            case "Pause": service.Pause(timerId, atMs); break;
            case "Resume": service.Resume(timerId, atMs); break;
            case "Stop": service.Stop(timerId, atMs); break;
            default: throw new ArgumentException("Unknown operation.", nameof(operation));
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
