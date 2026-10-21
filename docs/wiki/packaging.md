# T-2 stable Windows install/update candidate

## Candidate boundary

**EVIDENCE:** The local candidate is `GaleWorkTimer.Setup.exe` plus `GaleWorkTimer-win-x64.zip`, built for `win-x64` and self-contained.

**EVIDENCE:** Candidate version is `2.6.0`. The ZIP contains 228 publish files and a `manifest.json` with each path and SHA-256.

**EVIDENCE:** The local build output is ignored under `artifacts/`. The candidate was not uploaded, published, or installed on a Windows machine.

| Artifact | SHA-256 | Local path |
| --- | --- | --- |
| `GaleWorkTimer.Setup.exe` | `8ac25d31f700d7e5acf454a0298792c816004ccf582e722ab3565cb750045978` | `artifacts/windows-candidate/2.6.0/GaleWorkTimer.Setup.exe` |
| `GaleWorkTimer-win-x64.zip` | `492e68cb7ce57a946979b18e2d85784e14dfb46ecc9df6bcc6e4c37a3f196b7b` | `artifacts/windows-candidate/2.6.0/GaleWorkTimer-win-x64.zip` |

The hashes identify these local files. They do not prove publisher identity.

## Build and verification

**EVIDENCE:** Build command:

```bash
rtk test env DOTNET=/home/phil/.dotnet/dotnet bash packaging/build-windows-candidate.sh 2.6.0
```

**EVIDENCE:** Exit code `0`. The script published the app and setup for `win-x64`, created the ZIP, printed both SHA-256 values, and verified the ZIP manifest and contents.

**EVIDENCE:** Rebuild command:

```bash
rtk test env DOTNET=/home/phil/.dotnet/dotnet bash packaging/build-windows-candidate.sh 2.6.0 2.6.0-verify
```

**EVIDENCE:** The second build exited `0`. Both artifact SHA-256 values matched the first build.

**EVIDENCE:** Solution build command:

```bash
rtk test -v /home/phil/.dotnet/dotnet build GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `0` warnings and `0` errors.

**EVIDENCE:** Solution test command:

```bash
rtk test -v /home/phil/.dotnet/dotnet test GaleWorkTimer.sln --configuration Release --verbosity minimal
```

**EVIDENCE:** Exit code `0`; `41` packaging tests and `66` existing application tests passed. No tests failed or were skipped.

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
| C4 | `Recovery_selects_last_committed_payload_at_each_persistent_boundary`, `Interruption_after_each_staged_payload_file_write_recovers_previous_payload`, `Interrupted_initial_stable_entry_write_leaves_no_launchable_partial_payload`, and `Initial_install_recovery_does_not_invent_a_previous_version` | Before state commit recovery selects the prior complete payload; after the atomic state commit recovery selects the new complete payload; initial failure never reports rollback or launches partial payload | `PASS` in packaging tests |
| C5 | `Unsafe_archive_paths_are_rejected_without_changing_installed_payload`, `Symlink_archive_entry_is_rejected_before_switching`, `Invalid_manifest_file_list_and_integrity_are_rejected`, `Concurrent_updater_is_rejected_while_first_transaction_holds_the_install_lock`, `Occupied_inactive_slot_rolls_back_without_touching_active_payload_or_data`, and `Running_application_rejects_update_before_any_payload_or_data_mutation` | Traversal, absolute/drive paths, Windows reserved names, invalid characters, link entries, missing files, wrong SHA-256, simulated occupied slot, concurrent updater, injected running-process check | `PASS` in filesystem tests; Windows locking and process detection remain unproven |
| C6 | `README.md`, `USER_GUIDE_RU.md`, and this page describe candidate commands, first portable-to-stable transition, normal update, close-before-update, stable path, rollback/recovery, and copying the safe reason | Current public T2.5 portable release is distinguished from the unpublished candidate | `PASS` by direct document inspection |
| C7 | Release solution build (`0` warnings, `0` errors); solution tests (`41` packaging + `66` app, exit `0`); win-x64 app/setup publish; package verification and candidate SHA-256 | Candidate build and Linux file simulation are not evidence of Windows runtime behavior | `PASS` for local candidate checks; W1–W4 remain `NOT_YET_PROVEN` |

The A/B byte assertions and state-commit boundary fixtures are the anti-collapse proof: selecting A before commit and B after commit produce different observed executable bytes. Tests also include successful, degraded initial-install, and negative package fixtures.

## Human Windows validation

| Item | State | Evidence required |
| --- | --- | --- |
| W1: install without admin/.NET and preserve Timer/Total/History | `NOT_YET_PROVEN` | Galia runs the candidate on Windows and confirms existing app data. |
| W2: stable path, existing shortcut, and new version | `NOT_YET_PROVEN` | Galia updates on Windows and confirms path and launch. |
| W3: controlled failure and recovery | `NOT_YET_PROVEN` | Galia uses a disposable Windows user profile/install-root and confirms the previous payload and copied reason. |
| W4: Windows locking, Cyrillic/spaced paths, and data preservation | `NOT_YET_PROVEN` | Galia confirms these cases on Windows. |

No release, publication, or Windows acceptance is claimed. The source snapshot is bound by the final worker-result `source_digest`; the AXLE authority base is `2854ea15a29ea5cbb1e2206b7e2a01a7ef784779`.

**NOT_YET_PROVEN:** Sudden power-loss recovery and parent-directory durability after file/directory renames. Tests simulate process interruption; they do not prove filesystem behavior after power loss.
