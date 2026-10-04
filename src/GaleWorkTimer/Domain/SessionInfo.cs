namespace GaleWorkTimer.Domain;

public sealed record SessionInfo(
    long Id, long TimerId, long StartedAtMs, long? StoppedAtMs,
    long CreatedAtMs, long UpdatedAtMs);
