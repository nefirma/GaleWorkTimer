using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerDeleteTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-Delete-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");
    public TimerDeleteTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_removes_stopped_or_archived_timer_and_all_dependents_only(bool archived)
    {
        using var service = new TimerService(DatabasePath);
        var id = CreateHistory(service, "Delete");
        var other = CreateHistory(service, "Keep");
        if (archived) service.ArchiveTimer(id, 5000);
        var beforeOther = Rows(other);
        service.DeleteTimer(id);
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(other, Assert.Single(reopened.GetTimers()).Id);
        Assert.Equal(TimerError.TimerNotFound, Assert.Throws<TimerDomainException>(() => reopened.GetTimer(id)).Error);
        Assert.Empty(Rows(id));
        Assert.Equal(beforeOther, Rows(other));
        Assert.False(reopened.GetTimerTotals(6000).ContainsKey(id));
        Assert.Equal(2, reopened.GetTimerSessions(other).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Running_and_paused_deletion_is_rejected_without_any_mutation(bool paused)
    {
        using var service = new TimerService(DatabasePath);
        var id = CreateHistory(service, "Work");
        service.Start(id, 6000);
        if (paused) service.Pause(id, 7000);
        var before = Rows(id);
        var error = Assert.Throws<TimerDomainException>(() => service.DeleteTimer(id));
        Assert.Equal(TimerError.ActiveTimerCannotBeDeleted, error.Error);
        Assert.Equal(before, Rows(id));
        Assert.Equal(paused ? TimerState.Paused : TimerState.Running, service.GetTimerState(id));
    }

    [Theory]
    [InlineData("sessions")]
    [InlineData("timers")]
    public void Failure_after_dependent_deletes_rolls_back_every_field(string failingTable)
    {
        using var service = new TimerService(DatabasePath);
        var id = CreateHistory(service, "Work");
        var before = Rows(id);
        using (var database = OpenDatabase())
        using (var command = database.CreateCommand())
        {
            command.CommandText = $"CREATE TRIGGER fail_delete BEFORE DELETE ON {failingTable} BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => service.DeleteTimer(id));
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(before, Rows(id));
        Assert.Equal(2, reopened.GetTimerSessions(id).Count);
        Assert.Equal(TimeSpan.FromSeconds(3), reopened.GetTimerTotals(6000)[id]);
    }

    private static long CreateHistory(TimerService service, string name)
    {
        var id = service.CreateTimer(name, 0).Id;
        service.Start(id, 0);
        service.Pause(id, 1000);
        service.Resume(id, 2000);
        service.Stop(id, 3000);
        service.Start(id, 4000);
        service.Stop(id, 5000);
        return id;
    }

    // Storage-boundary assertions include every persisted field, including dependent pauses.
    private string[] Rows(long id)
    {
        using var database = OpenDatabase();
        var rows = new List<string>();
        foreach (var sql in new[]
        {
            "SELECT * FROM timers WHERE id = $id ORDER BY id;",
            "SELECT * FROM sessions WHERE timer_id = $id ORDER BY id;",
            "SELECT p.* FROM pauses p JOIN sessions s ON s.id=p.session_id WHERE s.timer_id=$id ORDER BY p.id;"
        })
        {
            using var command = database.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        }
        using var orphanCheck = database.CreateCommand();
        orphanCheck.CommandText = "SELECT count(*) FROM pauses WHERE session_id NOT IN (SELECT id FROM sessions);";
        Assert.Equal(0L, orphanCheck.ExecuteScalar());
        return rows.ToArray();
    }

    private SqliteConnection OpenDatabase()
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, ForeignKeys = true, Pooling = false }.ToString());
        database.Open();
        return database;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
