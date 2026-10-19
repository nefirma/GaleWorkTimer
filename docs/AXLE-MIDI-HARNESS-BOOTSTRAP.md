# AXLE-MIDI-HARNESS-BOOTSTRAP.md

Deterministic, project-agnostic control layer between ticket intent and model
execution. Executable standalone. All schemas/tables here are normative.

Scope premise: this hardens the existing stack (axle-midi engine + cfg-* agents +
template-only operators). It does not rebuild it. New agents introduced: **0**.

Runtime prompt source: `~/.config/opencode/agents/`. Repository documentation is not executable prompt configuration.

---

## 1. Architecture decision (single recommended path)

**One named enforcement point: `policy_gate()` — a pure function in a new module
`axle_midi/policy.py`, called at exactly three physical choke points that already
exist in the harness:**

| Choke point | When | What it enforces |
|---|---|---|
| CP-1 canonical `WorkflowService` runtime adapter | before LLM spawn | compiles Ticket IR × project profile × tier → per-run capability set (generated agent permissions). Capabilities the task class does not need are structurally absent. |
| CP-2 every canonical state-changing service action | before the mutation executes | LLM output is a proposal; `policy_gate(ticket_ir, action, context)` returns a decision; the action runs only on ALLOW/ALLOW_WITH_EVIDENCE (with evidence present). |
| CP-3 raw tool calls inside an OpenCode session (bash adb/ssh/rm/curl…) | at tool invocation | enforced by the capability compiler output (bash pattern permissions in the generated agent config). Where the installed OpenCode cannot express a needed pattern → NEEDS_OPENCODE_PLUGIN (`tool.execute.before` hook), listed in §11. |

Tradeoff (one line): a plugin-based bash interceptor is stronger than compiled
permissions but adds a runtime dependency and a spike risk; **pick the permission
compiler as the safety boundary now** because it is enforceable today with zero
new runtime components; plugin is a later hardening step.

Invariant: **privileges attach to task class, never to model tier.** A smarter
model gets more reasoning budget, not more side-effect rights.

---

## 2. Action side-effect taxonomy (normative)

Policy decisions use these categories. Tool names (`git`, `tea`, `adb`, `ssh`,
`docker`, `curl`, `npm`, `gradle`, browser, API clients) are only *examples* of a
category, never the category itself.

| Category | Definition | Example concrete tools |
|---|---|---|
| `READ_ONLY_LOCAL` | reads workspace/filesystem, no mutation | `cat`, `grep`, `git status`, graphify query |
| `READ_ONLY_REMOTE` | reads remote state, no mutation | `git fetch`, Gitea GET, `curl -X GET`, `adb shell dumpsys` |
| `WORKSPACE_MUTATION` | writes inside the bound ticket workspace | edit/write tool, codegen, `npm install` (lockfile) |
| `VCS_MUTATION` | changes VCS/remote-visible history or workflow state | `git commit/push`, `tea pulls create`, promote/review/merge |
| `EXTERNAL_SIDE_EFFECT` | mutates anything outside workspace+VCS | `adb install`, `ssh`, `docker run`, POST to external API, email send |
| `HUMAN_PHYSICAL_ACTION` | irreducible human step | tap on device, accept call, plug hardware |
| `APPROVAL_REQUIRED_ACTION` | ticket/profile marks it Phil-gated regardless of category | anything listed in `human_gates` |
| `CREDENTIAL_BEARING_ACTION` | command carries/uses a credential | authed curl, `ssh` with key, token-bearing CLI |
| `SECRET_READING_ACTION` | reads secret material | `cat .env`, keystore read, provisioning payloads |
| `PRODUCTION_OR_CUSTOMER_IMPACTING_ACTION` | affects prod/customer-visible systems | deploy to prod host, prod DB migration, store release |
| `DESTRUCTIVE_OR_IRREVERSIBLE_ACTION` | not cheaply reversible | `rm -rf`, force-push, DB drop, device wipe |
| `COST_BEARING_ACTION` | spends money/tokens beyond current session | spawning LLM runs, paid API calls, cloud resources |

One action may carry multiple categories; policy evaluates the union (strictest wins).

---

## 3. Ticket IR schema (JSON)

Stored as `docs/tickets/<ID>.ir.json` next to the markdown ticket; generated at
promotion time (deterministic extraction + explicit fields in the ticket
template) and validated by the canonical Ticket IR service.

```json
{
  "ir_version": 1,
  "ticket_id": "T-123",
  "project": "chartercost",
  "task_class": "scoped_feature",
  "title": "…",
  "goal": "…",
  "scope_paths": ["src/billing/**", "tests/billing/**"],
  "forbidden_paths": ["infra/**", ".github/**"],
  "allowed_action_categories": [
    "READ_ONLY_LOCAL", "READ_ONLY_REMOTE", "WORKSPACE_MUTATION", "VCS_MUTATION"
  ],
  "forbidden_action_categories": [
    "EXTERNAL_SIDE_EFFECT", "DESTRUCTIVE_OR_IRREVERSIBLE_ACTION",
    "PRODUCTION_OR_CUSTOMER_IMPACTING_ACTION", "SECRET_READING_ACTION"
  ],
  "human_gates": [
    {"gate": "PHIL_INPUT", "trigger": "auth-bearing validation", "blocking": true}
  ],
  "validation_contract": [
    {"ac_id": "AC1", "kind": "test|build|log|diff|artifact|human",
     "command_hint": "pytest tests/billing", "evidence_required": true}
  ],
  "done_definition": {
    "requires_merge": true,
    "requires_reconcile": true,
    "requires_evidence_for_all_ac": true,
    "extra": ["prompt_edit_closure"]
  },
  "model_budget": {"tier": "T1_CHEAP", "max_attempts": 2,
                   "escalation_allowed_to": "T3_REASONING"},
  "risk": "medium",
  "priority": "P2"
}
```

`task_class` enum: `mechanical_edit | scoped_feature | cross_module_feature |
rca_debug | architecture_review | docs | business_artifact | infra_change |
release_deploy | prompt_edit`.

---

## 4. Policy engine: decision enum + precedence

Decision enum (returned by `policy_gate()`):

```
DENY                   — action refused; recorded; job continues or stops per severity
REQUIRE_PHIL_APPROVAL  — pause via PHIL_INPUT relay; resume only with recorded approval
DRY_RUN_ONLY           — execute only the simulated/echo form of the action
ESCALATE_TIER          — refuse at this tier; router may relaunch at higher tier
ALLOW_WITH_EVIDENCE    — execute, but a validation_evidence row must be written or the
                         enclosing workflow transition is later refused
ALLOW                  — execute
```

Precedence rules (evaluated top-down; first match wins):

1. Action category ∈ ticket `forbidden_action_categories` → `DENY`.
2. Action category ∈ {`DESTRUCTIVE_OR_IRREVERSIBLE_ACTION`,
   `PRODUCTION_OR_CUSTOMER_IMPACTING_ACTION`, `CREDENTIAL_BEARING_ACTION`,
   `SECRET_READING_ACTION`} and not explicitly allowed by ticket IR **and**
   project profile → `REQUIRE_PHIL_APPROVAL` (never plain ALLOW by default).
3. Action matches a `human_gates` trigger → `REQUIRE_PHIL_APPROVAL`.
4. Simulation mode active → `DRY_RUN_ONLY`.
5. Action category ∉ `allowed_action_categories` → `DENY` (closed-world default).
6. Action has `evidence_required` linkage in `validation_contract` → `ALLOW_WITH_EVIDENCE`.
7. Otherwise → `ALLOW`.

Layer merge rule (monotonic tightening): global defaults ≤ project profile ≤
ticket IR — each layer may only **restrict**, never widen, the layer above.
A ticket cannot grant what the profile forbids; a profile cannot grant what the
global default forbids.

---

## 5. Persistent event store schema (SQLite, extends `.axle-midi/axle-midi.sqlite3`)

Existing tables reused as-is: `jobs`, `job_events`, `phil_inputs`,
`route_decisions`, `failure_locks`, `context_snapshots`, `merge_claims`,
`opencode_sessions`. New tables:

```sql
CREATE TABLE IF NOT EXISTS model_runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ticket_id TEXT NOT NULL, attempt INTEGER NOT NULL,
  project TEXT NOT NULL, task_class TEXT NOT NULL,
  agent TEXT NOT NULL, model_label TEXT NOT NULL, tier TEXT NOT NULL,
  started_at TEXT NOT NULL, finished_at TEXT,
  outcome TEXT,            -- accepted|changes_required|failed|abandoned|escalated
  tokens_in INTEGER, tokens_out INTEGER, cost_usd REAL,
  UNIQUE(ticket_id, attempt)
);

CREATE TABLE IF NOT EXISTS policy_decisions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL, run_id INTEGER,
  choke_point TEXT NOT NULL,           -- CP1_launch|CP2_command|CP3_tool
  action_verb TEXT NOT NULL,           -- e.g. merge_pr, commit_push, bash:rm
  action_categories TEXT NOT NULL,     -- JSON array of taxonomy categories
  decision TEXT NOT NULL,              -- enum from §4
  rule_matched TEXT NOT NULL,          -- precedence rule id
  mode TEXT NOT NULL DEFAULT 'live',   -- live|simulation
  details_json TEXT
);

CREATE TABLE IF NOT EXISTS action_attempts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL, run_id INTEGER,
  action_verb TEXT NOT NULL, action_categories TEXT NOT NULL,
  requested_by TEXT NOT NULL,          -- agent name or 'human' or 'daemon'
  policy_decision_id INTEGER,
  executed INTEGER NOT NULL DEFAULT 0, exit_ok INTEGER,
  summary_redacted TEXT
);

CREATE TABLE IF NOT EXISTS validation_evidence (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL, run_id INTEGER,
  ac_id TEXT NOT NULL, kind TEXT NOT NULL,
  evidence_ref TEXT NOT NULL,          -- file path, commit sha, log excerpt ref
  label TEXT NOT NULL,                 -- EVIDENCE|HYPOTHESIS|NOT_YET_PROVEN
  verified_by TEXT                     -- harness|reviewer|phil
);

CREATE TABLE IF NOT EXISTS state_transitions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL,
  from_state TEXT NOT NULL, to_state TEXT NOT NULL,
  actor TEXT NOT NULL,                 -- harness|Lead|Senior|BL|Daemon|phil
  gate_passed TEXT,                    -- e.g. reconcile_evidence_gate
  evidence_ids TEXT                    -- JSON array of validation_evidence ids
);

CREATE TABLE IF NOT EXISTS cost_metrics (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL, run_id INTEGER,
  scope TEXT NOT NULL,                 -- session|subagent|retry|review
  tokens_in INTEGER, tokens_out INTEGER, cost_usd REAL, wall_sec INTEGER
);

CREATE TABLE IF NOT EXISTS failure_labels (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL, ticket_id TEXT NOT NULL, run_id INTEGER,
  label TEXT NOT NULL,                 -- missed_requirement|premature_hypothesis|
                                       -- incomplete_multi_part|evidence_confusion|
                                       -- scope_creep|false_done|forbidden_action|
                                       -- skipped_validation|loop|other
  source TEXT NOT NULL,                -- review_verdict|policy|monitor|phil
  note_redacted TEXT
);
```

Routing statistics are a **derived view**, not a hand-maintained table:

```sql
CREATE VIEW IF NOT EXISTS routing_stats AS
SELECT project, task_class, tier,
       COUNT(*) AS n,
       AVG(CASE WHEN outcome='accepted' THEN 1.0 ELSE 0.0 END) AS acceptance_rate,
       AVG(cost_usd) AS avg_cost
FROM model_runs
WHERE finished_at IS NOT NULL
  AND started_at >= datetime('now','-90 days')
GROUP BY project, task_class, tier;
```

---

## 6. Capability compiler — per-tier differences

Compiler input: Ticket IR × project profile × tier. Output: a generated agent
config (`~/.config/opencode/agents/_gen/<ticket>-<attempt>.md`) derived from the
base cfg-* prompt with **permissions replaced**, plus injected env
(`AXLE_INJECTED_CONFIG`, `AXLE_TICKET_IR`). Base prompts are never edited.

| Capability | T0_SCRIPT (no LLM) | T1_CHEAP (deepseek-v4-flash) | T2_MID (deepseek-v4-pro / kimi-k2.6) | T3_REASONING (gpt-5.4/5.5) |
|---|---|---|---|---|
| read/grep/glob/list | n/a | scope_paths only where expressible | same | same |
| write/edit | n/a | allow within workspace | allow within workspace | allow within workspace |
| bash generic | n/a | pattern allowlist from profile; `git *`,`tea *`,`ssh *`,`adb *`,`rm *`,`rmdir *`,`mv *` → deny | same denies | same denies |
| task (subagents) | n/a | allowlist: cfg-*-ops + cfg-code-explorer + cfg-code-reviewer only | same | same |
| webfetch/websearch | n/a | deny unless IR allows READ_ONLY_REMOTE web | deny unless allowed | allow if IR allows |
| external_directory | n/a | deny | deny | deny unless IR allows |
| VCS mutation | via axle-midi CLI only | via cfg-gitea-ops templates → CP-2 | same | same |
| EXTERNAL_SIDE_EFFECT | scripts with rollback only | never direct; delegated ops subagent + CP-2/CP-3 | same | same |
| ambiguity handling | fail | hard gates, bounded retries, PHIL_INPUT early | limited reassessment | full reassessment/RCA |

Key property: **rows differ across tiers only in reasoning-related capability
(webfetch, ambiguity). Side-effect rows are identical** — tier never widens
side-effect rights.

---

## 7. Model routing table (seed) + update rule + escalation triggers

Seed table (used until event history exists for a `(project, task_class)`):

| task_class | default tier | validation owner |
|---|---|---|
| mechanical_edit, docs, business_artifact | T1_CHEAP | deterministic scripts + cheap reviewer |
| scoped_feature, rca_debug (clear repro) | T1_CHEAP | build/test ops + code-reviewer |
| cross_module_feature, rca_debug (ambiguous) | T2_MID | same + Lead review |
| architecture_review, prompt_edit | T3_REASONING | Lead/Phil |
| infra_change, release_deploy | T1_CHEAP execution + REQUIRE_PHIL gates | dry-run first, rollback scripts |

**Update rule (deterministic, runs at route time):**

```
candidates = tiers ordered cheapest→most expensive
for tier in candidates:
    s = routing_stats[project, task_class, tier]      # fallback: task_class only
    if s.n >= 5 and wilson_lower_bound(s.acceptance_rate, s.n, z=1.96) >= 0.70:
        return tier
    if s.n < 5 and tier == seed_tier(task_class):
        return tier                                    # exploration on seed tier
return seed_tier(task_class)
```

Per-ticket escalation (recorded as `model_runs.outcome='escalated'`):

- 2 consecutive `changes_required` on same ticket at same tier → +1 tier.
- ≥3 `DENY` policy decisions in one run (model fighting the harness) → stop run,
  label `forbidden_action`, relaunch same tier once with snapshot; second time → +1 tier.
- monitor loop detection or budget cap breach → stop, snapshot, +1 tier only if
  failure_label is reasoning-related (`premature_hypothesis`, `missed_requirement`).
- `ESCALATE_TIER` policy decision (ambiguity stop-condition) → +1 tier immediately.
- Escalation ceiling: `model_budget.escalation_allowed_to` from IR.

De-escalation: any `(project, task_class)` served by ≥T2 with 5+ accepted runs is
re-probed at the next-lower tier every 10th ticket (1-in-10 exploration).

---

## 8. Workflow state machine

```
Draft(T-NEW) → Ready → Claimed → InProgress → Validation → ReviewRequested
     → Review → { ChangesRequired → InProgress }* → Approved → Merged
     → Reconciled → Done

Overlay states (from any active state): Blocked, WaitingPhil (PHIL_INPUT)
```

Hard rules (enforced at CP-2, recorded in `state_transitions`):

- Only the harness executes `Merged→Reconciled→Done`. Models can only *request*.
- `InProgress` requires `start` workspace binding (existing gate).
- `Validation → ReviewRequested` refused unless every `validation_contract` entry
  with `evidence_required=true` has a `validation_evidence` row labeled EVIDENCE.
- `Reconciled → Done` refused unless `done_definition` satisfied (merge sha
  present, evidence complete, `prompt_edit_closure` checks branch merged+pushed
  for prompt_edit task class).
- `Claimed` is the storm lock: atomic claim + OS-level PID liveness check
  (`/proc/<pid>` verified, not DB state alone) + per-ticket process cap (1) +
  cooldown (no relaunch of same ticket within N minutes of a non-graceful exit)
  + circuit breaker (>2 spawns of same ticket per 30 min → autolaunch halts + alert).

---

## 9. Generic project profile schema (YAML)

Extends `~/.config/axle-midi/projects/<name>.yaml`. Works for Android, backend,
web/API, CLI, infra, docs-heavy projects — differences live in data, not code.

```yaml
project_root: /home/phil/<project>
project_type: android|backend|web_api|cli|infra|docs
gitea_config_engineering: …
gitea_config_business: …
kb: {teamlead: docs/teamlead_KB.md, senior: docs/senior_KB.md, gitea: docs/gitea_KB.md}

capabilities:                      # taxonomy category → concrete grants for THIS project
  READ_ONLY_REMOTE:
    bash_patterns: ["git fetch *", "curl -s https://api.internal/*"]
  WORKSPACE_MUTATION:
    paths: ["src/**", "tests/**", "docs/**"]
  VCS_MUTATION:
    via: canonical_action_service   # only through CP-2 service actions
  EXTERNAL_SIDE_EFFECT:
    delegated_ops: [cfg-dev-ops, cfg-adb-ops]     # or [] for docs projects
    preapproved_scripts: ["scripts/deploy_web_fanout.sh"]  # rollback-capable only
  PRODUCTION_OR_CUSTOMER_IMPACTING_ACTION:
    default: REQUIRE_PHIL_APPROVAL
    hosts: [prod.example.com]
  DESTRUCTIVE_OR_IRREVERSIBLE_ACTION:
    default: REQUIRE_PHIL_APPROVAL
  SECRET_READING_ACTION:
    default: DENY
    exceptions: []                  # explicit paths only

validation:
  build: {command: "./gradlew assembleDebug", owner: cfg-build-ops}   # or npm/pytest/mkdocs
  test: {command: "./gradlew test", owner: cfg-build-ops}
  live: {owner: cfg-adb-ops, devices_allowlist: [...]}                # optional
routing_overrides: {}               # optional per-project seed-tier overrides
```

---

## 10. Dry-run / simulation validation plan

Goal: prove policy blocking with **zero** real ADB/SSH/deploy/Gitea side effects.

1. The canonical policy simulator replays a recorded or hand-written action
   stream through `policy_gate()`, writes `policy_decisions` rows with
   `mode='simulation'`, and executes nothing.
2. Simulation mode returns synthetic `{"ok":true,"dry_run":true,…}` JSON for
   every canonical action without exposing or executing a runtime command.
3. Golden test corpus in `tests/policy_cases/*.json`: each case = ticket IR +
   action + expected decision. Must include at minimum: destructive rm on
   untracked dir → REQUIRE_PHIL_APPROVAL; merge without approved verdict → DENY;
   request-review without evidence rows → DENY; Done without reconcile → DENY;
   prod deploy from T1 ticket lacking category → DENY; duplicate spawn while
   claimed → DENY.
4. The capability compiler probe spawns a throwaway OpenCode session that
   attempts `git status` under a
   `git *: deny` compiled config and asserts the tool call is refused — this
   verifies the installed OpenCode honors bash pattern permissions before we
   rely on them (see §11 remediation if it does not).
5. Storm regression test: simulated autolaunch loop with kill -9 of a fake PID;
   assert claim + /proc check + circuit breaker prevent >1 spawn.

Acceptance for first rollout: all golden cases pass in simulation before any
live enforcement is switched on (`policy.enforce=true` flag per project).

---

## 11. Enforcement feasibility table

Levels: `NOW_SCRIPT_WRAPPER` | `NOW_PERMISSION_COMPILER` | `NEEDS_OPENCODE_PLUGIN` | `PROMPT_ONLY_RISK`.

| Gate / control | Level today (after this plan) | Note / smallest remediation |
|---|---|---|
| Ticket lifecycle transitions (promote/start/review/merge/reconcile/Done) | NOW_SCRIPT_WRAPPER | flow through canonical services; apply `policy_gate()` at CP-2 |
| False-Done prevention (evidence-gated reconcile) | NOW_SCRIPT_WRAPPER | refuse transition without validation_evidence rows |
| Spawn storm (claim, PID check, cap, cooldown, breaker) | NOW_SCRIPT_WRAPPER | enforced by the canonical runtime owner |
| Merge safety (open/mergeable/verdict checks) | NOW_SCRIPT_WRAPPER | exists in `merge_pr`; add verdict check into command, not prompt |
| Workspace scoping / worktree binding | NOW_SCRIPT_WRAPPER | exists (`ensure_worktree`, start gate) |
| Model tier selection & escalation | NOW_SCRIPT_WRAPPER | router in harness, not prompt |
| Subagent chain spawning (`task: deny` on operators) | NOW_PERMISSION_COMPILER | already structural in cfg-*-ops frontmatter |
| Senior raw `git`/`tea`/`ssh`/`adb`/`rm` bash | NOW_PERMISSION_COMPILER (pending §10.4 probe) | today PROMPT_ONLY_RISK; compile bash pattern denies into generated agent config. If probe fails on installed OpenCode → NEEDS_OPENCODE_PLUGIN (`tool.execute.before` wrapper); until then treat as open risk, do not claim safety |
| Destructive FS actions (`rm -rf` etc.) | NOW_PERMISSION_COMPILER + NOW_SCRIPT_WRAPPER | deny raw pattern; sanctioned deletion requires a registered action, classification, and REQUIRE_PHIL |
| Secret reading (`.env`, keys) | NOW_PERMISSION_COMPILER (deny read patterns) — partial | full coverage (read-tool path filters) NEEDS_OPENCODE_PLUGIN; interim: secrets live outside workspace + profile DENY default |
| Credential-bearing remote ops | NOW_SCRIPT_WRAPPER (delegated ops scripts hold credentials; agents never see them) | keep creds in operator env only |
| Prod/customer-impacting deploys | NOW_SCRIPT_WRAPPER | only via preapproved rollback-capable scripts + REQUIRE_PHIL for non-standard |
| Scope creep (forbidden_paths) | NOW_SCRIPT_WRAPPER (diff check at commit/PR time: refuse commit_push if diff touches forbidden_paths) | per-edit blocking would need plugin; commit-time refusal is sufficient gate |
| Cost caps per run | NOW_SCRIPT_WRAPPER | monitor + budget stop policy (exists as observation; add stop rule) |
| Evidence labels in prose reports | PROMPT_ONLY_RISK — acceptable | not a safety boundary; safety-relevant evidence is structural (validation_evidence rows) |
| Output style/language/checkpoint discipline | PROMPT_ONLY_RISK — acceptable | cosmetic, not safety |

No state-changing, destructive, human-gated, security-sensitive,
production-impacting, credential-bearing, or ticket-forbidden action terminates
at PROMPT_ONLY_RISK in this design; the single conditional is the OpenCode
bash-pattern probe (§10.4) with a named plugin remediation.

---

## 12. Failure-prevention matrix

| Failure (source) | Structural control | Level |
|---|---|---|
| Opencode storm, 58+ spawns (incident 2026-07-01) | Claimed state + /proc PID liveness + per-ticket cap + cooldown + circuit breaker in the canonical runtime owner | NOW_SCRIPT_WRAPPER |
| Destructive untracked-dir deletion (graphify-out, KB) | `rm*`/`rmdir*`/`mv*` bash deny in compiled config; sanctioned deletion requires a registered action, classification, and REQUIRE_PHIL | NOW_PERMISSION_COMPILER + NOW_SCRIPT_WRAPPER |
| Prompt edits left unmerged (KB Prompt-Edit Closure) | `prompt_edit` task class: Done gate requires merged-to-master + pushed sha in state_transitions | NOW_SCRIPT_WRAPPER |
| False Done claims | Done only via harness reconcile with evidence rows; models cannot execute Done | NOW_SCRIPT_WRAPPER |
| Skipped validation | ReviewRequested transition refused without EVIDENCE rows per AC | NOW_SCRIPT_WRAPPER |
| Scope creep | commit_push refuses diffs touching forbidden_paths | NOW_SCRIPT_WRAPPER |
| Forbidden side effects by cheap models | closed-world `allowed_action_categories` + compiled capability denies | NOW_PERMISSION_COMPILER |
| Ignoring soft instructions | soft instructions stop being safety controls at all; safety moves into policy_gate | n/a (by design) |

---

## 13. Staged migration plan

| Stage | Content | Risk | Rollback |
|---|---|---|---|
| M0 | DB migration: new tables + view (additive, `IF NOT EXISTS`); start writing model_runs/cost_metrics from existing launch/finish/step events. No behavior change. | none | drop tables |
| M1 | Ticket IR: template gains explicit IR fields; `ir validate` at promote (warn-only), IR emitted for new tickets. Old tickets keep working (IR derived with defaults). | low | flag off |
| M2 | policy_gate at CP-2 in **simulation mode** (`policy.enforce=false`): every decision recorded, nothing blocked. Run ≥1 week across ≥2 projects; review policy_decisions for false DENYs. | low | flag off |
| M3 | Enforce CP-2 per project (`policy.enforce=true`), plus storm controls and evidence-gated transitions. Capability compiler ships behind probe result (§10.4). | medium | per-project flag |
| M4 | Adaptive router live (update rule replaces static seed when n≥5), de-escalation probing on. | low | `routing.adaptive=false` falls back to seed table |

---

## 14. Implementation tickets (5)

### T-HARNESS-1 — Ticket IR + project profile capabilities schema
- **Goal:** machine-readable ticket contract and project capability profiles.
- **Scope:** IR JSON schema + parser/validator (`axle_midi/ir.py`); canonical IR inspection/validation; extend project YAML loader with `capabilities`/`validation` sections; promotion emits `<ID>.ir.json`; update `docs/HANDOFF_TICKET_TEMPLATE.md` with IR fields.
- **Non-goals:** no enforcement, no routing changes, no prompt edits.
- **Files:** `axle_midi/ir.py` (new), `axle_midi/project.py`, `axle-midi.py` (promote path), `docs/HANDOFF_TICKET_TEMPLATE.md`, `tests/test_axle_midi_ir.py` (new).
- **AC:** (1) `ir validate` passes for a valid IR and fails with exact field errors for 5 invalid fixtures; (2) promote writes IR file for new tickets; (3) legacy ticket without IR fields yields default IR with `task_class=scoped_feature` and closed-world forbidden set; (4) profile loader rejects a capabilities section that *widens* a global default.
- **Validation:** `pytest tests/test_axle_midi_ir.py`; canonical IR validation on one real promoted ticket per project type.
- **Rollback:** delete new module + template fields; IR files inert.
- **Model Budget:** T1_CHEAP (deepseek-v4-flash), max 2 attempts.
- **Do Not Touch:** runtime prompt files in `~/.config/opencode/agents/**`, routing.py, lifecycle/launch paths, cfg-* templates.

### T-HARNESS-2 — Policy engine + CP-2 gate + simulation mode
- **Goal:** the named enforcement point.
- **Scope:** `axle_midi/policy.py` with taxonomy constants, decision enum, precedence rules (§4), monotonic layer merge; `policy_gate()` at every canonical state-changing service boundary; `policy_decisions`/`action_attempts` writes; canonical simulation mode; config flag `policy.enforce` (default false); golden case corpus (§10.3).
- **Non-goals:** no capability compiler, no OpenCode plugin, no live enforcement default-on, no autolaunch changes.
- **Files:** `axle_midi/policy.py` (new), `axle_midi/db.py` (migration), `axle-midi.py` (command wrappers), `tests/test_axle_midi_policy.py` (new), `tests/policy_cases/*.json` (new).
- **AC:** (1) all golden cases return expected decisions; (2) with enforce=false no command behavior changes but decisions are recorded; (3) with enforce=true DENY/REQUIRE_PHIL block the mutation and return structured `{"ok":false,"error":"policy_denied",...}`; (4) REQUIRE_PHIL integrates with existing PHIL_INPUT relay and resume executes the held action exactly once; (5) simulation writes `mode='simulation'` rows and performs zero subprocess side effects (asserted via mock).
- **Validation:** `pytest tests/test_axle_midi_policy.py`; 1-week simulation run on two projects; review recorded false-DENY rate < 5%.
- **Rollback:** `policy.enforce=false`; module removal leaves commands unchanged.
- **Model Budget:** T1_CHEAP, escalation allowed to T3 only for precedence-rule design review.
- **Do Not Touch:** runtime prompt files in `~/.config/opencode/agents/**`, existing canonical workflow semantics, and merge safety checks (keep, don't replace).

### T-HARNESS-3 — Capability compiler + OpenCode probe
- **Goal:** structurally remove capabilities per task class at launch (CP-1/CP-3).
- **Scope:** `axle_midi/capabilities.py`: IR × profile × tier → generated agent file in `~/.config/opencode/agents/_gen/` (base prompt body unchanged; frontmatter permissions replaced, bash pattern map included); the canonical runtime adapter uses the generated agent; the capability probe (§10.4) asserts that installed OpenCode honors bash pattern deny; cleanup of `_gen` files on job end.
- **Non-goals:** no plugin development (if probe fails, ticket outcome is a documented NEEDS_OPENCODE_PLUGIN report + probe kept as regression); no edits to base cfg-* prompts.
- **Files:** `axle_midi/capabilities.py` (new), `axle_midi/launch.py`, `tests/test_axle_midi_capabilities.py` (new).
- **AC:** (1) generated config for a docs ticket contains no adb/ssh/deploy grants and denies `git *` bash; (2) probe result recorded as an event and printed as EVIDENCE line; (3) if probe passes: live throwaway session cannot run `rm` (tool refusal captured in log); (4) if probe fails: launch falls back to base agent + a `capability_compiler_unverified` warning event, and README section documents the plugin remediation; (5) `_gen` files removed after job completion.
- **Validation:** `pytest tests/test_axle_midi_capabilities.py`; canonical capability probe on this machine.
- **Rollback:** flag `capabilities.compile=false` → launches use base agents as today.
- **Model Budget:** T1_CHEAP, max 2 attempts; probe interpretation may escalate to T2_MID.
- **Do Not Touch:** base prompt bodies in `~/.config/opencode/agents/`; opencode.json global config.

### T-HARNESS-4 — Evidence store + gated transitions + closure gates
- **Goal:** structural Done/validation gates fed by stored evidence.
- **Scope:** `validation_evidence`/`state_transitions`/`failure_labels` writes; canonical evidence ingestion; request-review transition refuses without required EVIDENCE rows; reconcile→Done refuses without done_definition satisfied; `prompt_edit` closure check (merged+pushed sha); commit_push forbidden_paths diff check; storm controls in the canonical runtime owner.
- **Non-goals:** no reviewer automation changes, no new review agents, no TUI work.
- **Files:** `axle_midi/db.py`, `axle-midi.py` (request-review/reconcile/commit-push paths), `axle_midi/autolaunch*.py`, `axle_midi/lifecycle.py`, `tests/test_axle_midi_evidence.py` (new), `tests/test_axle_midi_storm.py` (new).
- **AC:** (1) request-review without evidence rows fails with `evidence_missing` listing exact AC ids; (2) Done unreachable without reconcile + evidence (golden test); (3) prompt_edit ticket cannot complete while branch unmerged (simulated repo test); (4) commit_push refuses forbidden_paths diff naming offending files; (5) storm regression test (§10.5) passes: max 1 live process per ticket under kill -9 loop; (6) all writes are visible through the canonical query service.
- **Validation:** `pytest tests/test_axle_midi_evidence.py tests/test_axle_midi_storm.py`; one real ticket driven end-to-end in a sandbox repo with AXLE_DRY_RUN for remote steps.
- **Rollback:** per-gate flags (`gates.evidence`, `gates.storm`); tables additive.
- **Model Budget:** T1_CHEAP, max 2 attempts.
- **Do Not Touch:** existing `merge_claims` semantics, telegram adapter, runtime prompt files in `~/.config/opencode/agents/`.

### T-HARNESS-5 — Adaptive router + cost feedback
- **Goal:** routing from event history instead of a static tier table.
- **Scope:** populate `model_runs`/`cost_metrics` from launch/finish/step_finish events and review verdicts (`outcome` set at reconcile/changes_required time); `routing_stats` view; update rule + escalation triggers (§7) in `axle_midi/routing.py` behind `routing.adaptive` flag; canonical routing report (per project/task_class: n, acceptance, avg cost, chosen tier); failure_labels written from review verdict category + policy DENY streaks + monitor loop kills.
- **Non-goals:** no automatic model onboarding, no per-token pricing service, no changes to label-based explicit overrides (labels still win as `cli_override`/`gitea_label` do today).
- **Files:** `axle_midi/routing.py`, `axle_midi/db.py`, `axle_midi/lifecycle.py` (outcome hooks), `axle-midi.py` (report cmd), `tests/test_axle_midi_routing_adaptive.py` (new).
- **AC:** (1) with adaptive off, routing byte-identical to today (regression tests pass); (2) synthetic history fixture (20 runs) makes router pick T1 for task_class with 0.9 acceptance and T2 where T1 acceptance 0.3 — asserted via unit test; (3) escalation triggers fire on 2×changes_required and on 3×DENY fixtures; (4) 1-in-10 de-escalation probe occurs in fixture sequence; (5) `routing report` shows real data after one live week.
- **Validation:** `pytest tests/test_axle_midi_routing_adaptive.py tests/test_axle_midi_routing.py`; two-week A/B: adaptive on for one project, report compares cost/ticket vs seed-table baseline.
- **Rollback:** `routing.adaptive=false`.
- **Model Budget:** T1_CHEAP for implementation; T3_REASONING allowed for one design-review pass of the update rule only.
- **Do Not Touch:** `RouteResult` public shape, confidence semantics ("below high → no autolaunch" stays), existing route_decisions table.

---

## 15. Reuse ledger

Kept unchanged (per constraint, nothing dropped): config-based project profiles ·
deterministic KBs · hidden deterministic subagents · template-only command
operators (cfg-gitea-ops et al.) · PHIL_INPUT · EVIDENCE/HYPOTHESIS/NOT_YET_PROVEN ·
repo_preflight · start_ticket workspace gate · review_packet ·
reconcile-before-Done (now evidence-gated, strictly stronger) · model tiers
(now data-driven selection, same tiers). New agents: **0** — every audited failure
was a harness/enforcement gap, not a missing role.
