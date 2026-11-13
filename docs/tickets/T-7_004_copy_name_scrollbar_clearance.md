# HANDOFF TICKET: Полоса прокрутки не перекрывает Copy name

## Meta
- Ticket ID: T-7_004_copy_name_scrollbar_clearance
- Gitea Issue: T-7 / #7
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
- Real ticket number is assigned only by workflow promotion. Until then use `T-7_004_copy_name_scrollbar_clearance`.
- Queue order and execution belong to AXLE; this draft does not register or launch a ticket.
- This project must not reopen already-fixed decisions unless this ticket explicitly says so.

## Workflow Metadata Block (engine-parsed)
Promotion binds the ticket to a Gitea issue and appends the canonical machine-written metadata.
Branch and source revision are not assigned in this draft. Use only identities returned by future promotion.

## Context
- Статус: DRAFT FOR PHIL REVIEW. До исполнения требуется доступный renderer и isolated profile.
- Фил сообщил: scrollbar при hover/expanded закрывает примерно половину Copy name и мешает нажать кнопку.
- Приложенный к исходной заявке screenshot в текущей сессии недоступен; не заявлять сравнение со screenshot.
- WHY отдельный тикет: layout имеет собственную rendered приёмку, которую не доказывают bulk-action tests.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `SPEC.md` §20 — Copy Name, theme/accessibility; `README.md` — runtime и validation boundaries.
- `src/GaleWorkTimer/Views/MainWindow.axaml`: outer ScrollViewer, ActiveTimers, archive Expander, copyName style.
- `src/GaleWorkTimer/Views/MainWindow.axaml.cs`: CopyTimerName.
- `src/GaleWorkTimer/ViewModels/MainWindowViewModel.cs`: CopyTimerNameAsync и snapshot semantics.

## Evidence State and Competing Explanations
- EVIDENCE, локальное чтение 2026-11-13: обе copy кнопки находятся справа в контенте общего ScrollViewer.
- EVIDENCE: у copyName margin справа 0; текущая разметка не задаёт отдельный scrollbar gutter.
- NOT_YET_PROVEN: root cause — overlay scrollbar действительно пересекает hit bounds кнопки; это пока HYPOTHESIS.
- Альтернативы: width/measure длинного имени; platform/theme-specific scrollbar template; масштабирование; устаревшая установленная сборка.
- Источники: пользовательский report описывает симптом, XAML — структуру, renderer — фактическую геометрию. Ни один не заменяет другой.
- SDK 10.0.401 и desktop environment присутствуют; успешный app launch, renderer access и Windows-specific reproduction ещё NOT_YET_PROVEN.
- Не переносить гипотезу в доказанный RCA. До исправления записать наблюдение, различающее эти альтернативы.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Views/MainWindow.axaml", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/copy-name-scrollbar.md", "docs/evidence/copy-name-scrollbar/**"]
- Forbidden Paths: ["SPEC.md", "docs/TZ.md", "docs/AXLE_PROJECT_KB.md", ".opencode/**", "src/GaleWorkTimer/Data/**", "src/GaleWorkTimer/Services/**", "src/GaleWorkTimer/ViewModels/**", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "worktrees/**"]
- После отдельного разрешения исполнения: native XAML layout fix и scoped docs/evidence; normal AXLE PR delivery.
- Сейчас разрешён только черновик. No promote, no Senior/Git/workflow mutations.
- Не менять clipboard handler, timer logic, schema, theme engine, framework/dependencies, startup/installer или пользовательскую БД.
- Если без изменения code-behind/styles вне scope исправление невозможно, stop и вернуть точную необходимую authority delta.

## Semantic Contract
- Copy button bounds/hit target и expanded scrollbar bounds не пересекаются; scrollbar сохраняет собственную кликабельность.
- Active и Archived используют одинаковую гарантию доступности кнопки, не обязательно одинаковую структуру строк.
- Имя, иконка, tooltip `Copy name`, accessible name `Copy timer name`, exact clipboard content не меняются.
- Длинное имя не вытесняет кнопку за viewport; допустима штатная обрезка/перенос имени без изменения clipboard string.
- При отсутствии вертикального overflow лишняя вертикальная полоса/накладка не появляется. При наличии вертикального overflow scrollbar работает и не перекрывает кнопку.
- Исправление не создаёт горизонтальный overflow и не вытесняет copy кнопку за viewport.
- Render evidence отсутствует: результат NOT_YET_PROVEN, не PASS по наличию margin/настройки в XAML.

## Authorized Execution Mechanism
- Native Avalonia layout/ScrollViewer properties в уже существующем MainWindow.axaml.
- Предпочтение — штатное резервирование места для scrollbar; локальный spacing допустим, если renderer доказывает достаточность.
- WHY: пространство должно принадлежать layout, а не обработчикам мыши; это сохраняет clipboard и scrollbar semantics.
- Не скрывать scrollbar, не уменьшать hit target, не поднимать кнопку поверх scrollbar через z-order и не добавлять custom scrollbar/hover JS-like logic.
- Воспроизвести на synthetic dataset в isolated application data profile. Не запускать приложение на личной БД для smoke.
- Если доступен только Linux renderer, проверять реальные geometry/hit bounds и явно ограничить платформенную достоверность. Windows runtime не утверждать без Windows evidence.
- Если репродукция отсутствует и альтернативы нельзя различить доступным renderer, вернуть BLOCKED с отсутствующим platform/reference evidence, не делать speculative margin patch.

## Acceptance Criteria
PASS разрешён только при доказательстве всех AC.
- [ ] AC1: Before-fix evidence фиксирует воспроизводимое перекрытие/hit obstruction либо точное renderer-доказательство конфликтующей геометрии. Root cause установлен, альтернативы из Evidence State проверены по относящимся к ним фактам.
- [ ] AC2: После исправления при достаточном числе active и archived timer expanded scrollbar не пересекает ни одну copy кнопку, включая верхнюю/нижнюю видимые строки. Hover и drag scrollbar не перекрывают target.
- [ ] AC3: Проверка выполнена при 1020×660 и minimum 760×420, Light/Dark, scaling 100%/150%, с короткими и длинными synthetic names. Обе секции проверены при overflow; отдельно — отсутствие overflow и collapsed scrollbar.
- [ ] AC4: Из всех проверяемых строк можно нажать на правую часть copy кнопки и вставить точное имя. Scrolling колесом и drag работают; copy не вызывает scroll/action другого timer.
- [ ] AC5: Tooltip, accessible name, keyboard focus/activation, Fluent icon и контраст имени сохраняются. Clipboard error path и duration snapshot semantics не изменены.
- [ ] AC6: Изменения ограничены layout и связанными docs/evidence. `dotnet build` и существующие tests проходят; нет новых dependencies, schema/data changes или polling.

## Required Semantic Proof
- Таблица каждой комбинации AC3: viewport, theme, scale, overflow state, active/archive, результат geometry и click.
- Redacted before/after captures с expanded scrollbar; synthetic names, без личных данных.
- Воспроизводимая процедура: создать synthetic timer до overflow, expand archive, hover/drag scrollbar, click Copy, paste exact name.
- Anti-collapse: «видна иконка» не означает «весь target кликабелен»; проверить именно ранее закрываемую правую часть кнопки.
- Screenshot/static XAML check не заменяет interaction proof. Основной путь — реальные desktop interactions; существующий renderer-backed hit-test допустим, если не требует нового harness/framework или расширения scope.

## Validation Boundaries
- Доступный development-host renderer — минимальный механизм; отсутствие доступа означает BLOCKED, а не отказ от UI AC.
- Скриншот из исходной заявки не обязателен при доказанной локальной репродукции; отсутствие репродукции требует platform/reference evidence.
- Existing CopyTimerNameAsync tests и один final `dotnet test`; XAML compile через `dotnet build`.
- Платформу и runtime revision указать точно. Linux evidence не называть Windows verification; user release/Windows packaging вне scope.
- Новые shared SSOT semantics не вводятся: это восстановление существующей доступности Copy Name. Wiki описывает доказанный layout fix; Lead-owned SPEC не изменять.

## Efficiency and Stop Rules
- Не повторять неизменённый probe/test; одна corrected retry после конкретной причины, stop после второго отказа той же границы.
- Не добавлять настройки «на будущее» или UI test framework ради обхода недоступного desktop.
- При scope conflict или недоступном renderer/platform evidence остановиться с точной причиной.
- Любой обязательный NO/NOT_YET_PROVEN запрещает PASS; после PASS не начинать packaging/deploy.

## Evidence Contract
- Final revision, changed files, per-AC results, environment/platform и redacted synthetic screenshots/hit-test evidence.
- Не сохранять личные DB, clipboard contents, имена пользовательских timer или private paths.
- Незапущенные проверки перечислить отдельно. Не выдавать исходное сообщение пользователя за результат smoke.

## Rollback Plan
- Reviewed revert XAML/doc changes; никаких data/schema changes.
- После revert Copy name и scrollbar сохраняют прежние функции; проблема перекрытия может вернуться и не считается исправленной.

## Deliverables
- Минимальное native layout исправление, доказанный RCA, rendered interaction evidence и scoped documentation.
