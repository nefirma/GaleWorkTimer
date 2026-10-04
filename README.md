# GaleWorkTimer — T1: persistent timer core

.NET 10 core with SQLite (`Microsoft.Data.Sqlite`). One production assembly and
one xUnit test project. The production project is a library: T1 adds no UI, CLI,
background worker, Windows integration, network code or elapsed-time polling.

The supplied T1 document is the agreed specification for this stage; the full
product specification was not supplied. T2 is not implemented.

## Build and test

With .NET 10 SDK on PATH, run from the repository root:

```bash
dotnet build
dotnet test
```

For the locally installed SDK in this development environment:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build
dotnet test
```

`global.json` pins SDK 10.0.401 with patch roll-forward. Tests use separate
temporary on-disk databases and remove them after disposing their connections.
No runtime databases or build outputs belong in Git. The existing local
`token-optimizer/` directory is unrelated tooling and is ignored.

## Public API

`GaleWorkTimer.Services.TimerService` opens a database path and is disposable.
The parent directory must already exist. Use one instance at a time on one
thread; independent callers use separate instances/connections to the same DB.

```csharp
using GaleWorkTimer.Services;

using var timers = new TimerService("timers.db");
long atMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var timer = timers.CreateTimer("Work", atMs);
var sessionId = timers.Start(timer.Id, atMs);
var state = timers.GetTimerState(timer.Id); // Running
var duration = timers.GetSessionDuration(sessionId,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
```

Available operations:

- `CreateTimer(name, atMs)` returns `TimerInfo`.
- `GetTimer(timerId)` returns persisted metadata.
- `RenameTimer(timerId, name, atMs)`.
- `ArchiveTimer(timerId, atMs)` / `UnarchiveTimer(timerId, atMs)`.
- `Start(timerId, atMs)` returns the new session ID.
- `Pause(timerId, atMs)` / `Resume(timerId, atMs)` / `Stop(timerId, atMs)`.
- `GetTimerState(timerId)` returns `Idle`, `Running` or `Paused`.
- `GetTimerSessions(timerId)` returns history, including an unfinished session,
  ordered by start timestamp and ID, oldest first.
- `GetSessionDuration(sessionId, snapshotAtMs = null)` returns active `TimeSpan`.

Pass UTC Unix milliseconds at the operation boundary. Names are trimmed and
must not be blank. Negative timestamps are rejected. Mutations cannot go
backwards relative to the timer's last update; session operations also check
the session's last update. Stop must be later than Start; Resume or Stop closing
an open pause must be later than Pause. Other equal timestamps remain valid,
including Pause at Start and Stop at the last Resume. A wall-clock rollback
produces a clear error instead of corrupting interval chronology.

Invalid transitions, missing IDs, archive violations and backwards timestamps
raise `TimerDomainException` with a `TimerError` code. Invalid arguments raise
standard argument exceptions. SQLite failures propagate without being silently
accepted. An archived timer must be unarchived before it can start.

## State and duration

State is queried from persisted intervals:

- No unfinished session: `Idle`.
- Unfinished session without an unfinished pause: `Running`.
- Unfinished session with an unfinished pause: `Paused`.

Duration is session end minus start minus all pause intervals. A running
session requires an explicit snapshot. A paused session can omit the snapshot;
its duration freezes at the open pause, even with a later supplied snapshot.
For an unfinished session, a supplied snapshot must not precede its last state
change (this API does not implement historical snapshots). A stopped session
always uses its stored stop timestamp; an optional snapshot is ignored.

Stopping a paused timer closes both the pause and session at the same timestamp
in the same transaction. The complete Pause-to-Stop interval is excluded.
The specification's 10:00–12:00 example with a 10:45–11:10 pause yields 95 minutes.

## SQLite schema

Schema version 1 is initialized transactionally using `PRAGMA user_version`.
Unknown nonzero schema versions are refused; no destructive migration or
automatic deletion is performed. Initialization uses `PRAGMA foreign_keys = ON`
and `PRAGMA journal_mode = WAL`.

| Table | Fields |
| --- | --- |
| `timers` | `id`, `name`, `is_archived`, `sort_order`, `created_at_ms`, `updated_at_ms` |
| `sessions` | `id`, `timer_id`, `started_at_ms`, nullable `stopped_at_ms`, `created_at_ms`, `updated_at_ms` |
| `pauses` | `id`, `session_id`, `paused_at_ms`, nullable `resumed_at_ms` |

IDs are SQLite integer primary keys. Foreign keys link sessions to timers and
pauses to sessions without cascading deletion. Partial unique indexes enforce
one unfinished session per timer and one unfinished pause per session, including
for direct SQLite writers. Additional indexes support ordered interval history.
Checks reject blank names, invalid archive flags and negative/inverted local
timestamp intervals. No state column or elapsed counter is stored.

Every mutation uses an immediate SQLite transaction. This serializes the
read-check-write sequence across connections; partial unique indexes independently
protect unfinished-interval uniqueness. Duration reads use one read transaction
so session and pause data come from the same committed snapshot. Connections
disable pooling, so disposal closes the real database handle.

Archiving refuses any unfinished session and never deletes history. No deletion
API is provided. Sort order is assigned incrementally at creation; editing order
is not part of T1.

## Validation boundary

Tests cover required states and durations, all invalid transitions, independent
timers, reconstruction from the same database, archive/history semantics, SQLite
constraints, and injected persistence failures after interval writes to prove
transaction rollback. SQLite-level tests intentionally exercise storage
constraints and failure atomicity; ordinary semantic tests use the public API.

Windows 11 runtime/packaging, Avalonia and abrupt process-kill/power-loss testing
are not validated by T1's Linux service-reconstruction tests.
