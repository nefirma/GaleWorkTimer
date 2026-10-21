# GaleWorkTimer

.NET 10 / Avalonia 12 desktop executable with the accepted T1 SQLite core
(`Microsoft.Data.Sqlite`). One production assembly and one xUnit test project.
Product SSOT: [SPEC.md](SPEC.md). Current source stage: **T2.5**, adding newest-first
Timer lists and one-click Copy Name to the accepted T2.4 functionality.
T2.5 also includes a selected-Timer **Общее время** calculator in `HH:MM`.
No elapsed-time polling; T3 is not implemented.

## Для Гали: Windows 11 x64 — T2.5 Preview

[Скачать GaleWorkTimer-T2.5-win-x64.zip](https://github.com/nefirma/GaleWorkTimer/releases/download/t2.5-preview/GaleWorkTimer-T2.5-win-x64.zip)
· [Страница release](https://github.com/nefirma/GaleWorkTimer/releases/tag/t2.5-preview)
· **[Инструкция для пользователя на русском](USER_GUIDE_RU.md)** (также внутри ZIP).

1. Закрой предыдущую версию через **Exit** или крестик окна.
2. Скачай ZIP и распакуй **все файлы** в отдельную папку T2.5.
3. Запусти `GaleWorkTimer.exe` из этой папки. Отдельно устанавливать .NET не нужно.
4. Новые созданные Timer теперь появляются сверху; кнопка с двумя перекрывающимися
   прямоугольниками справа от имени копирует только имя. Подсказка — **Copy name**.

Данные T2.3/T2.4 остаются в `%LOCALAPPDATA%\NeFirma\GaleWorkTimer\data.db`.
T2.5 использует ту же базу, schema version 2, без миграции. **Не удаляй базу**
при замене программы и не запускай две версии одновременно. База не включена
в ZIP; при первом запуске без существующей базы создаётся новая.

Исходный commit обновлённой T2.5, результаты проверок и SHA-256 ZIP приведены
в release notes. **Windows runtime обновлённой T2.5: NOT_YET_PROVEN** — запуск,
Copy → Paste и калькулятор нужно проверить на Windows.
Предыдущие [T2.4](https://github.com/nefirma/GaleWorkTimer/releases/tag/t2.4-preview)
и [T2.3](https://github.com/nefirma/GaleWorkTimer/releases/tag/t2.3-preview) остаются доступными.

## Stable-install candidate — not published

T-2 adds a manual Windows installer/update candidate; it is not a released Windows build. Do not distribute local artifacts as a release before Phil accepts the candidate.

Build and statically verify the win-x64, self-contained files on CachyOS:

```bash
DOTNET="$HOME/.dotnet/dotnet" bash packaging/build-windows-candidate.sh 2.6.0
```

The script writes `GaleWorkTimer.Setup.exe` and `GaleWorkTimer-win-x64.zip` under `artifacts/windows-candidate/2.6.0/`, refuses to overwrite that output directory, and prints both SHA-256 values. The ZIP contains the complete publish tree and a per-file SHA-256 manifest; hashes prove package integrity, not publisher identity. No artifact is uploaded or published.

When Phil supplies a candidate, run `GaleWorkTimer.Setup.exe`, choose the ZIP, and install/update under `%LOCALAPPDATA%\Programs\GaleWorkTimer`. The stable entry stays `GaleWorkTimer.exe`; application data remains outside the install tree at `%LOCALAPPDATA%\NeFirma\GaleWorkTimer\data.db`. Close GaleWorkTimer before an update. The next stable-entry launch recovers the previous verified payload if a switch was interrupted. The existing public T2.5 ZIP above remains the portable release until a Windows candidate is accepted.

Candidate checks on CachyOS do not prove Windows runtime behavior. W1–W4 remain `NOT_YET_PROVEN`; see [packaging evidence](docs/wiki/packaging.md).

## Run and use

```bash
dotnet run --project src/GaleWorkTimer
```

The default **Timers** tab supports Add, Edit, Start, Pause, Resume, Stop and
Remove from list. Active and archived timers are displayed newest first by
persisted creation timestamp descending, then ID descending; existing records
are not reordered in storage. Every active and
archived timer shows cumulative active **Total** across all its sessions, excluding
pauses. Running totals use one shared explicit snapshot per list refresh and
remain static until startup, Timers navigation, Refresh or a timer action.
No live current-session elapsed is displayed. Totals retain accumulated hours
(for example, `126 h 15 min`), not days. Archived timers can be restored from
the **Archived timers** section. Removing an unfinished timer is refused; Stop
it first. Archive preserves all history. **Delete** is separate and irreversible:
only Stopped timers (including archived) can be deleted, after explicit confirmation
that all history will be removed. Cancel is the safe default. Running/Paused
timers must be stopped manually first. One SQLite transaction deletes dependent
pauses, sessions and the timer; any failure rolls back all records.

A compact Fluent Copy icon beside each active/archived Timer title copies its
exact name to the platform clipboard, without status or Total. Tooltip: **Copy
name**; accessible name: **Copy timer name**. Clipboard errors use the existing
error message area without changing data or refreshing snapshots. The official
Microsoft SVG geometry is rendered natively, with no SVG/icon package dependency;
its [MIT notice](src/GaleWorkTimer/Assets/Icons/LICENSE.txt) is included in build/publish output.

Select active Timer checkboxes and press **Общее время** to sum their cumulative
Total snapshots as `HH:MM` (for example, `126:15`). Pauses are excluded and
overlapping timers accumulate independently. The calculator adds full durations
before truncating to whole minutes. Empty selection gives `00:00`. It does not
refresh or tick; selection changes or a Timer-list refresh clear the old result.
Restore an archived Timer before including it. Selection/results are not persisted.

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
individual history editing/deletion, statistics or CSV. The T-2 Windows installer is a local candidate, not a published/Windows-accepted release. T3 is not implemented.

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
- `GetTimerTotals(snapshotAtMs)` returns derived active totals for all timers,
  including archived and zero-session timers, using one batch loading query.
  No mutable total is persisted.
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
T2.3 tests cover cumulative sessions, multiple pauses, independent overlapping
timers, fixed running snapshots, paused totals, restart and the Timer row projection.
T2.4 tests cover stopped/archived deletion, active-state rejection, dependent-row
rollback, ViewModel history removal and black/white palette contrast in both themes.
T2.5 tests cover creation-time/ID ordering for active/archived timers, exact-name
clipboard writes and synchronous/asynchronous clipboard failures without refreshing data.
Calculator tests cover selected-only sums, duplicate IDs, pause/overlap semantics,
subminute carry, hours above 24 and stale-result invalidation.
GUI launch and interaction need
a real desktop smoke; automated ViewModel tests do not prove pixel layout or
manual interaction. Static Windows preview packaging is validated on Linux;
Windows runtime behavior, later platform integrations and abrupt power loss are
not proven by these checks.
