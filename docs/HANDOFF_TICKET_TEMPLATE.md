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
- Depends on: [T-…, T-…] — optional; the engine holds this ticket on Ready until every listed ticket is Done (see Workflow Authority).

## Goal
<One sentence: what will be true after this is done?>

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-NEW_<order>_<slug>`.
- Queue order is engine-decided, not board-decided: among Ready tickets the engine launches the first by numeric ticket id (`T-NEW_NNN` sorts after all numeric ids) whose `Depends on` are all Done.
- `Depends on` gating: the workflow DB `terminal_outcome='DONE'` is authoritative; board Done counts only as a fallback for tickets that never ran through AXLE. A known FAILED/CANCELLED or still-active workflow row hard-blocks.
- This project must not reopen already-fixed decisions from the current SSOT/planning pack unless the ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends a parseable `## Workflow Metadata` block to the issue body. The block is machine-written; Lead edits only `Branch` on an explicit reset.

Fields the canonical engine reads:
- `Ticket ID` — ticket identity when the issue title has no `T-N` prefix. Valid forms: `T-N`, `B-N`, `T-NEW_NNN`, `B-NEW_NNN`.
- `Branch` — exact generation branch, must start at the `ticket/<Ticket ID>` boundary (e.g. `ticket/T-13-...`); a missing, pending, or foreign-ticket branch ends registration with `branch_not_ready` — no spawn.
- Gitea labels control autolaunch, not this block: an `interactive` or `no-autolaunch` label makes the engine skip the ticket; `agent:<role>` labels drive routing.
- The `Depends on:` line is parsed from the body text (anywhere), not from this block.

## Template Contract
- Ticket type: semantic / architecture / contract change.
- Use a specialized template instead when the task is a bounded implementation or a production change: `/home/phil/axle-midi/docs/ticket-templates/`.
- The generated ticket must keep the parser-sensitive prefix above unchanged, select exactly one `Recommended Executor`, and remove every non-applicable placeholder or section.
- One ticket must produce one observable outcome. Split independent implementation, live-observation, and production-mutation boundaries into separate tickets.

## Source of Truth (must read)
- Primary Roadmap: `<real path>`
- Product / Project SSOT: `<real path>`
- Architecture / Technical SSOT: `<real path>`
- Data / Contract / Schema SSOT: `<real path if applicable>`
- Security / Privacy / Ops SSOT: `<real path if applicable>`
- Existing related tickets / decisions: `<real paths if applicable>`

Rules:
- Remove non-applicable source lines from the generated ticket.
- If a required source does not exist, record `NOT_YET_PROVEN: <missing source>`; never invent a path or fact.
- If current sources conflict on a decision that changes implementation, stop with `SSOT_DRIFT`.

## Evidence State
### Static facts that may be trusted
- `<fact>` — source: `<path / commit / decision>`

### Current observations
- Observed at: `<timestamp>`
- `<dynamic state>` — source: `<command / report / status artifact>`

### Expected but not proved
- `NOT_YET_PROVEN: <hypothesis>`

Rules:
- Do not present a mutable runtime observation as a permanent canonical fact.
- Recheck only dynamic facts that can invalidate the current ticket.
- If current evidence contradicts a static fact, report the contradiction instead of silently choosing one.

## Spec Alignment
- Matches:
- Conflicts/unknowns:
- Assumptions that this ticket must resolve:

## Authorization and Scope
- Scope Paths: ["<exact authorized path or bounded glob>"]
- Forbidden Paths: []

Replace both JSON arrays with approved repository-relative paths, including required
documentation and evidence. Keep them consistent with the prose; missing scope
does not permit all files. Do not use a repository-wide wildcard to pass a gate.
For a genuinely read-only investigation, use Scope Paths: [] and explicit
Allowed Action Categories without WORKSPACE_MUTATION or VCS_MUTATION; return
findings without writing files. Verify the promoted IR matches these declarations.

### Authorized
- `<read / edit / validation / external action>`
- Git outcome: `AXLE_NORMAL_FLOW | REVIEW_ONLY | <explicit fallback>`

### Not authorized
- `<merge / deploy / provider mutation / DB mutation / other boundary>`

### Files / modules
- Must touch: `<paths or semantic surfaces>`
- May touch only if directly required: `<bounded adjacent scope>`
- Must not touch: `<protected paths or systems>`

## Semantic Contract
### Field / State Inventory
- `<name>` — `raw_source | computed | branch_state | fallback_note`

### Source / Derivation
- `<name>` <- `<source or exact decision rule>`
- Unavailable-data behavior: `<degraded branch or explicit rejection>`

### Semantic Invariants
- `<field / behavior>`:
  - Allowed states/branches:
  - Forbidden semantic collapse:

## Contract / Interfaces
### Inputs
- `<documents / schemas / DTOs / commands>`

### Outputs
- `<stored data / API / UI / docs / notifications>`

### Data formats
- `<type / table / DTO / markdown structure>`

## Authorized Execution Mechanism
- Use: `<one real mechanism available to cfg-senior>`
- Mandatory order only where safety or data integrity depends on it:
  1. `<precondition>`
  2. `<consequential operation>`
  3. `<validation / restoration>`
- Do not substitute another transport, profile, host, or authority path.

## Forbidden Shortcuts
- Do not satisfy semantic AC with names, shape, notes, copied values, or placeholder math.
- Do not collapse distinct outputs to one value unless the contract explicitly permits equality.
- Do not treat string presence or test presence as proof of semantic behavior.

## Acceptance Criteria
PASS is permitted only when every applicable AC is directly proved.

- [ ] AC1 — `<observable semantic result>`
- [ ] AC2 — `<degraded / missing-data behavior>`
- [ ] AC3 — `<rejection / negative behavior>`
- [ ] AC4 — `<security / authorization invariant>`

If a required condition is `NO` or `NOT_YET_PROVEN`, completion is forbidden.

## Required Semantic Proof
- Success fixture/probe:
  - input:
  - expected output:
- Missing-data fixture/probe:
  - input:
  - expected degraded behavior:
- Negative fixture/probe:
  - input:
  - expected rejection:
- Semantic-collapse trap:
  - input:
  - expected distinction:
  - why a shallow implementation fails:

## Validation Boundaries
### Lead Validation Boundary
- `<architecture / SSOT / static facts proved before promotion>`

### Local Validation Boundary
- `<minimum affected build / test / static proof>`

### Live Validation Boundary
- `<smallest live fact required, or none>`

Do not repeat a passing check without a relevant code or environment change.

## Efficiency and Stop Rules
- Do not rediscover trusted evidence unless a live contradiction appears.
- Do not repeat the same command, test, or probe without a relevant state or code change.
- Allow at most one corrected retry after identifying a concrete cause.
- After two failed attempts at the same boundary, stop with exact evidence.
- Stop when all AC pass; do not begin the next project checkpoint.
- Return `BLOCKED` only when continuation requires missing authority, identity, mechanism, or a decision that changes semantics.

## Evidence Contract
- Per-AC evidence: `<path / command result / redacted live fact>`
- Changed files: `<exact list or produced by delivery pack>`
- Safety/final-state evidence: `<applicable invariants>`
- Remaining uncertainty: `<material uncertainty or none>`

## No-Architecture-Drift Rule
- Findings may invalidate assumptions, but cfg-senior must not redesign product semantics, state transitions, security policy, or ticket scope.

## Security Notes
- Leak-prevention constraints:
- PII / operator-data handling constraints:
- Logging constraints:
- Dependency / network policy:

## Rollback Plan
- Revert mechanism:
- Required preserved data/evidence:
- Proved good state after rollback:

## Deliverables
- `<exact docs / code / contract artifacts>`
- Delivery pack with per-AC evidence citations.
- Field-by-field source/derivation table.
- At least one anti-collapse proof when semantics are involved.
