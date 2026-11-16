# Delete all archived timers

`Delete all` is a destructive action inside the **Archived timers** section. It
is separate from `Archive all`, which only archives eligible stopped timers and
preserves their history.

The bulk delete confirmation warns that every archived timer and its history
will be permanently deleted. Cancel is focused and is the default action;
Escape and closing the dialog cancel. The delete action is disabled when there
are no archived timers.

On explicit confirmation, the service selects the persisted archived set inside
one immediate SQLite write transaction, rejects the whole operation if any
candidate has an open session, then deletes pauses, sessions, and timers in that
order. Any write failure rolls back the whole set. Non-archived timers and their
sessions are not candidates. After commit, timer and loaded-history projections
are refreshed; a later refresh failure is reported as a saved change, not a
rollback.
