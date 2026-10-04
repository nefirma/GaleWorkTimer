using GaleWorkTimer.Data;
using GaleWorkTimer.Domain;
using Microsoft.Data.Sqlite;

namespace GaleWorkTimer.Services;

public sealed class TimerService : IDisposable
{
    private readonly SqliteConnection connection;

    public TimerService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            Schema.Initialize(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public TimerInfo CreateTimer(string name, long atMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(atMs);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = Command("""
            INSERT INTO timers(name, sort_order, created_at_ms, updated_at_ms)
            VALUES ($name, (SELECT coalesce(max(sort_order) + 1, 0) FROM timers), $at, $at);
            SELECT last_insert_rowid();
            """, transaction, ("$name", name.Trim()), ("$at", atMs));
        var id = Convert.ToInt64(command.ExecuteScalar());
        var timer = ReadTimer(id, transaction);
        transaction.Commit();
        return timer;
    }

    public TimerInfo GetTimer(long timerId) => ReadTimer(timerId, null);

    public IReadOnlyList<TimerInfo> GetTimers()
    {
        using var command = Command("""
            SELECT id, name, is_archived, sort_order, created_at_ms, updated_at_ms
            FROM timers ORDER BY sort_order, id;
            """, null);
        using var reader = command.ExecuteReader();
        var timers = new List<TimerInfo>();
        while (reader.Read())
            timers.Add(new TimerInfo(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)));
        return timers;
    }

    public void RenameTimer(long timerId, string name, long atMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(atMs);
        using var transaction = connection.BeginTransaction(deferred: false);
        var timer = ReadTimer(timerId, transaction);
        EnsureTimestamp(atMs, timer.UpdatedAtMs);
        Execute("UPDATE timers SET name = $name, updated_at_ms = $at WHERE id = $id;",
            transaction, ("$name", name.Trim()), ("$at", atMs), ("$id", timerId));
        transaction.Commit();
    }

    public void ArchiveTimer(long timerId, long atMs) => SetArchived(timerId, true, atMs);
    public void UnarchiveTimer(long timerId, long atMs) => SetArchived(timerId, false, atMs);

    private void SetArchived(long timerId, bool archived, long atMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(atMs);
        using var transaction = connection.BeginTransaction(deferred: false);
        var timer = ReadTimer(timerId, transaction);
        EnsureTimestamp(atMs, timer.UpdatedAtMs);
        if (archived)
        {
            using var query = Command("SELECT 1 FROM sessions WHERE timer_id = $id AND stopped_at_ms IS NULL;",
                transaction, ("$id", timerId));
            if (query.ExecuteScalar() is not null)
                throw new TimerDomainException(TimerError.ActiveTimerCannotBeArchived, "Stop the timer before archiving it.");
        }
        Execute("UPDATE timers SET is_archived = $archived, updated_at_ms = $at WHERE id = $id;",
            transaction, ("$archived", archived), ("$at", atMs), ("$id", timerId));
        transaction.Commit();
    }

    public IReadOnlyList<SessionInfo> GetTimerSessions(long timerId)
    {
        ReadTimer(timerId, null);
        using var command = Command("""
            SELECT id, timer_id, started_at_ms, stopped_at_ms, created_at_ms, updated_at_ms
            FROM sessions WHERE timer_id = $timer ORDER BY started_at_ms, id;
            """, null, ("$timer", timerId));
        using var reader = command.ExecuteReader();
        var sessions = new List<SessionInfo>();
        while (reader.Read())
            sessions.Add(ReadSession(reader));
        return sessions;
    }

    public TimeSpan GetSessionDuration(long sessionId, long? snapshotAtMs = null) =>
        GetSessionSnapshot(sessionId, snapshotAtMs).ActiveDuration;

    public SessionSnapshot GetSessionSnapshot(long sessionId, long? snapshotAtMs = null)
    {
        // One read transaction prevents mixing a session and pauses from different commits.
        using var transaction = connection.BeginTransaction(deferred: true);
        using var sessionQuery = Command("""
            SELECT id, timer_id, started_at_ms, stopped_at_ms, created_at_ms, updated_at_ms
            FROM sessions WHERE id = $id;
            """, transaction, ("$id", sessionId));
        SessionInfo session;
        using (var reader = sessionQuery.ExecuteReader())
        {
            if (!reader.Read())
                throw new TimerDomainException(TimerError.SessionNotFound, $"Session {sessionId} does not exist.");
            session = ReadSession(reader);
        }
        using var pauseQuery = Command("""
            SELECT paused_at_ms, resumed_at_ms FROM pauses
            WHERE session_id = $id ORDER BY paused_at_ms, id;
            """, transaction, ("$id", sessionId));
        var pauses = new List<(long Start, long? End)>();
        using (var reader = pauseQuery.ExecuteReader())
            while (reader.Read())
                pauses.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1)));

        long end;
        if (session.StoppedAtMs is long stoppedAtMs)
            end = stoppedAtMs;
        else
        {
            var openPause = pauses.LastOrDefault(p => p.End is null);
            end = snapshotAtMs ?? (pauses.Any(p => p.End is null) ? openPause.Start
                : throw new ArgumentException("A running session requires an explicit snapshot timestamp.", nameof(snapshotAtMs)));
            EnsureTimestamp(end, session.UpdatedAtMs);
        }
        var activeMs = end - session.StartedAtMs;
        foreach (var pause in pauses)
            activeMs -= (pause.End ?? end) - pause.Start;
        var pausedMs = end - session.StartedAtMs - activeMs;
        transaction.Commit();
        return new SessionSnapshot(session,
            TimeSpan.FromTicks(checked(activeMs * TimeSpan.TicksPerMillisecond)),
            TimeSpan.FromTicks(checked(pausedMs * TimeSpan.TicksPerMillisecond)),
            pauses.Count, pauses.Any(p => p.End is null));
    }

    public long Start(long timerId, long atMs) => Transition(timerId, atMs, TimerAction.Start);
    public void Pause(long timerId, long atMs) => Transition(timerId, atMs, TimerAction.Pause);
    public void Resume(long timerId, long atMs) => Transition(timerId, atMs, TimerAction.Resume);
    public void Stop(long timerId, long atMs) => Transition(timerId, atMs, TimerAction.Stop);

    public TimerState GetTimerState(long timerId)
    {
        using var command = Command("""
            SELECT CASE
                WHEN NOT EXISTS (SELECT 1 FROM sessions s
                    WHERE s.timer_id = t.id AND s.stopped_at_ms IS NULL) THEN 0
                WHEN EXISTS (SELECT 1 FROM sessions s JOIN pauses p ON p.session_id = s.id
                    WHERE s.timer_id = t.id AND s.stopped_at_ms IS NULL
                    AND p.resumed_at_ms IS NULL) THEN 2
                ELSE 1 END
            FROM timers t WHERE t.id = $id;
            """, null, ("$id", timerId));
        var state = command.ExecuteScalar();
        return state is null
            ? throw new TimerDomainException(TimerError.TimerNotFound, $"Timer {timerId} does not exist.")
            : (TimerState)Convert.ToInt32(state);
    }

    private enum TimerAction { Start, Pause, Resume, Stop }

    private long Transition(long timerId, long atMs, TimerAction action)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(atMs);
        using var transaction = connection.BeginTransaction(deferred: false);
        var timer = ReadTimer(timerId, transaction);
        using var sessionQuery = Command("""
            SELECT id, timer_id, started_at_ms, stopped_at_ms, created_at_ms, updated_at_ms
            FROM sessions WHERE timer_id = $timer AND stopped_at_ms IS NULL;
            """, transaction, ("$timer", timerId));
        SessionInfo? session;
        using (var reader = sessionQuery.ExecuteReader())
            session = reader.Read() ? ReadSession(reader) : null;

        using var pauseQuery = Command("""
            SELECT id, paused_at_ms FROM pauses WHERE session_id = $session AND resumed_at_ms IS NULL;
            """, transaction, ("$session", session?.Id));
        long? pauseId = null;
        long pausedAtMs = 0;
        using (var reader = pauseQuery.ExecuteReader())
            if (reader.Read())
            {
                pauseId = reader.GetInt64(0);
                pausedAtMs = reader.GetInt64(1);
            }
        var state = session is null ? TimerState.Idle
            : pauseId is null ? TimerState.Running : TimerState.Paused;
        var allowed = action switch
        {
            TimerAction.Start => state == TimerState.Idle,
            TimerAction.Pause => state == TimerState.Running,
            TimerAction.Resume => state == TimerState.Paused,
            TimerAction.Stop => state != TimerState.Idle,
            _ => false
        };
        if (!allowed)
            throw new TimerDomainException(TimerError.InvalidTransition, $"Cannot {action} timer in state {state}.");
        if (action == TimerAction.Start && timer.IsArchived)
            throw new TimerDomainException(TimerError.ArchivedTimerCannotStart, "Unarchive the timer before starting it.");
        EnsureTimestamp(atMs, timer.UpdatedAtMs);

        long sessionId;
        if (action == TimerAction.Start)
        {
            using var insert = Command("""
                INSERT INTO sessions(timer_id, started_at_ms, created_at_ms, updated_at_ms)
                VALUES ($timer, $at, $at, $at);
                SELECT last_insert_rowid();
                """, transaction, ("$timer", timerId), ("$at", atMs));
            sessionId = Convert.ToInt64(insert.ExecuteScalar());
        }
        else
        {
            sessionId = session!.Id;
            EnsureTimestamp(atMs, session.UpdatedAtMs);
            if (action == TimerAction.Stop && atMs <= session.StartedAtMs)
                throw new TimerDomainException(TimerError.InvalidTimestamp, "Stop must be later than the session start.");
            if (pauseId is not null && atMs <= pausedAtMs)
                throw new TimerDomainException(TimerError.InvalidTimestamp, "Closing a pause requires a timestamp later than Pause.");
            if (action == TimerAction.Pause)
                Execute("INSERT INTO pauses(session_id, paused_at_ms) VALUES ($session, $at);",
                    transaction, ("$session", sessionId), ("$at", atMs));
            else if (pauseId is not null)
                Execute("UPDATE pauses SET resumed_at_ms = $at WHERE id = $pause;",
                    transaction, ("$at", atMs), ("$pause", pauseId));

            if (action == TimerAction.Stop)
                Execute("UPDATE sessions SET stopped_at_ms = $at WHERE id = $session;",
                    transaction, ("$at", atMs), ("$session", sessionId));
            Execute("UPDATE sessions SET updated_at_ms = $at WHERE id = $session;",
                transaction, ("$at", atMs), ("$session", sessionId));
        }
        Execute("UPDATE timers SET updated_at_ms = $at WHERE id = $timer;",
            transaction, ("$at", atMs), ("$timer", timerId));
        transaction.Commit();
        return sessionId;
    }

    private static void EnsureTimestamp(long atMs, long previousMs)
    {
        if (atMs < previousMs)
            throw new TimerDomainException(TimerError.InvalidTimestamp, "Timestamp precedes the last operation.");
    }

    private static SessionInfo ReadSession(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
        reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));

    private TimerInfo ReadTimer(long timerId, SqliteTransaction? transaction)
    {
        using var command = Command("""
            SELECT id, name, is_archived, sort_order, created_at_ms, updated_at_ms
            FROM timers WHERE id = $id;
            """, transaction, ("$id", timerId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new TimerDomainException(TimerError.TimerNotFound, $"Timer {timerId} does not exist.");
        return new TimerInfo(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private void Execute(string sql, SqliteTransaction transaction,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, transaction, parameters);
        command.ExecuteNonQuery();
    }

    public void Dispose() => connection.Dispose();
}
