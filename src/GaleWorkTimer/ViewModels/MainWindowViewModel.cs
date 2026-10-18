using System.ComponentModel;
using GaleWorkTimer.Domain;
using GaleWorkTimer.Services;

namespace GaleWorkTimer.ViewModels;

public sealed record TimerRow(long Id, string Name, TimerState State, string ColorKey = TimerPalette.DefaultKey,
    TimeSpan Total = default)
{
    public string TotalText => HistoryRow.FormatDuration(Total);
    public string StatusText => State == TimerState.Idle ? "Stopped" : State.ToString();
    public bool CanStart => State == TimerState.Idle;
    public bool CanDelete => State == TimerState.Idle;
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
    public string SuggestedColorKey => TimerPalette.NextKey(Timers.Count);
    public long TimersSnapshotAtMs { get; private set; }
    public long? HistorySnapshotAtMs { get; private set; }
    public string SnapshotText => HistorySnapshotAtMs is long atMs
        ? $"Snapshot: {HistoryRow.FormatTimestamp(atMs)} — refresh to update." : "";
    public string ErrorMessage { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(TimerService service, long snapshotAtMs)
    {
        this.service = service;
        LoadTimers(snapshotAtMs); // Startup must fail, not present an empty in-memory substitute.
    }

    public bool CreateTimer(string name, long atMs, string? colorKey = null) => Execute(() => service.CreateTimer(name, atMs, colorKey), atMs);
    public bool RenameTimer(long id, string name, long atMs) => Execute(() => service.RenameTimer(id, name, atMs), atMs);
    public bool EditTimer(long id, string name, string colorKey, long atMs) => Execute(() => service.EditTimer(id, name, colorKey, atMs), atMs);
    public bool ArchiveTimer(long id, long atMs) => Execute(() => service.ArchiveTimer(id, atMs), atMs);
    public bool RestoreTimer(long id, long atMs) => Execute(() => service.UnarchiveTimer(id, atMs), atMs);
    public bool DeleteTimer(long id, long snapshotAtMs) => Execute(() => service.DeleteTimer(id), snapshotAtMs);
    public bool StartTimer(long id, long atMs) => Execute(() => service.Start(id, atMs), atMs);
    public bool PauseTimer(long id, long atMs) => Execute(() => service.Pause(id, atMs), atMs);
    public bool ResumeTimer(long id, long atMs) => Execute(() => service.Resume(id, atMs), atMs);
    public bool StopTimer(long id, long atMs) => Execute(() => service.Stop(id, atMs), atMs);

    public async Task<bool> CopyTimerNameAsync(TimerRow timer, Func<string, Task> writeText)
    {
        try
        {
            await writeText(timer.Name);
            SetError("");
            return true;
        }
        catch (Exception)
        {
            SetError("Could not copy the timer name. Please try again.");
            return false;
        }
    }

    public bool RefreshTimers(long snapshotAtMs)
    {
        try
        {
            LoadTimers(snapshotAtMs);
            SetError("");
            return true;
        }
        catch (Exception)
        {
            SetError("Could not load timers. Check the system clock and try Refresh again. No data was changed.");
            return false;
        }
    }

    public bool RefreshHistory(long snapshotAtMs)
    {
        try
        {
            var history = service.GetTimers()
                .SelectMany(timer => service.GetTimerSessions(timer.Id)
                    .Select(session => new HistoryRow(timer.Name, service.GetSessionSnapshot(session.Id, snapshotAtMs), timer.ColorKey)))
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

    private bool Execute(Action operation, long snapshotAtMs)
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
                TimerDomainException { Error: TimerError.ActiveTimerCannotBeDeleted } =>
                    "Stop this timer before deleting it.",
                TimerDomainException { Error: TimerError.ArchivedTimerCannotStart } =>
                    "Restore this timer before starting it.",
                TimerDomainException { Error: TimerError.InvalidTransition } =>
                    "This action is not available in the timer's current state.",
                TimerDomainException { Error: TimerError.InvalidTimestamp } =>
                    "The time is not valid for this action. Check the system clock and try again.",
                TimerDomainException => "This timer or session is no longer available.",
                ArgumentException => "Enter a non-empty timer name, choose a palette color and use a valid timestamp.",
                _ => "Could not save this change. Please try again."
            });
            try { LoadTimers(snapshotAtMs); }
            catch (Exception) { /* Keep the last persisted view if the DB is unavailable. */ }
            return false;
        }

        try
        {
            LoadTimers(snapshotAtMs);
            SetError("");
        }
        catch (Exception)
        {
            // The write succeeded; do not misreport a subsequent read failure as rollback.
            SetError("The change was saved, but the list could not be reloaded. Restart the application.");
        }
        return true;
    }

    private void LoadTimers(long snapshotAtMs)
    {
        var all = service.GetTimers().OrderByDescending(t => t.CreatedAtMs).ThenByDescending(t => t.Id).ToArray();
        var totals = service.GetTimerTotals(snapshotAtMs);
        var timers = all.Where(t => !t.IsArchived)
            .Select(t => new TimerRow(t.Id, t.Name, service.GetTimerState(t.Id), t.ColorKey, totals[t.Id])).ToArray();
        var archived = all.Where(t => t.IsArchived)
            .Select(t => new TimerRow(t.Id, t.Name, TimerState.Idle, t.ColorKey, totals[t.Id])).ToArray();
        Timers = timers;
        ArchivedTimers = archived;
        TimersSnapshotAtMs = snapshotAtMs;
        Changed(nameof(Timers));
        Changed(nameof(ArchivedTimers));
        var byId = all.ToDictionary(timer => timer.Id);
        History = History.Where(row => byId.ContainsKey(row.Snapshot.Session.TimerId)).Select(row =>
        {
            var timer = byId[row.Snapshot.Session.TimerId];
            // Update current metadata without recalculating the explicit duration snapshot.
            return row.TimerName == timer.Name && row.ColorKey == timer.ColorKey ? row
                : row with { TimerName = timer.Name, ColorKey = timer.ColorKey };
        }).ToArray();
        Changed(nameof(History));
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        Changed(nameof(ErrorMessage));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
