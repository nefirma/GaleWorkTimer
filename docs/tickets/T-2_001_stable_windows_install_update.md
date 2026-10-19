# HANDOFF TICKET: Стабильная Windows-установка с безопасным откатом

## Meta
- Ticket ID: T-2_001_stable_windows_install_update
- Gitea Issue: T-2 / #2
- Owner (Lead): Phil / TeamLead
- Recommended Executor: senior_min
- Executor Mode: low
- Why this execution profile is sufficient:
  - Первый ограниченный кандидат: известны пути, поведение, запреты и граница проверки; использовать существующий .NET SDK.
- Model Budget:
  - Estimated tokens: 26000
  - Reasoning effort: low
  - Rationale: минимальный профиль для первого выполнения; эскалация только по конкретному неразрешённому затруднению.
- Components: [OPS] [SECURITY] [DOCS]
- Priority: P1
- Risk level: High

## Goal
Подготовить проверяемый на CachyOS кандидат ручной Windows-установки/обновления в постоянный каталог с сохранением базы, безопасным откатом и понятной причиной сбоя, без заявления о пройденной Windows-проверке.

## Workflow Authority
- Этот файл определяет scope, поведение, AC, проверки и границу человеческой проверки.
- Номер T-N назначает только promotion; исходный GitHub issue #1 — входной материал, не workflow authority.
- Источник исполнения: зарегистрированный проект galeworktimer, Gitea Phil/galeworktimer, remote origin, рабочий путь /home/phil/Projects/galeworktimer.
- Исходная линия main; точные branch/SHA исполнения берутся из успешного promotion, не из старого письма.
- Ясень прямым письмом Lead подтвердил подтверждение задачи Филом в ChatGPT; это основание обычного AXLE execution и promotion.
- Phil напрямую уточнил отказ: безопасный откат предыдущей версии и показ причины, которую Галя может передать команде.
- Приёмка результата остаётся за Филом. Senior не развёртывает приложение на ноутбуке Гали.

## Context
Текущий USER_GUIDE_RU.md предписывает распаковку каждой версии в отдельный каталог. В найденной структуре отсутствует механизм установки/обновления. Нужен один кандидат безопасной упаковки; отдельная живая Windows-проверка — человеческая граница, не скрытое требование к агентскому runner.

## Source of Truth (must read)
- SPEC.md, разделы 41–43: offline, self-contained win-x64, постоянный install location, изоляция Windows-интеграций.
- README.md: команды сборки/тестов, база, существующая граница Linux/Windows validation.
- USER_GUIDE_RU.md: текущая установка, сохранность данных и пользовательская инструкция.
- global.json, GaleWorkTimer.sln, src/GaleWorkTimer/GaleWorkTimer.csproj, tests/GaleWorkTimer.Tests/GaleWorkTimer.Tests.csproj: существующий toolchain и тестовый подход.
- GitHub https://github.com/nefirma/GaleWorkTimer/issues/1 — только дополнительный входной материал, не источник разрешений.

## Evidence State
- EVIDENCE, 2026-10-20: прямые письма Ясеня Lead подтверждают задачу и каталог; прямое письмо Phil подтверждает откат и показ причины.
- EVIDENCE, 2026-10-20: ai-engineer передал решение Фила — релевантные проверки на CachyOS, реальная Windows-проверка Галей; runner не является предварительным условием.
- EVIDENCE, 2026-10-20: /home/phil/.dotnet/dotnet --version вернул 10.0.401; PowerShell на PATH отсутствует.
- EVIDENCE: SPEC §42 уже рекомендует постоянный каталог, но текущая инструкция требует отдельную папку. Исправляется инструкция, не логика таймеров.
- NOT_YET_PROVEN: исполнение Windows-кандидата, Windows file locking, ярлыки и восстановление на реальной Windows.
- docs/wiki/INDEX.md отсутствует; локальный graphify-out/graph.json отсутствует. Не создавать вымышленную ссылку на существующую wiki.
- Старое письмо о равенстве main/origin/main не является текущим SHA-доказательством; preflight проверяет актуальное состояние.

## Authorization and Scope
- Scope Paths: ["packaging/**", "tests/GaleWorkTimer.Packaging.Tests/**", "GaleWorkTimer.sln", "README.md", "USER_GUIDE_RU.md", "docs/wiki/packaging.md", "docs/tickets/T-2_001_stable_windows_install_update.md"]
- Forbidden Paths: ["src/**", "SPEC.md", ".opencode/**", ".github/**", "docs/context5.md", "docs/teamlead_KB.md", "docs/knowledge_base.md"]
- Allowed Action Categories: ["READ_ONLY_LOCAL", "WORKSPACE_MUTATION", "VCS_MUTATION"]
- Authorized: .NET packaging helper, отдельные packaging-тесты, воспроизводимая сборка кандидата, пользовательская инструкция и компактная wiki-страница.
- Git outcome: AXLE_NORMAL_FLOW. Доставка только через ticket branch и PR. Merge и workflow transitions принадлежат AXLE.
- Не разрешены: публикация release, изменение GitHub issue, установка на реальную машину Гали, удалённые операции, изменение базы/схемы/таймеров, служба автообновления, telemetry, новый сетевой канал, administrator/UAC requirement.
- Не менять чужие файлы для обхода promotion/delivery. Если promoted IR сузил или изменил scope, остановиться для Lead.
- src/** только читать и публиковать существующим dotnet publish; код приложения не менять.

## Semantic Contract
### Inventory and derivation
- install_root = %LOCALAPPDATA%\Programs\GaleWorkTimer\ для текущего пользователя; значение не берётся из архива.
- data_path = %LOCALAPPDATA%\NeFirma\GaleWorkTimer\data.db; установщик не открывает, не переносит, не заменяет и не удаляет базу или её соседние SQLite-файлы.
- stable_entry = install_root\GaleWorkTimer.exe; после первоначальной установки путь не меняется между обновлениями.
- installed_version = идентификатор фактически выбранного полностью проверенного payload, а не имя скачанного архива.
- update_result: installed | updated | rolled_back | rejected | recovered_previous; успех не может означать, что просто скачан или распакован пакет.
- failure_reason = безопасный код этапа и понятная причина; если обновление оборвалось без exception, причина явно «обновление прервано», а не выдуманное исключение.

### State and safety invariants
- Не обновлять payload поверх работающей версии. Сначала проверить пакет в staging, сохранив предыдущий полный payload.
- Постоянный launcher/recovery entry не уничтожается при замене payload. Использовать ограниченное число внутренних слотов и восстанавливаемое состояние переключения, а не отдельную пользовательскую папку для каждого выпуска.
- При незавершённом переключении следующий запуск через stable_entry сначала восстанавливает предыдущий проверенный payload и показывает причину. Неполный payload не запускается.
- Первоначальная установка не имеет предыдущей версии: при отказе нет ложного installed/rollback; остаётся безопасная повторная установка без частичного запуска.
- Для первого перехода со старого portable ZIP инструкция один раз переводит пользователя на stable_entry. Не обещать исправление произвольных ярлыков на старые каталоги; созданный стабильный путь далее не меняется.
- Успех обновления означает проверенный и переключённый payload; это не доказательство GUI-работоспособности на Windows.
- Не менять и не удалять portable-каталог или базу при переходе. Временные staging/backup допустимы; завершённые обновления не накапливают неограниченно каталоги выпусков.

## Authorized Execution Mechanism
- Локальная разработка .NET 10 packaging helper под packaging/ и отдельного тестового проекта, включённого в существующий solution.
- Использовать .NET stdlib для файлов, проверки пакета и процесса запуска. Использовать существующий xUnit-подход, не вводить новый framework или installer tooling.
- Отделить проверяемую файловую транзакцию от Windows shell-операций; на CachyOS реальные временные файлы проверяют алгоритм, Windows поведение явно остаётся непроверенным.
- Паковать существующий app как self-contained win-x64. Launcher/installer также не требует отдельной установки .NET у Гали.
- Не исполнять распакованные скрипты/команды из метаданных пакета. Проверить целостность и обязательные файлы до переключения.
- Сборка/тесты доступны через /home/phil/.dotnet/dotnet; обычный SDK PATH допустим при той же версии global.json.
- Если механизм безопасного переключения невозможно реализовать в указанном scope, вернуть конкретный BLOCKED; не подменять откат ручной переустановкой.

## Acceptance Criteria
### Candidate acceptance: обязательны все пункты C1–C7
- [ ] C1 — Кандидат включает воспроизводимую win-x64 self-contained упаковку launcher/installer и неизменённого приложения, инструкцию установки в install_root без administrator rights и без отдельного runtime.
- [ ] C2 — Файловые тесты initial install → update A→B доказывают один stable_entry, фактическую смену выбранного payload и отсутствие накопления каталогов версий; повторное применение того же пакета безопасно.
- [ ] C3 — Контролируемый сбой до/во время переключения восстанавливает A; причина доступна пользователю для передачи команде. Не выдавать partial B за успех.
- [ ] C4 — Детерминированные прерывания на каждой границе persistent state/rename/write восстанавливаются следующим запуском через stable_entry: запускается предыдущий полный payload и показывается причина. Отдельно проверить отказ первоначальной установки без предыдущего payload.
- [ ] C5 — Успех, отказ, повтор и recovery сохраняют байты sentinel data.db и соседних файлов в отдельном временном data-каталоге. Повреждённый/неполный архив, выход путей за staging, конфликт параллельных updater и занятые файлы не повреждают установленный payload или данные.
- [ ] C6 — USER_GUIDE_RU.md описывает первую миграцию из portable ZIP, обычное обновление, закрытие запущенного приложения, стабильный запуск, rollback/recovery и копирование безопасной причины. README и docs/wiki/packaging.md согласованы с реальными командами, артефактом и границей доказательств.
- [ ] C7 — Существующие build/test и новые packaging checks проходят на CachyOS; статическая проверка win-x64 publish/упаковки проходит. Доставка содержит точную таблицу C1–C7, SHA кандидата, команды/exit codes и отдельный список непроверенных Windows-пунктов.

### Human Windows validation: не является precondition доставки кандидата
- [ ] W1 — Галя устанавливает кандидат без admin/.NET, запускает stable_entry и видит прежние Timer/Total/History.
- [ ] W2 — На Windows обновление сохраняет тот же стабильный путь и ранее созданный для него ярлык; фактически запускается новая версия.
- [ ] W3 — На безопасном тестовом install-root контролируемый отказ и прерывание обновления сохраняют/восстанавливают предыдущую запускаемую версию; Галя может передать показанную причину. Не создавать сбой на единственной рабочей копии.
- [ ] W4 — Windows locking, кириллица/пробелы в пользовательском пути и сохранность данных подтверждены человеком.
- PASS кандидата = C1 AND C2 AND C3 AND C4 AND C5 AND C6 AND C7; любой required NO/NOT_YET_PROVEN запрещает этот PASS.
- Полный Windows PASS запрещён до W1–W4. До свидетельств Гали W1–W4 = NOT_YET_PROVEN, даже если C1–C7 прошли. Решение о приёмке остаётся у Фила.

## Required Semantic Proof
- Реальные временные каталоги с различимыми payload A/B и sentinel data; утверждения проверяют выбранные байты/версию, а не наличие названий полей.
- Fault injection на изменяющих состояние границах и запуск recovery новым экземпляром helper, без полагания на catch/finally прерванного процесса.
- Негативные пакеты: отсутствующий обязательный файл, неверная целостность, traversal/абсолютный путь и link/reparse escape; отклонение до изменения рабочей установки.
- Семантическая ловушка: пакет B распакован, но переключение не завершено; нельзя сообщить updated или запустить частичный B.
- Блокировки и Windows shell проверки на Linux только моделируются; симуляция не является W-доказательством.

## Validation Boundaries
- Lead: пути, rollback и граница Windows согласованы прямой перепиской; найден текущий per-version install flow и проверен SDK 10.0.401.
- Local: dotnet build, dotnet test, packaging faults/recovery с отдельными temp paths; win-x64 publish и проверка состава пакета. Не читать настоящую базу пользователя.
- Human: W1–W4 на Windows ноутбуке Гали после передачи кандидата; агентский Windows runner не требовать.
- Физическое отключение питания и гарантии диска не объявлять доказанными тестом исключений; перечислить остаточные ограничения, не скрывая обязательное recovery после прерывания процесса.
- Перед review Lead проверяет необходимость дополнения SPEC §42; Senior SPEC не редактирует. Новая wiki описывает кандидат и его реальные ограничения, не объявляет Windows release принятым.

## Efficiency and Stop Rules
- Не повторять команду/тест/probe без изменения релевантного кода или состояния. Допустим один исправленный повтор после установленной причины; после двух отказов той же границы остановиться.
- Не перечитывать доверенные решения и не искать Windows runner вместо реализации кандидата.
- BLOCKED только при недостающей authority, identity, mechanism или новом решении, меняющем согласованные семантику/границы. Обычный провал теста требует исправления в scope, не нового дизайна продукта.
- После C1–C7 остановиться и передать кандидат с непроверенными W-пунктами. Не публиковать и не устанавливать его Гале автоматически.
- Senior при дополнительном поиске кода делегирует cfg-code-explorer. Android Gradle/ADB к этому .NET-проекту не относятся.

## Security and Rollback
- Только локальная установка текущего пользователя. Исключить archive traversal, произвольный install-root из пакета и выход через links/reparse points.
- Исключить updater races и запуск непроверенного/частичного payload. Не завершать чужие процессы принудительно; попросить закрыть приложение или безопасно отказать.
- Показать полезную причину без stack trace, имени профиля, полного приватного пути, данных БД и credentials. Не загружать отчёты в сеть.
- Не доверять digest как доказательству издателя; не добавлять auto-download, signing PKI или сетевой updater.
- Откат кандидата в разработке: ticket PR revert обычным review workflow. Откат пользовательского обновления: предыдущий полный payload + recovery state, данные вне транзакции установки.

## Deliverables and Evidence Contract
- Source/helper и упаковка под packaging/, тесты под tests/GaleWorkTimer.Packaging.Tests/, integration в GaleWorkTimer.sln; README, пользовательская инструкция, компактная wiki.
- В delivery pack: изменённые пути, source revision, build/test/publish результаты, SHA-256 кандидата и точный способ воспроизведения.
- Таблица state/result/version/reason: источник, success, failure, interrupted recovery, unavailable behavior.
- C1–C7 evidence отдельно от W1–W4 NOT_YET_PROVEN. Никаких необоснованных утверждений о Windows runtime или приёмке Филом.
- Бинарные пакеты, БД, приватные логи и credentials в Git не добавлять. Candidate хранить как локальный build output, путь и digest сообщить redacted безопасно.
