# GaleWorkTimer — T2.4: deletion and stronger timer colors

.NET 10 / Avalonia 12 desktop executable with the accepted T1 SQLite core
(`Microsoft.Data.Sqlite`). One production assembly and one xUnit test project.
Product SSOT: [SPEC.md](SPEC.md); the T2 ticket bounds this stage to basic,
cross-platform UI. No Windows integration, network code or elapsed-time polling.

## Run and use

```bash
dotnet run --project src/GaleWorkTimer
```

The default **Timers** tab supports Add, Edit, Start, Pause, Resume, Stop and
Remove from list. Timers are displayed in persisted sort order and show cumulative
active Total across all sessions, excluding pauses. Running totals are static
snapshots, updated on startup, Timers navigation, Refresh or timer actions;
there is no live current-session counter. Archived timers can be restored from
the **Archived timers** section. Removing an unfinished timer is refused; Stop
it first. Archive preserves all history. **Delete** is separate and irreversible:
only Stopped timers (including archived) can be deleted, after explicit confirmation
that all history will be removed. Cancel is the safe default. Running/Paused
timers must be stopped manually first. One SQLite transaction deletes dependent
pauses, sessions and the timer; any failure rolls back all records.

Create and Edit offer ten large named palette swatches. Saturated timer colors
appear as prominent title backgrounds in Timers, History and Archived timers,
with automatically contrasting black/white text. The body stays neutral and
status colors remain separate. All ten existing symbolic keys still resolve;
no color-data migration is needed. Edit saves Name and Color
atomically, including for archived timers. Old History rows show the current
timer name/color without changing their duration snapshot. New timer defaults
cycle by the number of active timers; upgraded timers receive `blue`.

Timer accents are independent from semantic status colors: **Stopped** (domain
`Idle`) is red, **Paused** amber, **Running** green. Timers and History use the
same Light/Dark status resources. No full-row color fill or arbitrary color input.

**History** is read-only and includes archived timers' sessions, newest first,
with current timer names, local Start/Stop times, status, active/paused durations
and pause count. Opening History or pressing Refresh captures one UTC snapshot;
its values stay fixed until another explicit refresh. Duration display rounds
down to whole minutes; storage/calculations preserve milliseconds.

The database is `NeFirma/GaleWorkTimer/data.db` under .NET's per-user
`Environment.SpecialFolder.LocalApplicationData`: normally
`~/.local/share/NeFirma/GaleWorkTimer/data.db` on Linux and
`%LOCALAPPDATA%\NeFirma\GaleWorkTimer\data.db` on Windows. The directory is created
before opening SQLite. A startup failure shows an error instead of using an
in-memory substitute. Normal UI errors do not show SQL, internal paths or stack
traces. Tests always pass explicit temporary database paths.

Closing the main window exits normally **without stopping open sessions**.
Running and Paused timers reconstruct independently on the next launch. T2 has
no tray, global hotkeys, autostart, notifications, single-instance handling,
individual history editing/deletion, statistics, CSV or installer. T3 is not implemented.

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

- `CreateTimer(name, atMs, colorKey = null)` returns `TimerInfo` including `ColorKey`;
  null chooses the next palette default. Explicit unknown keys are rejected.
- `GetTimer(timerId)` returns persisted metadata.
- `GetTimers()` returns all persisted timers, including archived ones, in sort order.
- `GetTimerTotals(snapshotAtMs)` returns derived active totals for all timers from
  one batch query; no mutable total is persisted.
- `RenameTimer(timerId, name, atMs)`.
- `EditTimer(timerId, name, colorKey, atMs)` atomically saves a valid name/color.
  `RenameTimer` preserves the current color. `TimerPalette` is the canonical
  definition of valid symbolic keys and theme-dependent rendered colors.
- `ArchiveTimer(timerId, atMs)` / `UnarchiveTimer(timerId, atMs)`.
- `DeleteTimer(timerId)` permanently deletes a Stopped timer and all its history
  atomically; an unfinished session raises `ActiveTimerCannotBeDeleted`.
- `Start(timerId, atMs)` returns the new session ID.
- `Pause(timerId, atMs)` / `Resume(timerId, atMs)` / `Stop(timerId, atMs)`.
- `GetTimerState(timerId)` returns `Idle`, `Running` or `Paused`.
- `GetTimerSessions(timerId)` returns history, including an unfinished session,
  ordered by start timestamp and ID, oldest first.
- `GetSessionDuration(sessionId, snapshotAtMs = null)` returns active `TimeSpan`.
- `GetSessionSnapshot(sessionId, snapshotAtMs = null)` returns the session,
  active/paused durations, pause count and open-pause state from one read transaction.
  The existing duration API delegates to this same calculation.

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

Schema version 2 is initialized transactionally using `PRAGMA user_version`.
Existing supported v1 databases migrate in one immediate transaction with
`ALTER TABLE timers ADD COLUMN color_key TEXT NOT NULL DEFAULT 'blue'` and
`PRAGMA user_version = 2`. Existing timer metadata, sessions and pauses are not
rewritten or deleted. Other nonzero schema versions are refused. There is no
generic migration framework. Initialization uses `PRAGMA foreign_keys = ON`
and `PRAGMA journal_mode = WAL`.

| Table | Fields |
| --- | --- |
| `timers` | `id`, `name`, `is_archived`, `sort_order`, `created_at_ms`, `updated_at_ms`, `color_key` |
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

Archiving refuses any unfinished session and never deletes history. Permanent
Timer deletion is explicit, Stopped-only and transactional. Sort order is assigned
incrementally at creation; editing order is not part of T1.

## Validation boundary

Tests cover required states and durations, all invalid transitions, independent
timers, reconstruction from the same database, archive/history semantics, SQLite
constraints, and injected persistence failures after interval writes to prove
transaction rollback. SQLite-level tests intentionally exercise storage
constraints and failure atomicity; ordinary semantic tests use the public API.

T2 tests exercise the application-facing ViewModel without rendering: valid
action mapping, state/metadata persistence, independent timers, safe errors and
failed-write integrity, History projection and explicit immutable snapshots.
All existing tests remain; the schema-version assertion now expects v2.
T2.2 tests cover the real v1 migration, field preservation, color persistence/Edit,
invalid keys, default cycling and independent color/status History projections.
T2.3 tests cover cumulative totals and immutable running snapshots. T2.4 tests
cover stopped/archived deletion, active-state rejection, dependent-row rollback,
ViewModel history removal and black/white palette contrast in both themes.
GUI launch and interaction need
a real desktop smoke; automated ViewModel tests do not prove pixel layout or
manual interaction. Windows integrations/packaging and abrupt power loss are
outside this stage's validation.
