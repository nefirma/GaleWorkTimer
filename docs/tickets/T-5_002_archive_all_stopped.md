# HANDOFF TICKET: Archive all — архивировать все остановленные таймеры

## Meta
- Ticket ID: T-5_002_archive_all_stopped
- Gitea Issue: T-5 / #5
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Локальное расширение существующих service → ViewModel → Avalonia UI, без смены схемы или платформы.
- Model Budget:
  - Estimated tokens: 18000
  - Reasoning effort: low
  - Rationale: Первый ограниченный проход; переиспользовать текущие транзакции и xUnit.
- Components: [DATA] [UI]
- Priority: P1
- Risk level: Medium

## Goal
Пользователь архивирует все неархивированные Stopped таймеры одной кнопкой, сохраняя историю и незавершённые таймеры.

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-5_002_archive_all_stopped`.
- Queue order and execution belong to AXLE; this draft does not register or launch a ticket.
- This project must not reopen already-fixed decisions unless this ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends the canonical machine-written metadata.
Branch and source revision are not assigned in this draft. Use only identities returned by future promotion.

## Context
- Статус: DRAFT FOR PHIL REVIEW, не разрешение исполнения или promote.
- Основание: заявка Фила — добавить Remove all либо Archive all для всех остановленных таймеров.
- Выбрано имя `Archive all`: оно не смешивает обратимую архивацию с permanent Delete.
- Отдельный тикет нужен потому, что сохранение истории имеет независимую приёмку от её удаления.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `SPEC.md` §§6, 7, 14, 19, 20, 38–40; `README.md` — текущие archive/history/snapshot contracts.
- `src/GaleWorkTimer/Services/TimerService.cs`: SetArchived, GetTimerState, immediate transactions.
- `src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs`: Execute, LoadTimers, ClearSelectedTotal.
- `src/GaleWorkTimer/Views/MainWindow.axaml` и `.axaml.cs`: текущие индивидуальные действия.

## Evidence State
- EVIDENCE, локальное чтение 2026-11-13: массовой кнопки и массового service-действия нет.
- EVIDENCE: Stopped соответствует Idle — отсутствует session с `stopped_at_ms IS NULL`.
- EVIDENCE: SDK 10.0.401 доступен на development host; baseline build/tests в этой сессии не запускались.
- Источники конкурирующих утверждений: запрос Фила задаёт новое поведение; SPEC задаёт существующие инварианты; текущий код доказывает только локальную реализацию. Remote/release state не проверен.
- RCA: отсутствие bulk UI/service подтверждено; причина проблемы скроллбара не относится к этому тикету.
- Перед исполнением динамически проверить назначенную AXLE revision и отсутствие уже реализованного эквивалента. Не угадывать branch/SHA.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Services/TimerService.cs", "src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs", "src/GaleWorkTimer/Views/MainWindow.axaml", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "tests/GaleWorkTimer.Tests/MainWindowViewModelTests.cs", "tests/GaleWorkTimer.Tests/TimerBulkArchiveTests.cs", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/timers-bulk-archive.md", "docs/evidence/timer-bulk-archive/**"]
- Forbidden Paths: ["SPEC.md", "docs/TZ.md", "docs/AXLE_PROJECT_KB.md", ".opencode/**", "src/GaleWorkTimer/Data/**", "worktrees/**"]
- После отдельного разрешения исполнения: локальные изменения только в Scope Paths, isolated test DB, обычная AXLE PR delivery.
- Сейчас разрешено только создание этого черновика. Не выполнять promote, Git mutation, запуск Senior или изменение пользовательской БД.
- Исполнение не разрешает release, installer, deploy, migration, AXLE service/config/workflow changes или расширение scope.

## Semantic Contract
### Field / State Inventory and Derivation
- Eligible set: persisted `is_archived = 0` AND отсутствие open session в одной write transaction.
- `Stopped`: Idle, включая таймер без единой session; Paused не является Stopped.
- После commit у eligible set `is_archived = 1`, `updated_at_ms` равен одному timestamp команды.
- Остальные поля timer и все sessions/pauses сохраняются. История и Total продолжают выводиться по существующим правилам.
- Непустая выборка не зависит от checkbox selection, видимости строк или прокрутки.
- Пустой eligible set: безопасный no-op. Кнопка видима; disabled, если текущая UI-проекция не содержит eligible timer.
- Command timestamp должен быть неотрицательным. Внутри write transaction проверить: он не меньше `updated_at_ms` каждого кандидата. Один command timestamp применяется ко всему набору.
- Ошибка записи или нарушение этой проверки хотя бы для одного кандидата: отмена всей операции, понятная ошибка; не пропускать ошибочный timer молча.
- Состав eligible set вычисляет service из БД внутри immediate transaction, не UI из устаревших строк.

## Authorized Execution Mechanism
- Существующая C# сборка, Microsoft.Data.Sqlite immediate transaction и Avalonia обработчик кнопки.
- Один bulk service-вызов, не цикл отдельных commit через ArchiveTimer.
- Не вводить новый слой, framework, dependency, schema migration или polling.
- WHY: одна транзакция исключает частичную архивацию; проверка persisted sessions сохраняет защиту при устаревшем UI.

## Acceptance Criteria
PASS разрешён только при доказательстве всех AC.
- [ ] AC1: `Archive all` на Timers архивирует все eligible timer одним нажатием без дополнительного диалога; индивидуальное Remove from list работает как прежде.
- [ ] AC2: Fixture содержит zero-session Stopped, Stopped с несколькими sessions/pauses, Running, Paused и уже archived timer. Только первые два меняют archive flag и timestamp; Running/Paused не останавливаются.
- [ ] AC3: Все существующие sessions/pauses, имена, цвета, created timestamps и порядок newest-first сохраняются. Archived timers доступны для Restore, их History и Total сохранены после reopen БД.
- [ ] AC4: Пустой eligible set, повторный прямой bulk-вызов после успешной архивации и устаревший UI не меняют неподходящие timer. Кнопка disabled только при отсутствии eligible timer в текущей проекции; пустой checkbox selection не влияет на доступность и состав действия.
- [ ] AC5: Injected write failure после изменения хотя бы одного кандидата откатывает все записи. Отрицательный command timestamp либо timestamp меньше `updated_at_ms` любого кандидата отменяет всю операцию без изменений. UI не выдаёт ошибку записи за успех.
- [ ] AC6: После commit списки обновляются одним общим snapshot; результат «Общее время» сбрасывается по текущему контракту. History duration snapshots не пересчитываются. При post-commit reload failure UI сообщает «сохранено, но не обновлено», не заявляя rollback.
- [ ] AC7: Документация описывает различие Archive all/Delete и исключение Running/Paused. Нет пользовательской DB mutation при валидации.

## Required Semantic Proof
- Mixed-state fixture AC2 с точным сравнением persisted IDs/полей до/после и после reopen.
- Rollback fixture с ошибкой не на первом кандидате; пустая выборка и invalid timestamp.
- Anti-collapse: Paused имеет нулевой текущий прирост времени, но НЕ архивируется как Stopped.
- ViewModel fixture подтверждает списки, selected-total invalidation и сохранение History snapshot.

## Validation Boundaries
- Запустить affected xUnit tests и `dotnet build`; в конце один полный `dotnet test` для той же final revision.
- UI smoke на isolated test profile: кнопка, mixed states, архив и Restore. Без личных данных.
- Windows packaging/release и проверка установленной пользовательской версии не входят в тикет.
- Изменения UI-контракта описать в scoped wiki/guide. Lead синхронизирует `SPEC.md` §20 перед финальным одобрением; Senior не редактирует Lead-owned SSOT.

## Efficiency and Stop Rules
- Не повторять неизменённый тест/команду. Разрешена одна исправленная попытка после доказанной причины.
- После второго отказа той же границы остановиться. После PASS остановиться, не переходить к release.
- BLOCKED: отсутствуют authority, identity, validation mechanism или требуется решение вне scope; обычная пустая выборка не blocker.
- Любой обязательный NO/NOT_YET_PROVEN запрещает PASS. Не ослаблять AC ради прохождения.

## Evidence Contract
- Delivery pack: final revision, changed files, per-AC test names/results, redacted UI smoke, remaining uncertainty.
- Не выдавать текущий черновик или наличие теста за результат тестирования.
- Не помещать DB, реальные timer names, local user paths, credentials или сырые исключения в evidence.

## Rollback Plan
- Откат кода через обычный reviewed revert; schema остаётся v2.
- Пользователь восстанавливает archived timer через существующий Restore. Не переписывать пользовательскую БД автоматически.

## Deliverables
- Минимальное bulk archive действие, связанные проверки, scoped docs и per-AC evidence.
