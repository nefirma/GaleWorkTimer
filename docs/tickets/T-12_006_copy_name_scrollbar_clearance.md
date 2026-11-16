# HANDOFF TICKET: Полоса прокрутки не перекрывает Copy name

## Meta
- Ticket ID: T-12_006_copy_name_scrollbar_clearance
- Gitea Issue: T-12 / #12
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Ограниченное изменение native Avalonia layout с воспроизведением и desktop-проверкой, без domain changes.
- Model Budget:
  - Estimated tokens: 14000
  - Reasoning effort: low
  - Rationale: Первый bounded UI проход; сначала воспроизвести, затем минимально исправить layout.
- Components: [UI]
- Priority: P1
- Risk level: Low

## Goal
Copy name остаётся полностью видимой и кликабельной у active и archived timer при collapsed и expanded scrollbar.

## Workflow Authority
- This markdown file is authoritative for execution brief, source references, AC, validation, and rollback notes.
- Real ticket number is assigned only by workflow promotion. Until then use `T-12_006_copy_name_scrollbar_clearance`.
- Queue order is engine-decided, not board-decided: among Ready tickets the engine launches the first by numeric ticket id whose `Depends on` are all Done. This ticket declares no `Depends on`; numeric queue order places the Delete-all ticket first.
- This project must not reopen already-fixed decisions unless this ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends the canonical machine-written metadata.
Branch and source revision are not assigned in this draft. Use only identities returned by future promotion.

## Context
- Статус: DRAFT FOR PHIL REVIEW. До исполнения требуется доступный renderer и isolated profile.
- Фил сообщил: scrollbar при hover/expanded закрывает примерно половину Copy name и мешает нажать кнопку.
- Приложенный к исходной заявке screenshot в текущей сессии недоступен; не заявлять сравнение со screenshot.
- T-5 (`Archive all`) уже Done и смёржен; его controls остаются baseline. Если Delete all смёржен раньше запуска этого тикета, он также является baseline и не должен быть затронут.
- WHY отдельный тикет: layout имеет собственную rendered приёмку, которую не доказывают bulk-action tests.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `docs/TZ.md` §20 — Copy Name, main controls, Archive all, theme/accessibility; `README.md` — runtime и validation boundaries.
- `src/GaleWorkTimer/Views/MainWindow.axaml`: outer ScrollViewer, ActiveTimers, archive Expander, copyName style.
- `src/GaleWorkTimer/Views/MainWindow.axaml.cs`: CopyTimerName.
- `src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs`: CopyTimerNameAsync и snapshot semantics.
- AXLE-owned native smoke fixture `/home/phil/axle-midi/scripts/galeworktimer-ui-smoke/` — read-only reference; do not edit it.

## Evidence State and Competing Explanations
### Static facts that may be trusted
- `EVIDENCE` (2026-11-16, T-5 merged head `987604bb2d46eb3ca416dc5c12a44513752214b4`, PR #8): обе copy кнопки находятся справа в контенте общего ScrollViewer; T-5 добавил `Archive all` в верхние Timers controls, copy layout не менял.
- `EVIDENCE`: у copyName margin справа 0; текущая разметка не задаёт отдельный scrollbar gutter.
- `EVIDENCE`: AXLE-owned native fixture строит реальный `MainWindow`, но его README прямо исключает scrollbar clearance из доказанного покрытия; fixture не является evidence для этого тикета и не редактируется.

### Expected but not proved
- `HYPOTHESIS`: root cause — overlay scrollbar действительно пересекает hit bounds кнопки.
- Альтернативы: width/measure длинного имени; platform/theme-specific scrollbar template; масштабирование; устаревшая установленная сборка; T-5 layout controls.
- `NOT_YET_PROVEN`: успешный app launch, renderer access и Windows-specific reproduction.
- Источники: пользовательский report описывает симптом, XAML — структуру, renderer — фактическую геометрию. Ни один не заменяет другой.
- Не переносить гипотезу в доказанный RCA. До исправления записать наблюдение, различающее эти альтернативы.

## Spec Alignment
- Matches: `docs/TZ.md` §20 требует one-click Copy Name с tooltip/accessible name и сохранением theme/title semantics; layout fix не меняет product semantics.
- Conflicts/unknowns: root cause не установлен; это и есть предмет before-fix evidence.
- Assumptions which this ticket must resolve: пересечение вызвано layout/scrollbar geometry, а не устаревшей сборкой или масштабом.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Views/MainWindow.axaml", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/copy-name-scrollbar.md"]
- Forbidden Paths: ["SPEC.md", "docs/AXLE_PROJECT_KB.md", ".opencode/**", "src/GaleWorkTimer/Data/**", "src/GaleWorkTimer/Services/**", "src/GaleWorkTimer/ViewModels/**", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "worktrees/**"]
- Worker Forbidden Paths: ["docs/TZ.md", "docs/ssot/**"]
- Lead Documentation Paths: []
- Delivery evidence хранится в ignored `.axle-midi/delivery/<ticket_id>_delivery_pack.md`; tracked `docs/evidence/**` outputs не добавляются.
- После отдельного разрешения исполнения: native XAML layout fix и scoped docs; normal AXLE PR delivery.
- Сейчас разрешён только черновик. No promote, no Senior/Git/workflow mutations.
- Не менять clipboard handler, timer logic, schema, theme engine, framework/dependencies, startup/installer или пользовательскую БД; AXLE fixture не редактировать.
- Если без изменения code-behind/styles вне scope исправление невозможно, stop и вернуть точную необходимую authority delta.

## Semantic Contract
- Copy button bounds/hit target и expanded scrollbar bounds не пересекаются; scrollbar сохраняет собственную кликабельность.
- Active и Archived используют одинаковую гарантию доступности кнопки, не обязательно одинаковую структуру строк.
- Имя, иконка, tooltip `Copy name`, accessible name `Copy timer name`, exact clipboard content не меняются.
- Длинное имя не вытесняет кнопку за viewport; допустима штатная обрезка/перенос имени без изменения clipboard string.
- При отсутствии вертикального overflow лишняя вертикальная полоса/накладка не появляется. При наличии вертикального overflow scrollbar работает и не перекрывает кнопку.
- Исправление не создаёт горизонтальный overflow и не вытесняет copy кнопку за viewport.
- Existing T-5 `Archive all` и (если присутствует) Delete all controls сохраняют позицию, доступность и поведение.
- Render evidence отсутствует: результат NOT_YET_PROVEN, не PASS по наличию margin/настройки в XAML.

## Contract / Interfaces
- Меняется только visual layout контейнера/scrollbar gutter; XAML names, bindings, handlers и clipboard output сохраняются.
- Никаких новых controls, settings, dependencies или custom scrollbar templates.

## Authorized Execution Mechanism
- Native Avalonia layout/ScrollViewer properties в уже существующем MainWindow.axaml.
- Предпочтение — штатное резервирование места для scrollbar; локальный spacing допустим, если renderer доказывает достаточность.
- WHY: пространство должно принадлежать layout, а не обработчикам мыши; это сохраняет clipboard и scrollbar semantics.
- Не скрывать scrollbar, не уменьшать hit target, не поднимать кнопку поверх scrollbar через z-order и не добавлять custom scrollbar/hover JS-like logic.
- Воспроизвести на synthetic dataset в isolated application data profile. Не запускать приложение на личной БД для smoke.
- Если доступен только Linux renderer, проверять реальные geometry/hit bounds и явно ограничить платформенную достоверность. Windows runtime не утверждать без Windows evidence.
- AXLE fixture можно запустить только как общий MainWindow regression; он не доказывает scrollbar clearance.
- Если репродукция отсутствует и альтернативы нельзя различить доступным renderer, вернуть BLOCKED с отсутствующим platform/reference evidence, не делать speculative margin patch.

## Forbidden Shortcuts
- Не считать «иконка видна» доказательством кликабельности всего target.
- Не заменять interaction proof static XAML check или добавленным margin без reproduction.
- Не скрывать scrollbar и не уменьшать его hit target.
- Не добавлять UI test framework или Headless package ради обхода недоступного desktop.
- Не менять clipboard semantics, duration snapshots или T-5 controls.

## Acceptance Criteria
PASS разрешён только при доказательстве всех AC.
- [ ] AC1: Before-fix evidence фиксирует воспроизводимое перекрытие/hit obstruction либо точное renderer-доказательство конфликтующей геометрии. Root cause установлен, альтернативы из Evidence State проверены по относящимся к ним фактам.
- [ ] AC2: После исправления при достаточном числе active и archived timer expanded scrollbar не пересекает ни одну copy кнопку, включая верхнюю/нижнюю видимые строки. Hover и drag scrollbar не перекрывают target.
- [ ] AC3: Проверка выполнена при 1020×660 и minimum 760×420, Light/Dark, scaling 100%/150%, с короткими и длинными synthetic names. Обе секции проверены при overflow; отдельно — отсутствие overflow и collapsed scrollbar.
- [ ] AC4: Из всех проверяемых строк можно нажать на правую часть copy кнопки и вставить точное имя. Scrolling колесом и drag работают; copy не вызывает scroll/action другого timer.
- [ ] AC5: Tooltip, accessible name, keyboard focus/activation, Fluent icon и контраст имени сохраняются. Clipboard error path и duration snapshot semantics не изменены.
- [ ] AC6: Изменения ограничены layout и связанными docs. `dotnet build` и существующие tests проходят; нет новых dependencies, schema/data changes или polling; AXLE fixture не изменён.
- [ ] AC7: Existing `Archive all` и, если он уже смёржен, Delete all остаются видимыми, доступными и не перекрытыми layout change.

## Required Semantic Proof
- Таблица каждой комбинации AC3: viewport, theme, scale, overflow state, active/archive, результат geometry и click.
- Redacted before/after captures с expanded scrollbar; synthetic names, без личных данных; ссылки из ignored delivery pack.
- Воспроизводимая процедура: создать synthetic timer до overflow, expand archive, hover/drag scrollbar, click Copy, paste exact name.
- Anti-collapse: «видна иконка» не означает «весь target кликабелен»; проверить именно ранее закрываемую правую часть кнопки.
- Screenshot/static XAML check не заменяет interaction proof. Основной путь — реальные desktop interactions; существующий renderer-backed hit-test допустим, если не требует нового harness/framework или расширения scope.
- AXLE fixture run, если выполнен, подтверждает только общий MainWindow smoke и не засчитывается как AC2–AC4.

## Validation Boundaries
- Доступный development-host renderer — минимальный механизм; отсутствие доступа означает BLOCKED, а не отказ от UI AC.
- Скриншот из исходной заявки не обязателен при доказанной локальной репродукции; отсутствие репродукции требует platform/reference evidence.
- Existing CopyTimerNameAsync tests и один final `dotnet test`; XAML compile через `dotnet build`.
- Delivery evidence: ignored `.axle-midi/delivery/<ticket_id>_delivery_pack.md`; validation runs сообщают observed identity по текущему harness contract; не добавлять tracked evidence outputs после validation.
- Платформу и runtime revision указать точно. Linux evidence не называть Windows verification; user release/Windows packaging вне scope.
- Новые shared SSOT semantics не вводятся: это восстановление существующей доступности Copy Name. Wiki описывает доказанный layout fix; Senior не изменяет Lead-owned `docs/TZ.md`.

## Efficiency and Stop Rules
- Не повторять неизменённый probe/test; одна corrected retry после конкретной причины, stop после второго отказа той же границы.
- Не добавлять настройки «на будущее» или UI test framework ради обхода недоступного desktop.
- При scope conflict или недоступном renderer/platform evidence остановиться с точной причиной.
- Любой обязательный NO/NOT_YET_PROVEN запрещает PASS; после PASS не начинать packaging/deploy.

## Security Notes
- Synthetic timer names и temporary profile only; не сохранять личные DB, clipboard contents или private paths.
- Redacted evidence без пользовательских имён и локальных путей.
- Новые dependencies, network и telemetry не требуются и не разрешены.

## Evidence Contract
- Delivery pack: final revision, changed files, per-AC results, environment/platform и redacted synthetic captures/hit-test evidence.
- Validation runs: observed result identity по текущему harness contract; не встраивать aggregate digest в tracked changed file.
- Незапущенные проверки перечислить отдельно. Не выдавать исходное сообщение пользователя за результат smoke.
- Не сохранять личные DB, clipboard contents, имена пользовательских timer или private paths.

## No-Architecture-Drift Rule
- Senior не перепроектирует layout, theme engine, clipboard flow или scroll architecture; только минимальное native layout исправление в scope.

## Rollback Plan
- Reviewed revert XAML/doc changes; никаких data/schema changes.
- После revert Copy name и scrollbar сохраняют прежние функции; проблема перекрытия может вернуться и не считается исправленной.

## Deliverables
- Минимальное native layout исправление, доказанный RCA, rendered interaction evidence в ignored delivery pack и scoped documentation.
