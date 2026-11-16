using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;
using GaleWorkTimer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerBulkDeleteTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-BulkDelete-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");

    public TimerBulkDeleteTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Delete_all_removes_persisted_archive_and_history_but_preserves_every_nonarchived_timer()
    {
        long emptyArchive, withHistory, stopped, running, paused;
        TimerInfo[] nonarchivedBefore;
        SessionInfo[][] nonarchivedSessionsBefore;
        string[] nonarchivedPausesBefore;
        using (var service = new TimerService(DatabasePath))
        {
            emptyArchive = service.CreateTimer("Empty archive", 10).Id;
            withHistory = service.CreateTimer("History", 20).Id;
            service.Start(withHistory, 100);
            service.Pause(withHistory, 200);
            service.Resume(withHistory, 300);
            service.Stop(withHistory, 400);
            service.Start(withHistory, 500);
            service.Stop(withHistory, 600);
            service.ArchiveTimer(emptyArchive, 700);
            service.ArchiveTimer(withHistory, 700);

            stopped = service.CreateTimer("Stopped", 30).Id;
            service.Start(stopped, 100);
            service.Stop(stopped, 200);
            running = service.CreateTimer("Running", 40).Id;
            service.Start(running, 100);
            paused = service.CreateTimer("Paused", 50).Id;
            service.Start(paused, 100);
            service.Pause(paused, 200);
            nonarchivedBefore = service.GetTimers().Where(t => !t.IsArchived).ToArray();
            nonarchivedSessionsBefore = nonarchivedBefore.Select(t => service.GetTimerSessions(t.Id).ToArray()).ToArray();
            nonarchivedPausesBefore = ReadNonarchivedPauses();

            service.DeleteAllArchived();
            Assert.Equal(TimerError.TimerNotFound, Assert.Throws<TimerDomainException>(() => service.GetTimer(emptyArchive)).Error);
            Assert.Equal(TimerError.TimerNotFound, Assert.Throws<TimerDomainException>(() => service.GetTimer(withHistory)).Error);
            Assert.Equal(nonarchivedBefore, service.GetTimers().OrderBy(t => t.Id).ToArray());
            Assert.Equal(nonarchivedSessionsBefore.SelectMany(x => x), service.GetTimers().SelectMany(t => service.GetTimerSessions(t.Id)));
            Assert.Equal(nonarchivedPausesBefore, ReadNonarchivedPauses());
            Assert.Equal(TimerState.Idle, service.GetTimerState(stopped));
            Assert.Equal(TimerState.Running, service.GetTimerState(running));
            Assert.Equal(TimerState.Paused, service.GetTimerState(paused));
        }

        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(nonarchivedBefore, reopened.GetTimers().OrderBy(t => t.Id).ToArray());
        Assert.Equal(nonarchivedSessionsBefore.SelectMany(x => x), reopened.GetTimers().SelectMany(t => reopened.GetTimerSessions(t.Id)));
        Assert.Equal(nonarchivedPausesBefore, ReadNonarchivedPauses());
        Assert.Equal(new[] { stopped, running, paused }.Order(), reopened.GetTimers().Select(t => t.Id).Order());
    }

    [Fact]
    public void Empty_archive_is_a_safe_no_op()
    {
        using var service = new TimerService(DatabasePath);
        var ids = new[] { service.CreateTimer("One", 10).Id, service.CreateTimer("Two", 20).Id };
        var before = service.GetTimers().ToArray();

        service.DeleteAllArchived();

        Assert.Equal(before, service.GetTimers());
        Assert.Equal(ids.Order(), service.GetTimers().Select(t => t.Id).Order());
    }

    [Fact]
    public void Archived_open_session_rejects_the_whole_set_without_mutation()
    {
        using var service = new TimerService(DatabasePath);
        var safe = service.CreateTimer("Safe archived", 0).Id;
        service.ArchiveTimer(safe, 1);
        var open = service.CreateTimer("Open archived", 0).Id;
        service.Start(open, 2);
        using (var database = OpenDatabase())
        using (var command = database.CreateCommand())
        {
            command.CommandText = "UPDATE timers SET is_archived = 1 WHERE id = $id;";
            command.Parameters.AddWithValue("$id", open);
            command.ExecuteNonQuery();
        }
        var before = ReadAllRows();

        var error = Assert.Throws<TimerDomainException>(() => service.DeleteAllArchived());

        Assert.Equal(TimerError.ActiveTimerCannotBeDeleted, error.Error);
        Assert.Equal(before, ReadAllRows());
    }

    [Theory]
    [InlineData("pauses")]
    [InlineData("sessions")]
    [InlineData("timers")]
    public void Write_failure_after_dependent_or_intermediate_delete_rolls_back_all_archived_data(string failingTable)
    {
        using var service = new TimerService(DatabasePath);
        var ids = new[] { CreateArchivedHistory(service, "One"), CreateArchivedHistory(service, "Two") };
        var before = ReadAllRows();
        using (var database = OpenDatabase())
        using (var command = database.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE delete_attempts(id INTEGER NOT NULL);
                CREATE TRIGGER fail_bulk_delete AFTER DELETE ON {failingTable}
                BEGIN
                    INSERT INTO delete_attempts VALUES (1);
                    SELECT CASE WHEN (SELECT count(*) FROM delete_attempts) = {(failingTable == "timers" ? 2 : 1)}
                        THEN RAISE(ABORT, 'injected bulk delete failure') END;
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => service.DeleteAllArchived());

        Assert.Equal(before, ReadAllRows());
        Assert.All(ids, id => Assert.True(service.GetTimer(id).IsArchived));
    }

    [Fact]
    public void View_model_delete_all_refreshes_archived_and_loaded_history_only_after_commit()
    {
        using var service = new TimerService(DatabasePath);
        var model = new MainWindowViewModel(service, 0);
        Assert.False(model.CanDeleteAllArchived);
        var archived = CreateArchivedHistory(service, "Archived");
        var keep = service.CreateTimer("Keep", 10).Id;
        service.Start(keep, 20);
        service.Stop(keep, 30);
        Assert.True(model.RefreshTimers(60));
        Assert.True(model.CanDeleteAllArchived);
        Assert.True(model.RefreshHistory(60));
        var keptHistory = model.History.Single(row => row.Snapshot.Session.TimerId == keep);

        Assert.True(model.DeleteAllArchivedTimers(50));

        Assert.Empty(model.ArchivedTimers);
        Assert.Equal(keep, Assert.Single(model.Timers).Id);
        Assert.Same(keptHistory, Assert.Single(model.History));
        Assert.DoesNotContain(model.History, row => row.Snapshot.Session.TimerId == archived);
        Assert.Equal(TimerError.TimerNotFound, Assert.Throws<TimerDomainException>(() => service.GetTimer(archived)).Error);
    }

    [Fact]
    public void View_model_keeps_archived_projection_and_reports_safe_error_when_bulk_write_rolls_back()
    {
        using var service = new TimerService(DatabasePath);
        var first = CreateArchivedHistory(service, "First");
        var second = CreateArchivedHistory(service, "Second");
        var model = new MainWindowViewModel(service, 60);
        var archivedRows = model.ArchivedTimers;
        using (var database = OpenDatabase())
        using (var command = database.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_bulk_delete BEFORE DELETE ON timers BEGIN SELECT RAISE(ABORT, 'private SQL detail'); END;";
            command.ExecuteNonQuery();
        }

        Assert.False(model.DeleteAllArchivedTimers(70));

        Assert.Same(archivedRows, model.ArchivedTimers);
        Assert.Equal(new[] { first, second }.Order(), model.ArchivedTimers.Select(row => row.Id).Order());
        Assert.Contains("Could not save this change", model.ErrorMessage);
        Assert.DoesNotContain("SQL", model.ErrorMessage);
        Assert.DoesNotContain("private", model.ErrorMessage);
        Assert.All(new[] { first, second }, id => Assert.True(service.GetTimer(id).IsArchived));
    }

    private long CreateArchivedHistory(TimerService service, string name)
    {
        var id = service.CreateTimer(name, 0).Id;
        service.Start(id, 10);
        service.Pause(id, 20);
        service.Resume(id, 30);
        service.Stop(id, 40);
        service.ArchiveTimer(id, 50);
        return id;
    }

    private string[] ReadAllRows()
    {
        using var database = OpenDatabase();
        var rows = new List<string>();
        foreach (var table in new[] { "timers", "sessions", "pauses" })
        {
            using var command = database.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY 1;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        }
        return rows.ToArray();
    }

    private string[] ReadNonarchivedPauses()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT p.* FROM pauses p JOIN sessions s ON s.id = p.session_id JOIN timers t ON t.id = s.timer_id WHERE t.is_archived = 0 ORDER BY p.id;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
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
