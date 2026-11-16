# Bulk archive stopped timers

The **Archive all** action is available on the Timers tab. It archives every
persisted, unarchived timer with no open session in one immediate SQLite write
transaction. This includes a Stopped timer with no sessions. Running and Paused
timers both have an open session and are excluded; Pause does not make a timer
Stopped.

The service derives the eligible set from persisted timer/session rows inside
the transaction, not from the visible rows or checkbox selection. A stale UI
cannot archive a timer that became Running or Paused. The action assigns one
command timestamp to every eligible timer and rejects a negative timestamp or
one earlier than any eligible timer's last update. Empty and repeated calls are
safe no-ops.

Archive changes only `is_archived` and `updated_at_ms`. It preserves timer
identity, name, color, creation/order metadata, every Session and Pause, History
snapshots, and derived Total. Archived timers remain available to History and
can be restored. **Delete** is separate, permanent, and removes timer history;
Archive all never deletes or stops a timer.

The button is enabled when the current Timers projection contains a Stopped
timer and does not depend on checkbox selection. The service still rechecks
persisted eligibility at click time. All eligible rows commit together; a write
failure rolls back the whole operation and the UI reports failure. After a
successful commit, timer lists refresh as one snapshot and the selected-total
result clears without recalculating existing History duration snapshots.
