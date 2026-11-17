# HANDOFF TICKET: Copy name — разделение кнопки и scrollbar с UI-evidence waiver

## Meta
- Ticket ID: T-14_007_copy_name_layout_waiver
- Gitea Issue: T-14 / #14
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Один ограниченный XAML layout fix; copy semantics сохраняются, границы и validation определены.
- Model Budget:
  - Estimated tokens: 14000
  - Reasoning effort: low
  - Rationale: минимальный профиль достаточен для XAML, документации, build и existing tests.
- Components: [UI] [DOCS]
- Priority: P1
- Risk level: Low

## Goal
Goal: Deliver a statically justified XAML-only copy-name/scrollbar layout fix for active and archived rows; actual overlap removal without renderer remains NOT_YET_PROVEN under Phil's UI-evidence waiver.
Изменить layout active/archived Copy name, чтобы отделить кнопку от области scrollbar, сохранив wiring и accessibility; доставить статически обоснованное исправление с явно ограниченным UI-доказательством.

## Workflow Authority
- Этот новый brief определяет разрешённое исполнение, AC, validation и evidence.
- Real ticket number is assigned only by workflow promotion. Until then use `T-14_007_copy_name_layout_waiver`.
- Phil прямо разрешает исполнение нового тикета после успешного native AXLE promotion, без повторного запроса разрешения.
- Текущая сессия Lead выполняет только создание и promotion; не запускает worker, build, tests или исправление.
- AXLE владеет обычным исполнением после promotion, review и merge gates. Это не разрешение Lead вручную запускать worker.
- T-12 отменён Phil. Не восстанавливать и не использовать его branch, worktree, WIP, brief, IR или workflow state.
- Не использовать старые drafts или promotion worktrees. Новая ticket branch должна происходить от актуальной authority base зарегистрированного проекта.
- Depends on отсутствует: отменённый тикет не является зависимостью или источником результата.

## Workflow Metadata Block (engine-parsed)
Promotion присваивает новый ID и создаёт канонические branch/workflow metadata. Не придумывать ID или переносить metadata старого тикета.

## Context
Phil сообщил о перекрытии Copy name полосой прокрутки. Нужен XAML-only layout fix, не изменение clipboard или timer logic.

## Source of Truth (must read)
- `.opencode/project-config.yaml`, `docs/AXLE_PROJECT_KB.md`, `docs/wiki/INDEX.md`.
- `docs/TZ.md` §20, Copy Name: только чтение; изменение запрещено обеим ролям в этом задании.
- `src/GaleWorkTimer/Views/MainWindow.axaml`, `README.md`, `USER_GUIDE_RU.md` актуальной ticket base.
- Existing copy wiring и existing tests разрешено читать, но не изменять за пределами scope.
- Этот запрос Phil — источник authorization и UI-evidence waiver; исторические тикеты не являются authority.

## Evidence State
- EVIDENCE, 2026-11-17: registered project `galeworktimer`, repository `Phil/galeworktimer`, authority remote `origin`, base branch `main`.
- Timestamped observation: свежая authority `refs/heads/main` = `f9f6edd09ed33dafeb53301802fc23653c7f9989`; перед promotion проверить свежесть native preflight/reconciliation.
- EVIDENCE, 2026-11-17: native reconciliation выполнила fast-forward main; local main = origin/main = `f9f6edd09ed33dafeb53301802fc23653c7f9989`, equality=true.
- EVIDENCE после reconciliation: MainWindow.axaml строки 19–24, 53–122: обе copyName кнопки находятся в общем ScrollViewer; style задаёт правый margin 0. Archived теперь содержит существующую Delete all кнопку; сохранить её без изменений поведения.
- HYPOTHESIS: область overlay scrollbar не отделена от правого края row content. Root cause UNPROVEN до статического анализа свежей базы.
- NOT_YET_PROVEN: runtime geometry, фактическое перекрытие и фактическое устранение overlap без renderer.
- Graphify unavailable: workspace-local graph.json отсутствует. Использовать точечный анализ XAML и существующих ресурсов.

## Spec Alignment
- Product semantics `docs/TZ.md` сохраняются: exact Timer Name, active/archived parity, прежний clipboard/error path, без timer/snapshot mutation.
- Waiver меняет только требуемое доказательство этого тикета, не продуктовый контракт.
- docs/TZ.md не требует и не получает правки. Lead Documentation Paths пуст.
- При доказанном конфликте с текущим контрактом остановиться; не менять SSOT или scope самостоятельно.

## Authorization and Scope
- Scope Paths: ["src/GaleWorkTimer/Views/MainWindow.axaml", "README.md", "USER_GUIDE_RU.md", "docs/wiki/pages/copy-name-scrollbar.md"]
- Forbidden Paths: ["Services/**", "**/Services/**", "ViewModels/**", "**/ViewModels/**", "src/GaleWorkTimer/Views/MainWindow.axaml.cs", "**/MainWindow.axaml.cs", "docs/TZ.md", "docs/ssot/**"]
- Worker Forbidden Paths: ["docs/TZ.md", "docs/ssot/**"]
- Lead Documentation Paths: []
- Allowed Action Categories: ["READ_ONLY_LOCAL", "WORKSPACE_MUTATION", "VCS_MUTATION"]
- Human Gates: []
- Validation Contract: ["Phil authorizes execution of this new ticket after successful native AXLE promotion without a further approval request; Lead performs creation/promotion only, with no manual worker launch.", "Required: successful dotnet build and dotnet test with existing tests; provide commands, exit codes, source revision and actual coverage limits.", "Required: static layout reasoning for active/archived rows, hidden/visible/expanded scrollbar, archive collapsed/expanded, long/short names, minimum/normal width and Light/Dark.", "Required: static UI wiring/accessibility proof preserving CopyTimerName handler, row data context, icon, tooltip, accessible name, focusability and theme semantics; preserve existing clipboard success/failure and timer/snapshot behavior.", "UI-evidence waiver approved by Phil: manual interaction and automatic renderer-backed checks are not required; absence of these checks is not a blocker. Фактическое устранение overlap без renderer остаётся NOT_YET_PROVEN. State this limitation in delivery and documentation; do not claim visual proof.", "No new dependencies or UI harness; no changes outside the four Scope Paths. Services/**, ViewModels/** and MainWindow.axaml.cs mutations are forbidden. Lead Documentation Paths is empty; Lead and Senior have no authorization to edit docs/TZ.md.", "README.md, USER_GUIDE_RU.md and docs/wiki/pages/copy-name-scrollbar.md must agree with the fix, actual validation and waiver. PASS requires AC1-AC5 within the explicitly waived evidence boundary.", "The promoted IR must use the newly assigned ticket ID and match this brief. Do not restore or reuse cancelled T-12 branch, worktree, WIP or workflow state."]
- Git outcome: AXLE_NORMAL_FLOW.
- После promotion Senior разрешены только указанные четыре пути: XAML layout fix и согласованные описания fix/waiver в трёх Markdown-файлах.
- Отдельного разрешения Lead редактировать docs/TZ.md нет. Абсолютный запрет действует для Lead и Senior.
- Promotion может создавать собственные ticket/IR metadata через native AXLE; это не расширяет execution Scope Paths.
- Не менять code-behind, Services, ViewModels, tests, project/dependency/config files. Не добавлять dependencies или UI harness.
- Не разрешены deploy, release, публикация, операции с пользовательской БД, runtime/service changes или ручное управление workflow.

## Layout / UI Contract
- Кнопки active и archived используют существующее имя строки и существующий Click handler `CopyTimerName`.
- Сохранить copy icon, tooltip `Copy name`, accessible name `Copy timer name`, keyboard focus и существующую theme foreground семантику.
- Зарезервировать пространство между правой границей copy hit target и scrollbar, включая overlay/expanded состояние; механизм выбрать по текущему XAML и установленной Avalonia.
- Применить один согласованный layout принцип к active и archived rows. Не отключать scrolling и не прятать кнопку вместо исправления.
- При длинном имени и минимально допустимой ширине ограничивать область текста, а не вытеснять copy кнопку за viewport.
- При отсутствии vertical overflow не форсировать лишний scrollbar. При overflow сохранить wheel/drag semantics через неизменённую scrollbar wiring.
- При collapsed/expanded archive, Light/Dark и коротком/длинном имени сохранить существующие controls и цветной title.
- Clipboard success/failure и timer/snapshot semantics остаются прежними; не изменять их реализацию или поведение.

## Authorized Execution Mechanism
- Native AXLE запускает cfg-senior на новой ticket branch/worktree от authority base. Не использовать ресурсы отменённого тикета.
- Senior читает свежую базу, изменяет разрешённый XAML и Markdown, запускает существующие .NET build/tests через доступный локальный SDK.
- Статическое исследование code-behind/VM/test files разрешено только для доказательства сохранения wiring.
- Если минимальный fix требует запрещённого файла или недоступной зависимости, вернуть BLOCKED с причиной; scope не расширять.

## Acceptance Criteria
PASS разрешён только при совместном выполнении AC1–AC5 с UI-evidence waiver ниже.

- [ ] AC1: В XAML реализовано явное разделение copy hit target и scrollbar области для active и archived rows. Статическое обоснование указывает контейнеры, размеры/ограничения, scrollbar state и почему выбранное пространство защищает кнопку.
- [ ] AC2: Статически разобраны ветки: scrollbar hidden/visible/expanded, archive collapsed/expanded, длинное/короткое имя, минимальная/обычная ширина, Light/Dark. Нет отключения scrolling, сокрытия кнопки или нового горизонтального overflow по выбранным constraints. Не выдавать этот анализ за проверку renderer geometry.
- [ ] AC3: Статическое сравнение UI wiring подтверждает прежний Click handler, row data context, tooltip, accessible name, icon, focusability и theme semantics для обеих кнопок. Existing tests подтверждают прежний clipboard success/failure и отсутствие изменений timer/snapshot logic в пределах их фактического покрытия.
- [ ] AC4: Успешны `dotnet build` и `dotnet test` существующего solution со всеми existing tests. Новые dependencies, tests и UI harness не добавлены; нет изменений запрещённых файлов.
- [ ] AC5: README.md, USER_GUIDE_RU.md и docs/wiki/pages/copy-name-scrollbar.md согласованно описывают layout change, сохранение функций, выполненные статические/build/test проверки и ограничение доказательств. Promoted IR связан с новым ID и соответствует brief, scope, authorization и waiver.

## UI-evidence waiver
- Phil явно освобождает этот тикет от manual interaction и automatic renderer-backed проверок.
- Не требовать ручной click/paste/hover/drag, UI smoke, screenshot, renderer harness или headless rendered geometry для PASS.
- Фактическое устранение overlap без renderer остаётся `NOT_YET_PROVEN`. Указать это дословно в delivery и документации.
- Waiver-scoped PASS означает: разрешённый fix доставлен, build/existing tests успешны и статическое обоснование полно. Это НЕ подтверждение визуального устранения overlap.
- Отсутствие manual/renderer evidence само по себе не является BLOCKED или причиной требовать новый harness.
- Любой другой обязательный AC со статусом NO или NOT_YET_PROVEN запрещает PASS. Waiver не отменяет build, existing tests, wiring/accessibility или scope checks.

## Validation Boundaries
- Lead: новый brief, native promotion, новый ID, fresh authority ancestry и соответствие persisted IR. Не исполнять реализацию в текущей сессии.
- Senior: один final `dotnet build`, один final `dotnet test` после изменения; использовать существующий SDK и repository commands.
- Static proof: exact XAML lines/diff, layout reasoning по всем AC2 веткам, обе кнопки, существующие handler/data context/accessibility, changed-path audit.
- Existing tests не доказывают geometry. Если нет теста на конкретный UI аспект, назвать предел покрытия без добавления framework/harness.
- Runtime/renderer proof: waived; фактическое устранение overlap остаётся NOT_YET_PROVEN.

## Efficiency and Stop Rules
- Не повторять неизменённый command/test/probe без изменения кода или состояния.
- Разрешена максимум одна исправленная попытка после установленной причины; второй отказ той же границы завершает попытку.
- Остановиться после waiver-scoped PASS. Не начинать соседние UI задачи.
- BLOCKED: отсутствует authority, mechanism, SDK или решение, без которого требуется сменить scope/semantics. Не считать waived evidence блокером.

## Evidence Contract
- Дать per-AC ссылки на exact revision и строки/diff; build/test command, exit code и summary existing tests.
- Статическое обоснование и coverage limits сохранить в разрешённой wiki page; не создавать docs/evidence files вне scope.
- В delivery перечислить фактические changed files, подтвердить запреты и отсутствие dependencies/harness.
- Разделять EVIDENCE, HYPOTHESIS и NOT_YET_PROVEN. Не публиковать пользовательские данные, secrets, raw environment или clipboard content.

## Rollback Plan
- При необходимости отменить только изменения нового тикета через обычный AXLE PR/VCS путь. Не делать reset/clean/stash и не использовать прежние WIP.
- БД, timer data и clipboard реализация не меняются; rollback возвращает прежний XAML/docs и может вернуть overlap.

## Deliverables
- Минимальный layout diff в MainWindow.axaml и согласованные изменения трёх разрешённых Markdown-файлов.
- Delivery report с build/existing tests, static layout/UI wiring/accessibility proof и явным UI-evidence waiver.
