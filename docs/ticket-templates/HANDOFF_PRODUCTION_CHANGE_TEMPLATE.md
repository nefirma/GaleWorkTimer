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
- Ticket type: one authorized production, deploy, migration, or consequential runtime change with rollback.
- Keep the parser-sensitive prefix above unchanged, select exactly one `Recommended Executor`, and remove non-applicable placeholders.
- Local implementation and unknown-fact investigation must already be complete or explicitly included because they are inseparable.
- A provider mutation, schema migration, destructive operation, new host, or non-standard deploy requires exact explicit authorization; otherwise stop for `PHIL_INPUT`.

## Target and Candidate Identity
- Repository: `<exact path>`
- Branch / candidate SHA: `<exact values>`
- Remote host / service / deployment path: `<exact values>`
- Database / provider target: `<exact bounded target or none>`
- Expected currently deployed version: `<SHA / artifact identity>`

## Source of Truth (must read)
- Deployment / operation runbook: `<real path>`
- Architecture / security constraints: `<real paths>`
- Candidate validation evidence: `<real report / commit / PR>`
- Current production baseline: `<real report / status source>`

## Evidence State
### Static facts that may be trusted
- `<deployment topology / immutable candidate fact>` — source: `<path / SHA>`

### Current observations
- Observed at: `<timestamp>`
- `<service / DB / provider / capacity state>` — source: `<safe command / report>`

### Dynamic preconditions to recheck
- `<only state that can invalidate execution or rollback>`

### Expected but not proved
- `NOT_YET_PROVEN: <remaining production assumption>`

## Authorization Boundary
- Scope Paths: ["<exact authorized path or bounded glob>"]
- Forbidden Paths: []

Replace both JSON arrays with approved repository-relative paths, including required
documentation/evidence. Missing scope does not permit all files. Do not use a
repository-wide wildcard to pass a gate. If no workspace change is authorized,
use Scope Paths: [] and explicit Allowed Action Categories without WORKSPACE_MUTATION
or VCS_MUTATION. Path scope never authorizes a production operation by itself.
Verify the promoted IR matches these declarations before execution.

### Authorized production changes
- `<exact deploy / migration / service / provider operation>`
- Allowed target and maximum count: `<host / row / operation limit>`

### Not authorized
- `<other hosts / broad cleanup / unrelated services / alternate provider actions>`
- Secret output, raw credential transport, or unreviewed destructive cleanup.

### Approval checkpoint
- Pre-approved standard path: `<exact script / delegated mechanism or none>`
- Requires `PHIL_INPUT` before execution: `<non-standard consequential action or none>`

## Safety Invariants
- Candidate identity remains exact.
- Backup / recovery evidence exists before the consequential change.
- No unrelated service, data, firewall, provider, or repository state is modified.
- Secrets and private data remain redacted.
- Final state is either proved new-good or proved rolled back-good.

## Authorized Execution Mechanism
- Delegated path: `<cfg-dev-ops / cfg-adb-ops / standard deploy script / exact mechanism>`
- Mandatory order:
  1. Capture bounded pre-state and verify preconditions.
  2. Prepare backup / rollback material.
  3. Execute exactly one authorized change.
  4. Run the minimum production smoke that can decide accept vs rollback.
  5. Accept new state or execute rollback.
  6. Prove final state.
- Do not substitute another host, artifact, script, transport, or target.

## Acceptance Decision
### PASS only if
- [ ] Exact candidate is active at the exact target.
- [ ] Required production behavior is directly proved.
- [ ] Safety invariants and unrelated-service baseline remain intact.
- [ ] Rollback remains available or is no longer required by the approved runbook.

### ROLLBACK required if
- `<representative regression / startup failure / lost guardrail / data-integrity failure>`

### BLOCKED only if
- Exact target/candidate/authority cannot be proved.
- Backup or rollback precondition is absent.
- Required action exceeds the explicit authorization boundary.

PASS is forbidden while any required condition is `NO` or `NOT_YET_PROVEN`.

## Validation Boundaries
### Local candidate proof already completed
- `<build / tests / review evidence; do not repeat unless candidate changed>`

### Production smoke
- `<minimum real-path command / observable behavior>`

### Unrelated baseline
- `<only services / data surfaces with concrete blast-radius risk>`

Do not rerun passed local matrices when the candidate is unchanged. Prefer one production smoke plus prepared rollback over another synthetic preflight.

## Efficiency and Stop Rules
- Do not rediscover proved candidate or topology facts unless direct evidence contradicts them.
- Do not repeat an unchanged deploy, migration, or smoke command.
- One corrected retry maximum when the cause is concrete and rollback remains safe.
- Stop for `PHIL_INPUT` before any new consequential authority boundary.
- Stop after PASS or proved rollback; do not begin cleanup or the next checkpoint.

## Evidence Contract
- Pre-state: `<safe concise fields>`
- Executed change: `<exact redacted operation identity>`
- Candidate/deployed identity: `<SHA / artifact hash>`
- Smoke result: `<observable result>`
- Final state: `<new-good / rolled-back-good>`
- Unperformed actions: `<list or none>`

## Rollback Plan
- Trigger:
- Exact rollback mechanism:
- Data compatibility / restore source:
- Good-state proof after rollback:

## Deliverables
- Production change or proved rollback.
- Compact redacted operation evidence.
- Per-AC evidence and exact final-state identity.
- No unrelated cleanup or follow-on implementation.
