# HANDOFF TICKET: <Short title>

## Meta
- Ticket ID: T-NEW_<order>_<slug> (pre-promotion placeholder; real T-N appears only after workflow promotion)
- Gitea Issue: pending
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min / senior_mid / senior_high
- Executor Mode: low / medium / xhigh
- Why this execution profile is sufficient:
  - ...
- Model Budget:
  - Estimated tokens: ...
  - Reasoning effort: low / medium / xhigh
  - Rationale: why this is the cheapest sufficient model for this ticket
- Components: [PLANNING] [WBM] [DATA] [SCHEMA] [WEB] [BOT] [OPERATOR] [SECURITY] [ANALYTICS] [OPS]
- Priority: P0 / P1 / P2
- Risk level: Low / Medium / High

## Goal
<One sentence: what will be true after this is done?>

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-NEW_<order>_<slug>`.
- Ticket execution order inside a wave is determined by the numeric placeholder prefix: `001`, `002`, `003`, ...
- This project must not reopen already-fixed decisions from the current SSOT/planning pack unless the ticket explicitly says so.

## Context
- Why now / delivery impact:
- Constraints (hard):
- Out of scope (explicit):

## Template Contract
- Ticket type: bounded implementation of an already-decided behavior.
- Do not use this template while root cause, architecture, externally visible semantics, or the required execution mechanism remains unresolved.
- Keep the parser-sensitive prefix above unchanged, select exactly one `Recommended Executor`, and remove non-applicable placeholders.
- One ticket produces one observable implementation outcome; deploy or live-provider proof belongs in another ticket unless inseparable and explicitly authorized.

## Source of Truth (must read)
- Approved design / parent decision: `<real path>`
- Architecture / contract: `<real path>`
- Existing implementation / tests: `<real paths or symbols>`
- Related ticket / PR: `<real path or ID if applicable>`

If sources conflict on behavior, stop with `SSOT_DRIFT`; do not redesign.

## Evidence State
### Static facts that may be trusted
- `<proved root cause or approved design>` — source: `<path / commit / evidence>`

### Current observations
- Repository branch / expected HEAD: `<branch / SHA>`
- Baseline validation: `<proved checks and source>`

### Expected but not proved
- `NOT_YET_PROVEN: <only assumption the implementation must test>`

## Required Behavior
- Success path:
- Failure / degraded path:
- Permission / safety path:
- Behavior that must remain unchanged:

## Authorization and Scope
- Scope Paths: ["<exact authorized path or bounded glob>"]
- Forbidden Paths: []

Replace these parser-readable JSON arrays with the approved repository-relative paths.
Include authorized documentation and evidence paths. Keep them consistent with the
prose below. Promotion requires non-empty Scope Paths for workspace mutation;
AXLE does not infer permission from prose. Scope is authorization, not code review.
Do not use a repository-wide wildcard to pass a gate. Verify the promoted IR
matches these declarations before handoff; discrepancies require the ticket owner.

### Authorized
- Local source/test changes in: `<exact files or bounded modules>`
- AXLE normal Git/PR delivery after validation.

### Not authorized
- Architecture or schema redesign.
- Merge, production deploy, provider action, or production DB mutation unless explicitly listed here.

### Files / modules
- Expected files: `<paths>`
- May touch only if compilation or AC proves necessary: `<bounded adjacent paths>`
- Must not touch: `<protected paths>`

## Authorized Execution Mechanism
- Reuse: `<existing helper / pattern / interface>`
- Implement the smallest semantically correct diff.
- Exact function bodies and ordinary exploration order are executor-owned; the ticket specifies behavior and boundaries, not speculative implementation steps.

## Acceptance Criteria
PASS is permitted only when every applicable AC is directly proved.

- [ ] AC1 — `<observable success behavior>`
- [ ] AC2 — `<failure / degraded behavior>`
- [ ] AC3 — `<unchanged behavior / regression guard>`
- [ ] AC4 — `<scope / permission invariant>`

If any required condition is `NO` or `NOT_YET_PROVEN`, completion is forbidden.

## Validation Boundaries
### Lead Validation Boundary
- Approved design and root cause: `<evidence>`
- Do not repeat during implementation unless contradicted by source.

### Local Validation Boundary
- `<minimum affected build / test>`
- `<one failure-path or regression check when applicable>`

### Live Validation Boundary
- `none` or `<separate exact live fact>`

## Efficiency and Stop Rules
- Do not rediscover the approved design or proved root cause unless source contradicts it.
- Do not repeat the same command or test without a relevant code/environment change.
- After a mechanical correction, rerun only the affected failed check.
- Allow at most one corrected retry after identifying a concrete cause.
- Stop and report `BLOCKED` if correct implementation requires architecture, schema, permissions, or external action outside this ticket.
- Stop immediately after all AC pass; do not start deploy or the next checkpoint.

## Evidence Contract
- Changed files: `<exact paths from final diff>`
- Validation: `<command + concise result>`
- Per-AC evidence: `<artifact / test / source reference>`
- Unperformed external actions: `<list or none>`

## Security Notes
- Trust boundary:
- Secret / PII / logging constraints:
- Dependency / network constraints:

## Rollback Plan
- Revert mechanism:
- Data/schema compatibility:
- Good state after rollback:

## Deliverables
- Minimal implementation diff.
- Focused affected-path validation.
- Delivery pack with per-AC evidence and remaining uncertainty.
