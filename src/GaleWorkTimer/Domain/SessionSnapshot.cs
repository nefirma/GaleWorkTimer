namespace GaleWorkTimer.Domain;

public sealed record SessionSnapshot(
    SessionInfo Session, TimeSpan ActiveDuration, TimeSpan PausedDuration,
    int PauseCount, bool IsPaused);
