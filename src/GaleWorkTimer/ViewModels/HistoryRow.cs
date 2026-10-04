using GaleWorkTimer.Domain;

namespace GaleWorkTimer.ViewModels;

public sealed record HistoryRow(string TimerName, SessionSnapshot Snapshot, string ColorKey = TimerPalette.DefaultKey)
{
    public bool IsStopped => Snapshot.Session.StoppedAtMs is not null;
    public bool IsPaused => !IsStopped && Snapshot.IsPaused;
    public bool IsRunning => !IsStopped && !Snapshot.IsPaused;
    public string Status => IsStopped ? "Stopped" : IsPaused ? "Paused" : "Running";
    public string StartText => FormatTimestamp(Snapshot.Session.StartedAtMs);
    public string StopText => Snapshot.Session.StoppedAtMs is long stopped ? FormatTimestamp(stopped) : Status;
    public string ActiveText => FormatDuration(Snapshot.ActiveDuration);
    public string PausedText => FormatDuration(Snapshot.PausedDuration);

    public static string FormatTimestamp(long atMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(atMs).ToLocalTime().ToString("g");

    public static string FormatDuration(TimeSpan duration)
    {
        var minutes = duration.Ticks / TimeSpan.TicksPerMinute;
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }
}
