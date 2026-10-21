# T-2 stable Windows install/update candidate

## Candidate boundary

**EVIDENCE:** The local candidate is `GaleWorkTimer.Setup.exe` plus `GaleWorkTimer-win-x64.zip`, built for `win-x64` and self-contained.

**EVIDENCE:** Candidate version is `2.6.0`. The ZIP contains 228 publish files and a `manifest.json` with each path and SHA-256.

**EVIDENCE:** The local build output is ignored under `artifacts/`. The candidate was not uploaded, published, or installed on a Windows machine.

| Artifact | SHA-256 | Local path |
| --- | --- | --- |
| `GaleWorkTimer.Setup.exe` | `58d205d9139e6456b9d9ba2745989bfd28d61cc8042538395579735f9210bf5e` | `artifacts/windows-candidate/2.6.0-rework/GaleWorkTimer.Setup.exe` |
| `GaleWorkTimer-win-x64.zip` | `fdcf53af3c0e008ef586b1fa7d63e6215d90bbb2abae065c412e775985af525f` | `artifacts/windows-candidate/2.6.0-rework/GaleWorkTimer-win-x64.zip` |

The hashes identify these local files. They do not prove publisher identity.

## Build and verification

**EVIDENCE:** Current rework build command:

```bash
rtk test env DOTNET=/home/phil/.dotnet/dotnet bash packaging/build-windows-candidate.sh 2.6.0 2.6.0-rework
```

**EVIDENCE:** Exit code `0`. The script published the app and setup for `win-x64`, created the 228-file ZIP, printed both SHA-256 values, and verified the ZIP manifest and contents.

**EVIDENCE:** Reproducibility rebuild command:

```bash
rtk test env DOTNET=/home/phil/.dotnet/dotnet bash packaging/build-windows-candidate.sh 2.6.0 2.6.0-rework-verify
```

**EVIDENCE:** The second build exited `0`. Both artifact SHA-256 values matched the rework build.

**EVIDENCE:** Solution build command:

```bash
rtk test -v /home/phil/.dotnet/dotnet build GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `1` warning and `0` errors in compact output. Warning detail is `NOT_YET_PROVEN` because the compact result did not include its message.

**EVIDENCE:** Solution test command:

```bash
rtk test -v /home/phil/.dotnet/dotnet test GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `44` packaging tests and `66` application tests passed. No tests failed or were skipped.

## Output source and derivation

| Output | Class | Source or exact derivation | Similar output relationship |
| --- | --- | --- | --- |
| `install_root` | `computed` | `Environment.SpecialFolder.LocalApplicationData` + `Programs/GaleWorkTimer` | Fixed across candidate updates; never read from the archive. |
| `data_path` | `raw_source` | Existing app contract: `%LOCALAPPDATA%/NeFirma/GaleWorkTimer/data.db` | Outside `install_root`; installer does not open, copy, replace, or delete it. |
| `stable_entry` | `computed` | `install_root/GaleWorkTimer.exe`; permanent copy of the self-contained setup/launcher binary | Intentionally distinct from `payload_executable`, which changes between `payload-0` and `payload-1`. |
| `installed_version` | `computed` | Version in the verified package manifest, persisted only after the complete target slot passes file-hash verification and state commit | Intentionally distinct from a downloaded package version until that payload is selected. |
| `update_result` | `branch_state` | `installed` if no valid prior state existed; `updated` if a prior state existed and the new or already-selected verified payload is active; `recovered_previous` after startup recovery; `rolled_back` after a caught update failure restores the prior payload; otherwise `rejected` | `recovered_previous` means startup recovered a pending transaction; `rolled_back` means the current installer handled a failure. Reapplying the same manifest hash is idempotent and returns success without rewriting either slot. |
| `failure_reason` | `computed` | Safe stage code plus fixed user-facing reason selected from package validation, lock/access/I/O failure, startup recovery, or payload verification | No stack trace, profile name, full private path, database content, or credentials are included. `Ctrl+C` in the Windows message box copies the message. |

## Acceptance evidence

| AC | Evidence | Semantic trap / negative fixture | Result |
| --- | --- | --- | --- |
| C1 | `packaging/build-windows-candidate.sh`; win-x64 self-contained publish; 228-file verified ZIP; stable setup entry and install instructions in `USER_GUIDE_RU.md` | No application source change; no administrator or separately installed runtime requirement is added | `PASS` on CachyOS static build only |
| C2 | `Install_and_update_select_verified_payload_at_one_stable_entry_without_touching_data` asserts the stable entry path, A and B executable bytes, two bounded slots, idempotent repeat, and unchanged database/WAL/SHM sentinels | Same package repeat does not create another payload directory or change selected bytes | `PASS` in packaging tests |
| C3 | `Interrupted_update_recovers_previous_verified_payload_and_reports_a_safe_reason` and `Occupied_inactive_slot_rolls_back_without_touching_active_payload_or_data` | Interrupted B is not reported as updated; A remains selected and the reason contains no private test path | `PASS` in packaging tests |
| C4 | `Abrupt_update_interruption_at_each_persistent_boundary_recovers_through_stable_entry` runs a child process for every update checkpoint: journal temp/rename, all four staged-file writes, manifest write, before/after inactive-slot deletion, candidate rename, state temp/rename, and transaction cleanup. `Abrupt_initial_install_interruption_recovers_through_stable_entry_and_allows_retry` covers journal temp/rename, stable-entry temp/rename, all four staged-file writes, manifest write, candidate rename, state temp/rename, and transaction cleanup. | `Environment.FailFast` bypasses the interrupted process `finally`. Every case calls `LaunchStableEntry`. Update pre-commit cases select B and report `UPDATE_INTERRUPTED`; post-commit cases select C. Initial pre-commit cases reject without launching or inventing a prior version; cases before stable-entry rename assert the entry is absent. Retry installs A; post-commit startup selects A. All cases assert selected bytes and stable-entry preservation where it already exists. | `PASS` in the latest packaging suite |
| C5 | `AssertDataSentinels` compares exact names and bytes (`data.db`, `data.db-wal`, `data.db-shm`) in success/repeat, pre-transaction failures, uncommitted journal failure, rollback, every process-crash recovery boundary, initial-install failure and retry, traversal/link/manifest rejection, running-app rejection, and concurrent updater. | Includes same-version/different-payload rejection. Traversal, absolute/drive paths, reserved names, invalid characters, link entries, missing files, wrong SHA-256, occupied slot, concurrent updater, and every crash/recovery case leave all sentinel files unchanged. | `PASS` in the latest packaging suite; Windows locking and process detection remain unproven |
| C6 | `README.md`, `USER_GUIDE_RU.md`, and this page describe candidate commands, first portable-to-stable transition, normal update, close-before-update, stable path, rollback/recovery, and copying the safe reason | Current public T2.5 portable release is distinguished from the unpublished candidate | `PASS` by direct document inspection |
| C7 | Release solution build (exit `0`, `1` warning, `0` errors); latest Release solution suite (`57` packaging + `66` application tests passed); current rework win-x64 app/setup publish; repeat build produced matching SHA-256 values; ZIP verification | Candidate build and Linux file simulation are not evidence of Windows runtime behavior | `PASS` for local candidate checks; W1–W4 remain `NOT_YET_PROVEN` |

The A/B byte assertions and state-commit boundary fixtures are the anti-collapse proof: selecting A before commit and B after commit produce different observed executable bytes. Tests also include successful, degraded initial-install, and negative package fixtures.

## Rework execution evidence

**EVIDENCE:** Rework used branch `ticket/T-2-001_stable_windows_install_update`, starting from reviewed head `b865786ff048370a73a5a2fa2c652fb1f4bcd947` and authority base `2854ea15a29ea5cbb1e2206b7e2a01a7ef784779`. The source rework is limited to `InstallManager.cs` and `InstallManagerTests.cs`; this page records the updated validation evidence.

**EVIDENCE:** Release solution build command:

```bash
rtk test -v /home/phil/.dotnet/dotnet build GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `0` errors and `1` warning in compact output. Warning detail is `NOT_YET_PROVEN` because the wrapper output did not include its message.

**EVIDENCE:** Release solution test command:

```bash
rtk test -v /home/phil/.dotnet/dotnet test GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `44` packaging tests and `66` application tests passed; `0` failed and `0` skipped. The abrupt-termination tests launch a separate filtered test process and verify its forced termination marker before invoking the shared startup method.

**EVIDENCE:** Rework candidate build and repeat build both exited `0`. Setup SHA-256 was `58d205d9139e6456b9d9ba2745989bfd28d61cc8042538395579735f9210bf5e`; ZIP SHA-256 was `fdcf53af3c0e008ef586b1fa7d63e6215d90bbb2abae065c412e775985af525f`. The repeat build produced both same hashes and verified the 228-file manifest.

**NOT_YET_PROVEN:** This Linux evidence does not establish Windows process detection, file locking, shortcut behavior, Windows paths, physical power-loss durability, or human acceptance. W1–W4 remain `NOT_YET_PROVEN`.

**EVIDENCE:** Rework continuation expanded abrupt process termination to every applicable update and initial-install persistent boundary. Release packaging tests passed `57/57`; application tests passed `66/66`; the complete Release solution suite exited `0`. The packaging matrix has 13 update cases and 13 initial-install cases. Each child process confirms its exact checkpoint marker and exits through `Environment.FailFast`; each parent then uses `LaunchStableEntry` to inspect the selected payload and outcome. Initial pre-commit cases also prove no process start, no invented previous install, and successful retry. Each case compares the full `data.db`, WAL, and SHM sentinel set and bytes.

**EVIDENCE:** Latest Release solution test command:

```bash
rtk test /home/phil/.dotnet/dotnet test GaleWorkTimer.sln --no-restore --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `57` packaging tests and `66` application tests passed; no failures or skips.

## Human Windows validation

| Item | State | Evidence required |
| --- | --- | --- |
| W1: install without admin/.NET and preserve Timer/Total/History | `NOT_YET_PROVEN` | Galia runs the candidate on Windows and confirms existing app data. |
| W2: stable path, existing shortcut, and new version | `NOT_YET_PROVEN` | Galia updates on Windows and confirms path and launch. |
| W3: controlled failure and recovery | `NOT_YET_PROVEN` | Galia uses a disposable Windows user profile/install-root and confirms the previous payload and copied reason. |
| W4: Windows locking, Cyrillic/spaced paths, and data preservation | `NOT_YET_PROVEN` | Galia confirms these cases on Windows. |

No release, publication, or Windows acceptance is claimed. The validated worktree snapshot is bound by the final worker-result `source_revision` and complete `changed_files` manifest; the AXLE authority base is `2854ea15a29ea5cbb1e2206b7e2a01a7ef784779`.

**NOT_YET_PROVEN:** Sudden power-loss recovery and parent-directory durability after file/directory renames. Tests simulate process interruption; they do not prove filesystem behavior after power loss.
