namespace GaleWorkTimer.Domain;

public sealed record TimerInfo(
    long Id, string Name, bool IsArchived, long SortOrder,
    long CreatedAtMs, long UpdatedAtMs, string ColorKey);
