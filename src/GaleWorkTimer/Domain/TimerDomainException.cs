namespace GaleWorkTimer.Domain;

public enum TimerError
{
    TimerNotFound,
    SessionNotFound,
    InvalidTransition,
    ActiveTimerCannotBeArchived,
    ArchivedTimerCannotStart,
    InvalidTimestamp
}

public sealed class TimerDomainException(TimerError error, string message) : InvalidOperationException(message)
{
    public TimerError Error { get; } = error;
}
