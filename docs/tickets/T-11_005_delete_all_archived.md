# HANDOFF TICKET: Delete all — очистить архив с подтверждением

## Meta
- Ticket ID: T-11_005_delete_all_archived
- Gitea Issue: T-11 / #11
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Ограниченное расширение существующего Delete flow и SQLite transaction, без новой схемы.
- Model Budget:
  - Estimated tokens: 22000
  - Reasoning effort: low
  - Rationale: Первый bounded проход; сохранить существующие safety guards, T-5 bulk patterns и xUnit patterns.
- Components: [DATA] [UI] [SECURITY]
- Priority: P1
- Risk level: High

## Goal
Пользователь удаляет все archived timer и их историю одним явно подтверждённым действием, не затрагивая неархивированные timer.

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-11_005_delete_all_archived`.
- Queue order is engine-decided, not board-decided: among Ready tickets the engine launches the first by numeric ticket id whose `Depends on` are all Done. This ticket declares no `Depends on`.
- This project must not reopen already-fixed decisions unless this ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends the canonical machine-written metadata.
Branch and source revision are not assigned in this draft. Use only identities returned by future promotion.

## Context
- Статус: DRAFT FOR PHIL REVIEW, не разрешение исполнения или promote.
- Фил запросил `Delete all` в Archived timers и отдельно подтвердил: «подтверждение добавить».
- T-5 (`Archive all`) уже Done и смёржен (PR #8): обратимый bulk archive существует. Этот тикет добавляет отдельную destructive bulk-приёмку и не смешивает её с архивацией.
- WHY: атомарное удаление всего архива требует собственного подтверждения, rollback и evidence, которые не доказываются Archive all tests.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `docs/TZ.md` §20 (Archive all и controls main view), §§38–40; `README.md`, `USER_GUIDE_RU.md` — подтверждение, Stopped-only individual Delete, transaction, History semantics.
- `src/GaleWorkTimer/Views/MainWindow.axaml`: archive Expander, Delete all placement, existing T-5 Archive all control.
- `src/GaleWorkTimer/Views/MainWindow.axaml.cs`: DeleteTimer dialog, Cancel focus, failure display.
- `src/GaleWorkTimer/Services/TimerService.cs`: ArchiveAllStopped, DeleteTimer, open-session check, dependent delete order.
- `src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs`: Execute, ArchiveAllStopped, LoadTimers and History filtering.
- `tests/GaleWorkTimer.Tests/TimerBulkArchiveTests.cs`, `TimerDeleteTests.cs`, `MainWindowViewModelTests.cs`.
- AXLE-owned native smoke fixture `/home/phil/axle-midi/scripts/galeworktimer-ui-smoke/` — read-only reference; do not edit it.

## Evidence State
### Static facts that may be trusted
- `EVIDENCE` (2026-11-16, T-5 merged head `987604bb2d46eb3ca416dc5c12a44513752214b4`, PR #8): `ArchiveAllStopped` существует в service/ViewModel/XAML и покрыт `TimerBulkArchiveTests`; Archived section содержит только per-timer Edit/Restore/Delete. Bulk Delete отсутствует.
- `EVIDENCE`: текущая реализация использует таблицы `timers`, `sessions`, `pauses`; иллюстративное имя `session_pauses` из `docs/TZ.md` §13 не переносить в код.
- `EVIDENCE`: worker environment задаёт `DOTNET_ROOT`, `PATH` и `AXLE_DOTNET_UI_SMOKE_PROJECT`; fixture строит реальный `MainWindow` на synthetic temp DB и доказывает native hit-test для Archive all/Restore, но README прямо исключает Delete all из его покрытия.
- `EVIDENCE`: existing individual Delete уже реализует dialog с Cancel focus, service open-session guard и dependent deletes.

### Expected but not proved
- `NOT_YET_PROVEN`: baseline build/tests и rendered Delete all dialog на назначенной AXLE revision.
- `NOT_YET_PROVEN`: доступность native display в worker environment для scoped renderer evidence; при недоступности это validation-infrastructure blocker, а не PASS.
- Remote/release state не доказан; проверять только назначенную AXLE revision.

## Spec Alignment
- Matches: `docs/TZ.md` §20 (Archive all, individual Delete), §39 (failure behaviour), §40 (необратимое удаление timer и history); README и USER_GUIDE_RU сохраняют различие Archive/Delete.
- Conflicts/unknowns: нет для семантики; §13 `session_pauses` — только иллюстративное имя.
- Assumptions which this ticket must resolve: candidate set = весь persisted архив на момент выполнения, не видимая проекция UI.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Services/TimerService.cs", "src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs", "src/GaleWorkTimer/Views/MainWindow.axaml", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "tests/GaleWorkTimer.Tests/TimerDeleteTests.cs", "tests/GaleWorkTimer.Tests/MainWindowViewModelTests.cs", "tests/GaleWorkTimer.Tests/TimerBulkDeleteTests.cs", "tests/GaleWorkTimer.Tests/TimerBulkDeleteSmokeTests.cs", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/timers-bulk-delete.md"]
- Forbidden Paths: ["SPEC.md", "docs/AXLE_PROJECT_KB.md", ".opencode/**", "src/GaleWorkTimer/Data/**", "worktrees/**"]
- Worker Forbidden Paths: ["docs/TZ.md", "docs/ssot/**"]
- Lead Documentation Paths: ["docs/TZ.md"]
- После отдельного разрешения исполнения: только scoped source/tests/docs, isolated test DB и normal AXLE PR delivery.
- Delivery evidence хранится в ignored `.axle-midi/delivery/<ticket_id>_delivery_pack.md`; tracked `docs/evidence/**` outputs не добавляются.
- Сейчас: только черновик; no promote, no Senior, no Git/workflow mutation.
- Запрещены работа с пользовательской БД, migration, backup/retention subsystem, release/deploy, конфигурация AXLE и любые правки AXLE-owned smoke fixture.

## Semantic Contract
### Field / State Inventory and Derivation
- Candidate set: все persisted `timers.is_archived = 1`, вычисленные service внутри одной immediate write transaction после подтверждения.
- Modal dialog блокирует команды главного окна. Подтверждение означает «весь архив на момент выполнения», без отдельного списка произвольных ID от UI.
- Перед удалением service проверяет отсутствие open sessions у ВСЕХ кандидатов. Наличие хотя бы одной означает reject всей операции.
- Выход success: удалены только candidate timers и связанные `sessions`/`pauses`; History больше не содержит их sessions.
- Неархивированные Stopped/Running/Paused и их записи не меняются. Чужие History duration snapshots не пересчитываются.
- Existing `Archive all` и individual `Delete` не меняют поведение.
- Выход cancel: нет service mutation, reload или изменения snapshot/selection.
- Пустой архив: кнопка видима, disabled. Прямой service-вызов при пустом архиве — no-op.
- Write failure: rollback всего набора. Post-commit reload failure — сохранено, но отображение не обновлено; не сообщать ложный rollback.

## Contract / Interfaces
- Кнопка `Delete all` находится внутри Archived timers, отдельно от глобальных Timers controls; она не смещается и не заменяет T-5 `Archive all`.
- Один dialog: `Delete all archived timers?` и предупреждение `This will permanently delete all archived timers and all of their history. This action cannot be undone.`
- Действия dialog: `Cancel` и destructive `Delete all`.
- Cancel получает focus, является default; Escape/закрытие dialog отменяет. Enter сразу после открытия НЕ подтверждает удаление.
- Только явное destructive действие запускает один bulk service-вызов. При write failure dialog остаётся открыт и показывает безопасную ошибку.
- При domain rejection, включая open session у archived кандидата, dialog также остаётся открыт с безопасной ошибкой. Данные не меняются.
- Индивидуальное Delete и его подтверждение сохраняются.

## Authorized Execution Mechanism
- Переиспользовать существующий native Avalonia ShowDialog и service/ViewModel error flow.
- Одна transaction для всего набора: validation → pauses → sessions → timers → commit.
- Не вызывать цикл отдельно commit-ящих DeleteTimer. Внутреннее переиспользование допустимо без вложенных транзакций и новой архитектуры.
- Native renderer evidence: запустить AXLE-owned fixture через configured `AXLE_DOTNET_UI_SMOKE_PROJECT` как регрессию Archive all/Restore; fixture не редактировать. Для Delete all dialog использовать scoped project test в `tests/GaleWorkTimer.Tests/TimerBulkDeleteSmokeTests.cs` или реальные desktop interactions.
- WHY: атомарность всего набора исключает частичное удаление архива; service guard защищает не только UI-вход.

## Forbidden Shortcuts
- Не удалять только видимые/выбранные строки вместо persisted archive set.
- Не вызывать индивидуальные DeleteTimer commits циклом.
- Не ослаблять individual Delete guard и не удалять open-session timer.
- Не считать test presence, XAML button presence или note за доказательство поведения.
- Не расширять scope до AXLE fixture, migration, retention или recovery subsystem.

## Acceptance Criteria
PASS разрешён только при доказательстве всех AC.
- [ ] AC1: При непустом архиве нажатие `Delete all` открывает ровно одно подтверждение с указанным предупреждением. До подтверждения записи не меняются.
- [ ] AC2: Cancel, Escape, закрытие dialog и Enter при начальном focus оставляют все данные и UI snapshots неизменными. Destructive действие не является default.
- [ ] AC3: Явное подтверждение удаляет все archived timer, включая zero-session и timer с несколькими sessions/pauses, без orphan rows; результат сохраняется после reopen БД.
- [ ] AC4: Fixture одновременно содержит неархивированные Stopped, Running, Paused и archived timer. Неархивированные записи, состояния, история и timestamps неизменны; нет auto Stop/Archive. Existing `Archive all` по-прежнему архивирует только eligible Stopped.
- [ ] AC5: Пустой архив безопасен. Вне UI service отклоняет весь набор, если у archived кандидата есть open session; guards individual Delete не ослаблены.
- [ ] AC6: Injected failure после dependent deletes и после обработки не первого timer восстанавливает ВСЕ timers/sessions/pauses. UI не скрывает записи и не заявляет успех; безопасная ошибка видна в dialog.
- [ ] AC7: После commit ArchivedTimers и уже загруженная History больше не показывают удалённые данные. Остальные History snapshots сохраняются; selection/result очищаются только по текущему refresh contract. Post-commit reload failure не называется failed deletion.
- [ ] AC8: Scoped docs описывают irreversible history removal, confirmation, cancel и отличие от Archive all; валидация не касается пользовательской БД.
- [ ] AC9: AXLE-owned native smoke fixture проходит на final revision как регрессия Archive all/Restore; fixture не изменён и не заявлен как доказательство Delete all.

## Required Semantic Proof
- Mixed archived/nonarchived fixture; сравнить все persisted поля и dependent rows до/после и после reopen.
- Negative fixture с искусственно созданной archived/open-session комбинацией только в isolated DB; весь набор сохраняется.
- Rollback после удаления dependents/промежуточного кандидата, не только ошибка до первой записи.
- Native renderer evidence для dialog: open, Cancel, Escape, Close, Enter при начальном focus, confirm, injected error; один только ViewModel test не доказывает focus/keyboard semantics.
- AXLE fixture run подтверждает отсутствие регрессии Archive all/Restore; он не доказывает Delete all.
- Anti-collapse: «удалить архив» не равно «удалить все Stopped»; nonarchived Stopped обязан остаться.

## Validation Boundaries
- Affected xUnit tests, `dotnet build`, один финальный полный `dotnet test` на final revision и final changed bytes.
- Native renderer: configured AXLE fixture плюс scoped `TimerBulkDeleteSmokeTests` или реальный desktop smoke; указывать exact platform/display. Windows behavior не утверждать без Windows evidence.
- Delivery evidence: ignored `.axle-midi/delivery/<ticket_id>_delivery_pack.md`; validation runs сообщают observed identity по текущему harness contract; не добавлять tracked evidence outputs после validation.
- Прямой SQL допустим только как тестовая failure injection; production data mutation запрещена.
- Lead синхронизирует `docs/TZ.md` §§20/40 отдельным approved `lead_documentation` шагом, привязанным к reviewed PR head; затем выполняется свежий review. Senior обновляет только scoped wiki/guide, не Lead-owned SSOT.
- Release/Windows installer/доставка пользователю не входят в этот outcome.

## Efficiency and Stop Rules
- Не повторять неизменённую команду/тест; одна corrected retry после конкретной причины, затем stop на втором отказе той же границы.
- При неизвестных authority/identity/mechanism или конфликте семантики остановиться, не расширять scope.
- Любой обязательный NO/NOT_YET_PROVEN запрещает PASS. После PASS не начинать release.
- Отсутствие display/renderer — BLOCKED с точной причиной, не замена UI AC на static XAML check.

## Security Notes
- Пользовательские имена не подставлять в SQL; сохранять parameterization и service-side guards.
- Не логировать реальные timer names, DB paths/contents, stack traces или secrets в evidence/UI.
- Native fixture использует только synthetic temp DB; пользовательские данные не читаются.
- Новые dependencies, network и telemetry не требуются и не разрешены.

## Evidence Contract
- Delivery pack: final revision, exact changed files, per-AC test/run results и redacted native interaction evidence.
- Отдельно указать commit/rollback outcome и post-commit read outcome; не подменять один другим.
- Validation runs: observed result identity по текущему harness contract; не встраивать aggregate digest в tracked changed file.
- Не включать реальные БД или личные данные. Непроверенное помечать NOT_YET_PROVEN.

## No-Architecture-Drift Rule
- Senior не перепроектирует schema, retention, recovery или общий delete flow; только scoped bulk Delete all по этому контракту.

## Rollback Plan
- Откат кода через reviewed revert, schema v2 совместима.
- Revert НЕ восстанавливает явно удалённые данные. Не обещать Undo и не добавлять скрытый recycle bin.
- Все destructive validation выполняются только на disposable synthetic DB.

## Deliverables
- Минимальный bulk delete flow, service guards/atomicity, focused tests, scoped docs и per-AC delivery evidence.
