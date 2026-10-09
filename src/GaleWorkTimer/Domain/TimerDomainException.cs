namespace GaleWorkTimer.Domain;

public enum TimerError
{
    TimerNotFound,
    SessionNotFound,
    InvalidTransition,
    ActiveTimerCannotBeArchived,
    ArchivedTimerCannotStart,
    InvalidTimestamp,
    ActiveTimerCannotBeDeleted
}

public sealed class TimerDomainException(TimerError error, string message) : InvalidOperationException(message)
{
    public TimerError Error { get; } = error;
}
