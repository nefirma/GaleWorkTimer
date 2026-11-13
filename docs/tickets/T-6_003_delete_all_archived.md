# HANDOFF TICKET: Delete all — очистить архив с подтверждением

## Meta
- Ticket ID: T-6_003_delete_all_archived
- Gitea Issue: T-6 / #6
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Ограниченное расширение существующего Delete flow и SQLite transaction, без новой схемы.
- Model Budget:
  - Estimated tokens: 22000
  - Reasoning effort: low
  - Rationale: Первый bounded проход; сохранить существующие safety guards и xUnit patterns.
- Components: [DATA] [UI] [SECURITY]
- Priority: P1
- Risk level: High

## Goal
Пользователь удаляет все archived timer и их историю одним явно подтверждённым действием, не затрагивая неархивированные timer.

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-6_003_delete_all_archived`.
- Queue order and execution belong to AXLE; this draft does not register or launch a ticket.
- This project must not reopen already-fixed decisions unless this ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends the canonical machine-written metadata.
Branch and source revision are not assigned in this draft. Use only identities returned by future promotion.

## Context
- Статус: DRAFT FOR PHIL REVIEW, не разрешение исполнения или promote.
- Фил запросил `Delete all` в Archived timers и отдельно подтвердил: «подтверждение добавить».
- WHY: отдельный тикет сохраняет независимую destructive-action приёмку, не смешивая удаление с обратимой архивацией.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `SPEC.md` §§20, 38–40; `README.md` — подтверждение, Stopped-only, transaction и History semantics.
- `src/GaleWorkTimer/Views/MainWindow.axaml.cs`: DeleteTimer dialog, Cancel focus, failure display.
- `src/GaleWorkTimer/Services/TimerService.cs`: DeleteTimer, проверка open session, порядок dependent deletes.
- `src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs`: Execute, LoadTimers и History filtering.
- `tests/GaleWorkTimer.Tests/TimerDeleteTests.cs`, `MainWindowViewModelTests.cs`.

## Evidence State
- EVIDENCE, локальное чтение 2026-11-13: существует только индивидуальное Delete; bulk UI/service отсутствует.
- EVIDENCE: текущая реализация использует таблицы `timers`, `sessions`, `pauses`; не переносить в код иллюстративное имя `session_pauses` из SPEC §13.
- EVIDENCE: SDK 10.0.401 доступен; текущий dialog использует Cancel как IsDefault/IsCancel и focus target.
- Источники: Фил задаёт новый bulk flow; SPEC §40 задаёт safety semantics; код задаёт текущую schema/API. Remote/release state не доказан.
- RCA отсутствия bulk-действия подтверждён UI/service; никакая текущая runtime DB не проверялась.
- Перед исполнением проверить назначенную AXLE revision. Baseline тесты и GUI этого изменения ещё NOT_YET_PROVEN.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Services/TimerService.cs", "src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs", "src/GaleWorkTimer/Views/MainWindow.axaml", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "tests/GaleWorkTimer.Tests/TimerDeleteTests.cs", "tests/GaleWorkTimer.Tests/MainWindowViewModelTests.cs", "tests/GaleWorkTimer.Tests/TimerBulkDeleteTests.cs", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/timers-bulk-delete.md", "docs/evidence/timer-bulk-delete/**"]
- Forbidden Paths: ["SPEC.md", "docs/TZ.md", "docs/AXLE_PROJECT_KB.md", ".opencode/**", "src/GaleWorkTimer/Data/**", "worktrees/**"]
- После отдельного разрешения исполнения: только scoped source/tests/docs, isolated test DB и нормальная AXLE PR delivery.
- Сейчас: только черновик; no promote, no Senior, no Git/workflow mutation.
- Запрещены работа с пользовательской БД, migration, backup/retention subsystem, release/deploy и конфигурация AXLE.

## Semantic Contract
### Field / State Inventory and Derivation
- Candidate set: все persisted `timers.is_archived = 1`, вычисленные service внутри одной immediate write transaction после подтверждения.
- Modal dialog блокирует команды главного окна. Подтверждение означает «весь архив на момент выполнения», без отдельного списка произвольных ID от UI.
- Перед удалением service проверяет отсутствие open sessions у ВСЕХ кандидатов. Наличие хотя бы одной означает reject всей операции.
- Выход success: удалены только candidate timers и связанные `sessions`/`pauses`; History больше не содержит их sessions.
- Неархивированные Stopped/Running/Paused и их записи не меняются. Чужие History duration snapshots не пересчитываются.
- Выход cancel: нет service mutation, reload или изменения snapshot/selection.
- Пустой архив: кнопка видима, disabled. Прямой service-вызов при пустом архиве — no-op.
- Write failure: rollback всего набора. Post-commit reload failure — сохранено, но отображение не обновлено; не сообщать ложный rollback.

## Contract / Interfaces
- Кнопка `Delete all` находится внутри Archived timers, отдельно от глобальных Timers controls.
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
- WHY: атомарность всего набора исключает частичное удаление архива; service guard защищает не только UI-вход.

## Acceptance Criteria
PASS разрешён только при доказательстве всех AC.
- [ ] AC1: При непустом архиве нажатие `Delete all` открывает ровно одно подтверждение с указанным предупреждением. До подтверждения записи не меняются.
- [ ] AC2: Cancel, Escape, закрытие dialog и Enter при начальном focus оставляют все данные и UI snapshots неизменными. Destructive действие не является default.
- [ ] AC3: Явное подтверждение удаляет все archived timer, включая zero-session и timer с несколькими sessions/pauses, без orphan rows; результат сохраняется после reopen БД.
- [ ] AC4: Fixture одновременно содержит неархивированные Stopped, Running, Paused и archived timer. Неархивированные записи, состояния, история и timestamps неизменны; нет auto Stop/Archive.
- [ ] AC5: Пустой архив безопасен. Вне UI service отклоняет весь набор, если у archived кандидата есть open session; guards individual Delete не ослаблены.
- [ ] AC6: Injected failure после dependent deletes и после обработки не первого timer восстанавливает ВСЕ timers/sessions/pauses. UI не скрывает записи и не заявляет успех; безопасная ошибка видна в dialog.
- [ ] AC7: После commit ArchivedTimers и уже загруженная History больше не показывают удалённые данные. Остальные History snapshots сохраняются; selection/result очищаются только по текущему refresh contract. Post-commit reload failure не называется failed deletion.
- [ ] AC8: Scoped docs описывают irreversible history removal, confirmation, cancel и отличие от Archive all; валидация не касается пользовательской БД.

## Required Semantic Proof
- Mixed archived/nonarchived fixture; сравнить все persisted поля и dependent rows до/после и после reopen.
- Negative fixture с искусственно созданной archived/open-session комбинацией только в isolated DB; весь набор сохраняется.
- Rollback после удаления dependents/промежуточного кандидата, не только ошибка до первой записи.
- UI evidence для каждого пути отмены и подтверждения; один только ViewModel test не доказывает focus/keyboard semantics.
- Anti-collapse: «удалить архив» не равно «удалить все Stopped»; nonarchived Stopped обязан остаться.

## Validation Boundaries
- Affected xUnit tests, `dotnet build`, один финальный полный `dotnet test` на final revision.
- Desktop smoke с synthetic archive: focus, Enter, Escape, Close, Cancel, confirm, error. Только isolated profile/DB.
- Прямой SQL допустим только как тестовая failure injection; production data mutation запрещена.
- Lead синхронизирует `SPEC.md` §§20/40 перед финальным одобрением. Senior обновляет только scoped wiki/guide, не Lead-owned SSOT.
- Release/Windows installer/доставка пользователю не входят в этот outcome.

## Efficiency and Stop Rules
- Не повторять неизменённую команду/тест; одна corrected retry после конкретной причины, затем stop на втором отказе той же границы.
- При неизвестных authority/identity/mechanism или конфликте семантики остановиться, не расширять scope.
- Любой обязательный NO/NOT_YET_PROVEN запрещает PASS. После PASS не начинать release.

## Security Notes
- Пользовательские имена не подставлять в SQL; сохранять parameterization и service-side guards.
- Не логировать реальные timer names, DB paths/contents, stack traces или secrets в evidence/UI.
- Новые dependencies, network и telemetry не требуются и не разрешены.

## Evidence Contract
- Final revision, exact changed files, per-AC test/run results и redacted desktop interaction evidence.
- Отдельно указать commit/rollback outcome и post-commit read outcome; не подменять один другим.
- Не включать реальные БД или личные данные. Непроверенное помечать NOT_YET_PROVEN.

## Rollback Plan
- Откат кода через reviewed revert, schema v2 совместима.
- Revert НЕ восстанавливает явно удалённые данные. Не обещать Undo и не добавлять скрытый recycle bin.
- Все destructive validation выполняются только на disposable synthetic DB.

## Deliverables
- Минимальный bulk delete flow, service guards/atomicity, focused tests, scoped docs и per-AC evidence.
