# T-5 Archive all — delivery evidence

## Semantic contract

| Field/state/output | Classification | Source or derivation | Similar outputs |
|---|---|---|---|
| Eligible timer set | computed | Inside `TimerService.ArchiveAllStopped` immediate transaction: persisted `is_archived = 0` and no `sessions.stopped_at_ms IS NULL`; includes zero-session timers. | Same eligibility as individually archiving each timer, but selection/visibility/checkbox state is intentionally irrelevant. |
| `is_archived` | raw_source | Persisted flag changes from 0 to 1 only for the eligible set. | Archive is reversible; Delete is permanently destructive and intentionally distinct. |
| `updated_at_ms` | raw_source | Persisted field receives the one nonnegative command `atMs`; operation rejects if it is earlier than any eligible timer's prior value. | Every eligible timer receives the same command timestamp. |
| Timer and Archived timer lists | computed | One post-commit ViewModel reload derives both lists and states from the database snapshot. | Active/archived order remains the existing creation-time/ID projection. |
| `CanArchiveAllStopped` | computed | Current active-list projection contains at least one `TimerState.Idle` row; independent of selected checkboxes. | This UI enablement hint is not the service's source of eligibility; transaction rechecks persisted state. |
| History and Total | computed | Existing persisted Sessions/Pauses and snapshot calculation. Archive does not edit those inputs or recalculate a saved History snapshot. | History/Total remain available for archived timers; archive does not equal delete. |
| Selected-total result | branch_state | Existing successful timer-list refresh clears it once. | Selection is not consulted by Archive all. |

## Acceptance criteria mapping

| AC | Evidence path | Concrete semantic proof | Result |
|---|---|---|---|
| AC1 | `TimerBulkArchiveTests.cs`; `MainWindowViewModel.cs`; `MainWindow.axaml`; `MainWindow.axaml.cs`; AXLE native-renderer fixture `scripts/galeworktimer-ui-smoke/ArchiveAllSmoke.cs` | Focused/full xUnit and renderer-backed smoke pass. Smoke renders actual `MainWindow`, hit-tests and raises Archive all, checks mixed Stopped/Running/Paused state, History preservation, disabled state, and Restore. Individual `ArchiveTimer` remains separate. | PASS for scoped AC1 smoke; physical mouse delivery and Windows behavior remain outside this fixture. |
| AC2 | `TimerBulkArchiveTests.cs::Archive_all_archives_only_stopped_timers_and_preserves_metadata_sessions_pauses_and_totals` | Fixture covers zero-session Stopped, multiple completed Sessions/Pauses, Running, Paused, and already-archived timers; exact archive/timestamp, state, session, pause, and total comparisons. Focused and full xUnit runs passed. | PASS. |
| AC3 | Same service test | Reopens the isolated database; compares Session and Pause rows and totals, checks archived History count/duration and newest-first order, then restores one timer. Names/colors/created/order metadata are compared through `TimerInfo`. Focused and full xUnit runs passed. | PASS. |
| AC4 | `TimerBulkArchiveTests.cs::Empty_repeat_and_invalid_timestamp_calls_are_safe_and_atomic`; `MainWindowViewModelTests.cs::Archive_all_uses_persisted_state_and_refreshes_timer_projection_once_without_ticking_history` | Empty and repeated calls, negative/backward timestamps, and stale UI that changes one candidate to Running; selection is absent from the service API. Focused and full xUnit runs passed. | PASS. |
| AC5 | `TimerBulkArchiveTests.cs::Failure_after_a_candidate_update_rolls_back_the_entire_bulk_archive`; `MainWindowViewModelTests.cs::Archive_all_write_failure_is_not_reported_as_success_or_partial_archive` | SQLite trigger fails on the second candidate update; reopened records remain unchanged and ViewModel returns false with generic error text. Focused and full xUnit runs passed. | PASS. |
| AC6 | `MainWindowViewModelTests.cs::Archive_all_uses_persisted_state_and_refreshes_timer_projection_once_without_ticking_history`; `MainWindowViewModelTests.cs::Archive_all_reports_saved_when_post_commit_snapshot_reload_fails` | Checks one update notification per timer-list projection, shared snapshot timestamp, selected-total clearing, unchanged History row/snapshot, and committed archive with the explicit saved-but-not-reloaded message when a subsequent snapshot fails. Focused and full xUnit runs passed. | PASS. |
| AC7 | `README.md`; `USER_GUIDE_RU.md`; `docs/wiki/pages/timers-bulk-archive.md` | Describes reversible Archive all, excludes Running/Paused, preserves history, and distinguishes permanent Delete. | Documentation present. |

## Fixture coverage

- Success: mixed persisted states with eligible zero-session and multi-session timers.
- Degraded: empty eligible set and repeated archive call.
- Rejection: negative timestamp, timestamp older than one candidate, and injected SQLite write failure after a prior candidate update.
- Anti-collapse: Paused is not treated as Stopped even when it contributes no new active duration; Running and Paused remain unarchived.
- Semantic collapse trap: the mixed fixture fails if bulk archive archives every unarchived timer or uses one timer's timestamp without validating the whole eligible set.

## Validation and remaining uncertainty

- Source-bound runs (all passed on the same observed pre/post source digest at execution time): focused xUnit `dotnet test tests/GaleWorkTimer.Tests --filter 'FullyQualifiedName~TimerBulkArchiveTests|FullyQualifiedName~Archive_all'` PASS, 6/6; `dotnet build` PASS, 0 warnings; full `dotnet test` PASS, 72 application tests + 57 packaging tests; AXLE native-renderer UI smoke PASS, 1/1. The smoke used only a synthetic temporary database and closed/deleted only its own temporary directory.
- The AXLE fixture source and README were read before use. It configures the actual Avalonia `MainWindow` without a desktop lifetime or ordinary user-profile startup, uses `UsePlatformDetect`, waits for native layout, verifies rendered hit-testing and routed Archive all/Restore actions, and cleans its isolated temp database. It covers the ticket's required button/mixed-state/archive/Restore smoke; it does not prove physical mouse delivery or Windows behavior.
- An earlier non-final focused compile emitted Avalonia AVLN3001 and an earlier build emitted xUnit2013; neither warning recurred in final build/test validation. Native-renderer smoke passed for scoped Archive all behavior; physical mouse delivery and Windows runtime remain NOT_YET_PROVEN.
- Phil instructed: leave `docs/context5.md` unchanged because it is outside ticket/IR `scope_paths`; place the equivalent Senior execution summary in this authorized evidence file. This entry records that resolution; brief, IR, and scope remain unchanged.
- Graphify: unavailable; the assigned worktree has no `graphify-out/graph.json`.
- Current branch: `ticket/T-5-002_archive_all_stopped`. Current HEAD and `refs/remotes/origin/main` both equal the launch authority SHA `693a0e56fb453e61d3d5c2a1c1ca99cc32668564`; current tracked diff and untracked files match the T-5 scope. No T-6/T-7 ticket files or `.env` appear in status/diff.

## Senior execution summary (approved context5 alternative)

- Date: 2026-11-14 local | Ticket: T-5 | Branch: `ticket/T-5-002_archive_all_stopped`.
- Summary: Reviewed the assigned in-scope bulk archive implementation and tests; no code change was needed. Focused tests passed 6/6, build passed, full suite passed 129/129, and native-renderer smoke passed 1/1 against a synthetic database.
- Findings/Quirks: Graphify unavailable because the assigned worktree lacks `graphify-out/graph.json`.
- Risks: Physical mouse delivery and Windows runtime behavior remain NOT_YET_PROVEN; smoke covers the requested native-renderer UI behavior only.
- Validation: Focused xUnit, `dotnet build`, `dotnet test`, and AXLE native-renderer smoke all PASS at execution-time source digest; later change to this evidence summary was documentation-only. No full tests repeated after that docs-only edit, per Phil instruction.
- Scope: Per Phil instruction, `docs/context5.md`, ticket brief, and IR remain unchanged; this scoped evidence file carries the execution summary.
