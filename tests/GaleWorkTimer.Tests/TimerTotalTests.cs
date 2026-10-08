using GaleWorkTimer.Services;
using GaleWorkTimer.Domain;
using Xunit;

namespace GaleWorkTimer.Tests;

public sealed class TimerTotalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaleWorkTimer-Total-" + Guid.NewGuid());
    private string DatabasePath => Path.Combine(directory, "data.db");
    public TimerTotalTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Completed_sessions_accumulate_independently_and_survive_reopening_and_archiving()
    {
        const long minute = 60_000;
        long a, b, empty;
        using (var service = new TimerService(DatabasePath))
        {
            a = service.CreateTimer("A", 0).Id;
            b = service.CreateTimer("B", 0).Id;
            empty = service.CreateTimer("Empty", 0).Id;
            service.Start(a, 0);
            service.Stop(a, 60 * minute);
            service.Start(a, 120 * minute);
            service.Stop(a, 210 * minute);
            service.Start(b, 0);
            service.Stop(b, 200 * minute);
            var totals = service.GetTimerTotals(210 * minute);
            Assert.Equal(TimeSpan.FromMinutes(150), totals[a]);
            Assert.Equal(TimeSpan.FromMinutes(200), totals[b]);
            Assert.Equal(TimeSpan.Zero, totals[empty]);
            service.ArchiveTimer(a, 220 * minute);
        }
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(TimeSpan.FromMinutes(150), reopened.GetTimerTotals(300 * minute)[a]);
        Assert.Equal(2, reopened.GetTimerSessions(a).Count);
    }

    [Fact]
    public void Multiple_pauses_across_sessions_are_subtracted_once()
    {
        const long minute = 60_000;
        using var service = new TimerService(DatabasePath);
        var id = service.CreateTimer("Work", 0).Id;
        service.Start(id, 0);
        service.Stop(id, 60 * minute);
        service.Start(id, 120 * minute);
        service.Pause(id, 150 * minute);
        service.Resume(id, 180 * minute);
        service.Stop(id, 210 * minute);
        Assert.Equal(TimeSpan.FromMinutes(120), service.GetTimerTotals(210 * minute)[id]);
        service.Start(id, 240 * minute);
        service.Pause(id, 250 * minute);
        service.Resume(id, 260 * minute);
        service.Pause(id, 280 * minute);
        service.Stop(id, 300 * minute);
        Assert.Equal(TimeSpan.FromMinutes(150), service.GetTimerTotals(300 * minute)[id]);
    }

    [Fact]
    public void Running_snapshot_paused_freeze_resume_and_restart_reuse_active_duration_semantics()
    {
        const long minute = 60_000;
        long id;
        using (var service = new TimerService(DatabasePath))
        {
            Assert.Empty(service.GetTimerTotals(0));
            id = service.CreateTimer("Work", 0).Id;
            service.Start(id, 0);
            service.Stop(id, 300 * minute);
            service.Start(id, 600 * minute);
            Assert.Equal(TimeSpan.FromMinutes(330), service.GetTimerTotals(630 * minute)[id]);
            service.Pause(id, 640 * minute);
            Assert.Equal(TimeSpan.FromMinutes(340), service.GetTimerTotals(640 * minute)[id]);
            Assert.Equal(TimeSpan.FromMinutes(340), service.GetTimerTotals(700 * minute)[id]);
            var error = Assert.Throws<TimerDomainException>(() => service.GetTimerTotals(639 * minute));
            Assert.Equal(TimerError.InvalidTimestamp, error.Error);
            service.Resume(id, 700 * minute);
            Assert.Equal(TimeSpan.FromMinutes(360), service.GetTimerTotals(720 * minute)[id]);
        }
        using var reopened = new TimerService(DatabasePath);
        Assert.Equal(TimeSpan.FromMinutes(380), reopened.GetTimerTotals(740 * minute)[id]);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
