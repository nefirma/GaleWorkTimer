using Microsoft.Data.Sqlite;

namespace GaleWorkTimer.Data;

internal static class Schema
{
    public static void Initialize(SqliteConnection connection)
    {
        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            pragmas.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt64(command.ExecuteScalar());
        if (version == 0)
        {
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
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        else if (version != 1)
        {
            throw new NotSupportedException($"Unsupported database schema version: {version}.");
        }
        transaction.Commit();
    }
}
