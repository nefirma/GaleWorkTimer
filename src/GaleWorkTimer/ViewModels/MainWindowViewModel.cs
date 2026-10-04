using System.ComponentModel;
using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;

namespace GaleWorkTimer.ViewModels;

public sealed record TimerRow(long Id, string Name, TimerState State)
{
    public bool CanStart => State == TimerState.Idle;
    public bool CanPause => State == TimerState.Running;
    public bool CanResume => State == TimerState.Paused;
    public bool CanStop => State != TimerState.Idle;
}

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly TimerService service;
    public IReadOnlyList<TimerRow> Timers { get; private set; } = [];
    public IReadOnlyList<TimerRow> ArchivedTimers { get; private set; } = [];
    public IReadOnlyList<HistoryRow> History { get; private set; } = [];
    public long? HistorySnapshotAtMs { get; private set; }
    public string SnapshotText => HistorySnapshotAtMs is long atMs
        ? $"Snapshot: {HistoryRow.FormatTimestamp(atMs)} — refresh to update." : "";
    public string ErrorMessage { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(TimerService service)
    {
        this.service = service;
        LoadTimers(); // Startup must fail, not present an empty in-memory substitute.
    }

    public bool CreateTimer(string name, long atMs) => Execute(() => service.CreateTimer(name, atMs));
    public bool RenameTimer(long id, string name, long atMs) => Execute(() => service.RenameTimer(id, name, atMs));
    public bool ArchiveTimer(long id, long atMs) => Execute(() => service.ArchiveTimer(id, atMs));
    public bool RestoreTimer(long id, long atMs) => Execute(() => service.UnarchiveTimer(id, atMs));
    public bool StartTimer(long id, long atMs) => Execute(() => service.Start(id, atMs));
    public bool PauseTimer(long id, long atMs) => Execute(() => service.Pause(id, atMs));
    public bool ResumeTimer(long id, long atMs) => Execute(() => service.Resume(id, atMs));
    public bool StopTimer(long id, long atMs) => Execute(() => service.Stop(id, atMs));

    public bool RefreshHistory(long snapshotAtMs)
    {
        try
        {
            var history = service.GetTimers()
                .SelectMany(timer => service.GetTimerSessions(timer.Id)
                    .Select(session => new HistoryRow(timer.Name, service.GetSessionSnapshot(session.Id, snapshotAtMs))))
                .OrderByDescending(row => row.Snapshot.Session.StartedAtMs)
                .ThenByDescending(row => row.Snapshot.Session.Id).ToArray();
            History = history;
            HistorySnapshotAtMs = snapshotAtMs;
            Changed(nameof(History));
            Changed(nameof(SnapshotText));
            SetError("");
            return true;
        }
        catch (Exception)
        {
            SetError("Could not load history. Check the system clock and try Refresh again. No data was changed.");
            return false;
        }
    }

    private bool Execute(Action operation)
    {
        try
        {
            operation(); // SQLite commits before any visible state is updated.
        }
        catch (Exception exception)
        {
            SetError(exception switch
            {
                TimerDomainException { Error: TimerError.ActiveTimerCannotBeArchived } =>
                    "Stop this timer before removing it from the list.",
                TimerDomainException { Error: TimerError.ArchivedTimerCannotStart } =>
                    "Restore this timer before starting it.",
                TimerDomainException { Error: TimerError.InvalidTransition } =>
                    "This action is not available in the timer's current state.",
                TimerDomainException { Error: TimerError.InvalidTimestamp } =>
                    "The time is not valid for this action. Check the system clock and try again.",
                TimerDomainException => "This timer or session is no longer available.",
                ArgumentException => "Enter a non-empty timer name and a valid timestamp.",
                _ => "Could not save this change. Please try again."
            });
            try { LoadTimers(); }
            catch (Exception) { /* Keep the last persisted view if the DB is unavailable. */ }
            return false;
        }

        try
        {
            LoadTimers();
            SetError("");
        }
        catch (Exception)
        {
            // The write succeeded; do not misreport a subsequent read failure as rollback.
            SetError("The change was saved, but the list could not be reloaded. Restart the application.");
        }
        return true;
    }

    private void LoadTimers()
    {
        var all = service.GetTimers();
        var timers = all.Where(t => !t.IsArchived)
            .Select(t => new TimerRow(t.Id, t.Name, service.GetTimerState(t.Id))).ToArray();
        var archived = all.Where(t => t.IsArchived)
            .Select(t => new TimerRow(t.Id, t.Name, TimerState.Idle)).ToArray();
        Timers = timers;
        ArchivedTimers = archived;
        Changed(nameof(Timers));
        Changed(nameof(ArchivedTimers));
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        Changed(nameof(ErrorMessage));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
