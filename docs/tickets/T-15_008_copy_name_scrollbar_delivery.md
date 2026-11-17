# HANDOFF TICKET: Copy-name / scrollbar layout — reviewed fix with UI-evidence waiver

## Meta
- Ticket ID: T-15_008_copy_name_scrollbar_delivery
- Gitea Issue: T-15 / #15
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - One bounded XAML layout fix; product semantics and validation boundary are fixed.
- Model Budget:
  - Estimated tokens: 14000
  - Reasoning effort: low
  - Rationale: existing controls, tests and tooling; no architecture or dependency change.
- Components: [UI] [DOCS]
- Priority: P1
- Risk level: Low

## Goal
Goal: Deliver an active/archived copy-name layout fix that statically separates the copy hit target from the scrollbar while preserving existing behavior; actual overlap removal without renderer remains NOT_YET_PROVEN under Phil's waiver.

## Workflow Authority
- This markdown is authoritative for the brief, AC, scope, validation and authorization.
- Native AXLE promotion assigns the new numeric ID; use this placeholder only before promotion.
- Use the registered `galeworktimer` project and canonical AXLE at `/home/phil/axle-midi`.
- Phil authorizes promotion, ordinary automatic worker execution, review, and product PR merge AFTER successful review and mandatory checks.
- Do not add the new ID to workflow.merge_hold_tickets. Preserve unrelated hold entries, including T-5.
- Do not restore cancelled tickets, including T-12 and T-14. Do not use their branches, worktrees, WIP, IR or workflow state.
- Start the new ticket branch from fresh registered authority base. No dependency on a cancelled ticket or previous draft.
- AXLE owns worker launch, normal rework, review admission, merge gate, merge and completion. Do not manually launch workers or mutate its DB.
- After confirmed merge, Lead coordinates the separately authorized local packaging operation described below. Senior stops at engineering delivery.
- Phil receives the artifact; no sending to Gala, release, upload or publication is authorized.

## Workflow Metadata Block (engine-parsed)
Native promotion writes the real ticket/issue/branch metadata and persisted IR. Do not manually invent or carry forward these identities.

## Context
Phil reports the scrollbar overlapping the copy-name button. Fix layout only; do not redesign clipboard or timer logic.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `docs/TZ.md` Copy Name contract, read-only: exact name, active/archived parity, existing clipboard/error path, no timer/snapshot side effects.
- Current authority-base `src/GaleWorkTimer/Views/MainWindow.axaml`, `README.md`, `USER_GUIDE_RU.md`.
- Read existing handler, VM, resources and tests only as needed to verify unchanged wiring and behavior. Do not modify them.
- Phil's current instruction supplies UI-evidence waiver, automatic-execution/conditional-merge authorization and private post-merge packaging authority.

## Evidence State
- EVIDENCE: registered project galeworktimer, repository Phil/galeworktimer, authority remote origin, base main, resolved by native project resolver.
- Timestamped observation, 2026-11-17: local main and authority base both `1167c33cf906b2f90952e8d64cf4bfc7a392a87e`. Recheck freshness at promotion.
- EVIDENCE on this base: Button.copyName style has Margin 4,0,0,0 and Padding 6. Active and archived buttons share class and Click=CopyTimerName inside the Timers ScrollViewer.
- EVIDENCE: ActiveTimers disables its inner scrollbars. History uses a separate ScrollViewer. Existing archived Delete all control must remain unchanged.
- HYPOTHESIS: overlay scrollbar area is not separated from rightmost copy hit targets. Root cause UNPROVEN until current-base static layout analysis.
- NOT_YET_PROVEN: actual runtime geometry and actual overlap removal without renderer.
- graphify unavailable: no workspace-local graph.json. Use bounded source inspection.

## Spec Alignment
- Preserve product semantics, exact clipboard content, failure behavior, snapshot behavior, timer colors, selection and other controls.
- This is a layout correction and an evidence waiver, not a new product or API contract.
- docs/TZ.md is read-only for both Lead and Senior; no Lead synchronization permission is granted.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Views/MainWindow.axaml", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/copy-name-scrollbar.md"]
- Forbidden Paths: ["Services/**", "**/Services/**", "ViewModels/**", "**/ViewModels/**", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "**/MainWindow.axaml.cs", "docs/TZ.md", "docs/ssot/**"]
- Worker Forbidden Paths: ["docs/TZ.md", "docs/ssot/**"]
- Lead Documentation Paths: []
- Allowed Action Categories: ["READ_ONLY_LOCAL", "WORKSPACE_MUTATION", "VCS_MUTATION"]
- Human Gates: []
- Validation Contract: ["Phil authorizes this new ticket's ordinary automatic worker execution after promotion and product PR merge only after successful review and mandatory checks. Do not add its ID to merge_hold_tickets; preserve unrelated entries.", "Required: dotnet build and dotnet test for the existing solution, with exit codes, test counts, source revision/digest and actual coverage limits. No new dependencies or UI harness.", "Required: static layout reasoning for active and archived rows; scrollbar hidden, visible and expanded; archive collapsed/expanded; long/short names; minimum/normal supported width; Light/Dark. Preserve scrolling and other controls.", "Required: static UI wiring/accessibility proof: same CopyTimerName handler, row data context, icon, tooltip Copy name, accessible name Copy timer name, focusability and theme semantics. Preserve clipboard success/failure and timer/snapshot behavior.", "UI-evidence waiver approved by Phil: manual interaction and automatic renderer-backed checks are NOT required. Do not require UI smoke, screenshots, renderer harness or rendered geometry. Фактическое устранение overlap без renderer остаётся NOT_YET_PROVEN; state this in delivery and documentation. This specific uncertainty does not block waiver-scoped PASS or merge.", "Change only the four Scope Paths. Services/**, ViewModels/**, MainWindow.axaml.cs, docs/TZ.md and docs/ssot/** edits are forbidden. Lead Documentation Paths is empty; Lead has no docs/TZ.md edit authority. Do not modify dependencies, tests or harness/config files.", "README.md, USER_GUIDE_RU.md and docs/wiki/pages/copy-name-scrollbar.md must match the delivered layout, actual checks and waiver. No claim of visual or Windows runtime proof.", "Promoted IR must match the new ticket ID, project galeworktimer, scope and this brief. Never restore or reuse cancelled tickets or their worktrees/WIP."]
- Git outcome: AXLE_NORMAL_FLOW.
- Scope covers engineering source edits only. Native promotion/delivery may produce its own ticket/IR metadata; this is not permission to widen application changes.
- No deploy, customer/user DB changes, service/runtime changes, publishing or distribution.

## Layout / UI Contract
- Separate copy hit target and scrollbar area for both active and archived rows, including expanded/overlay scrollbar state.
- Choose the smallest shared layout fix supported by existing Avalonia and XAML. Justify reserved space or equivalent constraints using current resources; do not invent a scrollbar width without explaining the bound.
- Keep both buttons visible and focusable by layout design. Do not hide buttons, disable scrolling, or move handlers to compensate.
- Constrain long-name text at minimum supported width rather than pushing copy outside the viewport; preserve name content in the row data.
- When no vertical overflow exists, do not force an unnecessary scrollbar. With overflow, retain wheel/drag behavior through existing wiring.
- Preserve icon, tooltip `Copy name`, accessible name `Copy timer name`, row data context, `CopyTimerName` handler, theme foreground and title colors.
- Preserve exact-name clipboard success and existing clipboard error handling. No data/status/snapshot mutation or extra refresh.

## Authorized Execution Mechanism
- Canonical AXLE automatically launches cfg-senior using this ticket's new branch/worktree and senior_min profile.
- Senior changes only the four authorized files and runs existing solution build/tests with the installed .NET SDK.
- Read-only inspection outside mutation scope is permitted for existing UI wiring/resources/tests. If a fix requires a forbidden edit, stop with BLOCKED rather than widening scope.

## Acceptance Criteria
PASS requires every AC below within the explicitly waived UI-evidence boundary.
- [ ] AC1: XAML implements separation of copy hit target and scrollbar area for active and archived rows. Static proof cites containers, relevant constraints/resources and the selected separation mechanism.
- [ ] AC2: Static analysis covers hidden/visible/expanded scrollbar, collapsed/expanded archive, short/long names, minimum/normal width and Light/Dark. Explain why the proposed constraints preserve scrolling, copy availability and avoid horizontal overflow. Do not claim renderer geometry was tested.
- [ ] AC3: Static diff/wiring/accessibility proof preserves existing handler/data context, copy icon, tooltip, accessible name, keyboard focus and theme/title semantics for both buttons. Existing clipboard success/failure and timer/snapshot behavior are unchanged; cite existing tests only for their actual coverage.
- [ ] AC4: Existing solution build and all existing tests pass. No new dependencies, tests, UI harness or forbidden-file changes. Provide commands, exit codes and complete suite totals, including failures/skips and warnings.
- [ ] AC5: README.md, USER_GUIDE_RU.md and docs/wiki/pages/copy-name-scrollbar.md describe the actual fix, checks and waiver consistently. Delivery explicitly says: Фактическое устранение overlap без renderer остаётся NOT_YET_PROVEN.

## Validation Boundaries
- Manual interaction and automatic renderer-backed checks are waived. Do NOT require manual click/paste/hover/drag, screenshots, UI smoke, new harness or rendered geometry.
- Actual overlap removal without renderer remains NOT_YET_PROVEN. This uncertainty alone is not a merge blocker under Phil's explicit waiver.
- Waiver-scoped PASS proves delivery, existing build/tests and static layout/wiring/accessibility reasoning, NOT actual visual correctness or Windows runtime behavior.
- Any other required AC that is NO or NOT_YET_PROVEN forbids PASS. Waiver does not waive build, existing tests, static proof or scope.
- Use existing `dotnet build` and `dotnet test` from repository root with installed SDK. Record exact tested revision/digest for AXLE review binding.
- Put static evidence/coverage limits in the authorized wiki page and delivery report, not new out-of-scope evidence files.

## Efficiency and Stop Rules
- Do not repeat unchanged commands/tests/probes without a relevant code/state change.
- Permit at most one corrected retry after identifying a concrete cause; stop after the same boundary fails twice.
- Stop implementation after waiver-scoped AC1–AC5 pass. No adjacent improvements or source/dependency/packaging redesign.
- BLOCKED applies to missing identity, authority, SDK/mechanism or a required semantic decision; waived checks are not blockers.

## Evidence Contract
- Supply per-AC evidence, actual changed-file list and complete build/test results bound to source revision/digest.
- Review must cover AC, active/archived parity, semantics, security/privacy, downstream behavior and documentation parity in one pass.
- Preserve EVIDENCE / HYPOTHESIS / NOT_YET_PROVEN distinctions. Do not expose user timer data, clipboard contents, secrets or raw environment.

## Post-merge Packaging Boundary
- This is a separate Phil-authorized local operation after native merge/completion is confirmed; Senior does not package before review/merge.
- Lead delegates existing `packaging/build-windows-candidate.sh` on exact merged source to produce win-x64 self-contained Setup and ZIP privately for Phil.
- Use numeric patch version 2.6.1 and unique output label `2.6.1-copy-name-<short-merged-commit>`; same-version/different-payload 2.6.0 reuse is not an update.
- No source/version-file edits, new packaging code/dependencies, release/tag/upload or sending to Gala. Do not overwrite an existing output directory.
- Verify existing manifest/hash checks, capture SHA-256 and source provenance; return artifact paths and existing install/launch instructions.
- Windows/runtime and renderer behavior remain NOT_YET_PROVEN. Stop after the ready artifact is handed to Phil by path.

## Rollback Plan
- Revert only the new ticket's scoped changes through normal PR/VCS authority if needed. No reset/clean/stash or cancelled-WIP reuse.
- No user DB migration or clipboard implementation changes. Revert can restore the original overlap; do not claim it remains fixed.

## Deliverables
- Minimal XAML layout correction and three consistent documentation updates.
- Native reviewed PR with mandatory validation and explicit waiver; native merged commit after gates pass.
- After merge, private Setup/ZIP paths, hashes, launch/update instructions and known limitations for Phil.
