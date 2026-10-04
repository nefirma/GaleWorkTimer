# GaleWorkTimer — Product & Technical Specification

**Status:** implementation-ready draft  
**Target:** Windows 11 x64  
**Primary user:** Gala  
**Development host:** Linux / CachyOS  
**Application type:** local desktop application  
**Network/backend:** none  
**Primary storage:** SQLite  

---

# 1. Goal

GaleWorkTimer — персональное приложение для учёта рабочего времени.

Главное отличие от обычных time trackers: **во время работы приложение не показывает постоянно меняющийся счётчик времени**.

Пользователь запускает один или несколько таймеров, скрывает приложение в system tray и занимается работой. Время показывается после Pause/Stop либо по явному запросу пользователя через History/Statistics.

Приложение предназначено для помощи пользователю, а не для контроля, аудита или доказательства отработанного времени.

Следствия:

- пользователь может свободно редактировать историю;
- нет audit trail;
- нет защиты от изменения данных;
- нет аккаунтов;
- нет сервера;
- нет облачной синхронизации;
- нет telemetry;
- приложение не пытается автоматически определять, работает пользователь или нет.

---

# 2. Platform and stack

## Runtime

- Windows 11 x64
- C#
- .NET 10 LTS
- Avalonia 12
- SQLite
- `Microsoft.Data.Sqlite`
- Win32 `RegisterHotKey` / `UnregisterHotKey`
- Avalonia `TrayIcon`

## Build environment

Основная разработка выполняется на Linux/CachyOS.

На Linux должны работать:

- UI;
- domain logic;
- SQLite;
- session lifecycle;
- history;
- editing;
- validation;
- statistics;
- CSV export;
- unit/integration tests.

Windows 11 требуется для финальной проверки:

- global hotkeys;
- tray behaviour;
- autostart;
- Windows shutdown/reboot;
- sleep/hibernate;
- Windows notifications;
- packaged build.

## Architecture

Не создавать отдельные сервисы, backend или лишние процессы.

Предпочтительная структура:

```text
gale-work-timer/
├── src/
│   └── GaleWorkTimer/
│       ├── Domain/
│       ├── Data/
│       ├── Services/
│       ├── Platform/
│       │   └── Windows/
│       ├── Views/
│       ├── ViewModels/
│       └── App.axaml
├── tests/
│   └── GaleWorkTimer.Tests/
└── README.md
```

Логические слои не требуют отдельных .NET assemblies.

Не использовать:

- Electron;
- web backend;
- browser storage;
- Entity Framework;
- external database;
- dependency injection framework без реальной необходимости;
- background service;
- polling timer для подсчёта времени.

---

# 3. Core concepts

## Timer

Именованная категория работы.

Примеры:

```text
iOS testing
Android testing
Documentation
FamilyLine
```

Timer существует постоянно, пока пользователь вручную не уберёт его из рабочего списка.

Timer можно:

- создать;
- переименовать;
- назначить hotkeys;
- убрать из активного списка;
- вернуть в активный список.

Удаление Timer из списка **не удаляет историю**.

Технически это Archive/Unarchive.

---

# 4. Session

Session — один законченный или текущий период работы с конкретным Timer.

У одного Timer одновременно может существовать максимум **одна незавершённая Session**.

Разные Timer могут иметь незавершённые Session одновременно.

Пример:

```text
Timer A:
Start  10:00
Pause  10:45
Resume 11:10
Stop   12:00
```

Active work:

```text
10:00–10:45 = 45 min
11:10–12:00 = 50 min

Total = 95 min
```

Pause interval не считается рабочим временем.

---

# 5. Multiple simultaneous timers

Несколько Timer могут работать одновременно.

Например:

```text
iOS testing     10:00–10:40
Bug monitoring  10:00–10:40
```

Статистика:

```text
iOS testing      40 min
Bug monitoring   40 min
Total             80 min
```

Пересекающиеся интервалы разных Timer **не дедуплицируются**.

Это сознательное продуктовое решение: пользователь может одновременно выполнять две учитываемые работы.

---

# 6. Timer state machine

Состояния Timer:

```text
IDLE
RUNNING
PAUSED
```

Переходы:

```text
IDLE
 └─ Start ─────> RUNNING

RUNNING
 ├─ Pause ─────> PAUSED
 └─ Stop ──────> IDLE

PAUSED
 ├─ Resume ────> RUNNING
 └─ Stop ──────> IDLE
```

## Invalid commands

Следующие действия не создают записей в БД:

```text
Start  while RUNNING
Start  while PAUSED
Pause  while IDLE
Pause  while PAUSED
Resume while IDLE
Resume while RUNNING
Stop   while IDLE
```

UI может показать короткое сообщение, но не должен менять состояние.

---

# 7. No ticking counter

Это hard requirement.

Во время `RUNNING` приложение **не показывает постоянно меняющуюся длительность**:

- не в основном окне;
- не в tray icon;
- не в tray tooltip;
- не рядом с названием Timer;
- не в title bar.

Главный экран показывает только:

```text
iOS testing    ● Running
```

а не:

```text
iOS testing    01:37:42
```

Не должен существовать секундный UI timer, обновляющий elapsed time.

## Explicit statistics

History/Statistics — исключение.

Если пользователь явно открыл Statistics, приложение может вычислить snapshot на текущий момент.

Значение:

- вычисляется один раз;
- не обновляется каждую секунду;
- обновляется только при явном Refresh или повторном открытии view.

Таким образом нигде нет визуально тикающего времени.

---

# 8. Time calculation model

Приложение не должно хранить постоянно увеличивающийся counter.

Источник истины — timestamps.

Пример:

```text
started_at = 10:00
pause      = 11:00
resume     = 11:20
stop       = 12:00
```

Рабочее время вычисляется из интервалов.

Это позволяет приложению корректно переживать:

- process exit;
- crash;
- Windows reboot;
- shutdown;
- sleep;
- hibernation;
- работу пользователя с телефоном, пока ПК не используется.

---

# 9. Shutdown, reboot and sleep semantics

## RUNNING

Если Timer находился в `RUNNING`, то:

```text
10:00 Start
10:30 Windows reboot
10:35 Windows boot
11:00 Stop
```

результат:

```text
1 hour
```

Пять минут reboot входят во время.

То же правило действует для:

- shutdown;
- sleep;
- hibernation;
- закрытого приложения;
- killed process.

## PAUSED

Если Timer находился в `PAUSED`, время остановлено независимо от состояния приложения или Windows.

Пример:

```text
10:00 Start
10:30 Pause
10:40 reboot
11:30 Resume
12:00 Stop
```

Active work:

```text
10:00–10:30
11:30–12:00

Total = 60 min
```

---

# 10. Application process lifecycle

Каноническое поведение окна (решение T2.1; приоритет над прежними
формулировками Close → tray в последующих разделах):

```text
X / Close / Exit → полностью завершить application process
Minimize → обычное сворачивание; application process продолжает работать
Hide to tray → отдельное действие после появления tray в T3
```

Использовать штатные platform-native window decorations и controls.
Close не заменяется на Hide to tray, в том числе после реализации T3.
В T2.1 Hide to tray отсутствует.

Обычные in-window действия `Minimize` и `Exit` имеют ту же семантику,
что нативные Minimize и Close соответственно. Explicit `Exit` из будущего
tray также полностью завершает process.

При этом:

**Close, Exit и Minimize не останавливают RUNNING sessions и не меняют
состояние PAUSED sessions.**

Session продолжает логически идти по wall clock.

При следующем запуске состояние восстанавливается из SQLite.

Если пользователь хочет закончить работу — используется `Stop`.

---

# 11. Autostart

Приложение должно поддерживать per-user Windows autostart без administrator privileges.

Использовать штатный user-level Windows mechanism.

Предпочтительно:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Запуск:

```text
GaleWorkTimer.exe --autostart
```

`--autostart`:

- приложение запускается;
- main window не показывается;
- tray icon создаётся;
- SQLite открывается;
- состояния Timer восстанавливаются;
- global hotkeys регистрируются.

При обычном ручном запуске main window открывается.

Autostart можно включить/выключить в Settings.

---

# 12. SQLite storage

Расположение:

```text
%LOCALAPPDATA%\NeFirma\GaleWorkTimer\
```

Например:

```text
%LOCALAPPDATA%\NeFirma\GaleWorkTimer\data.db
```

База хранится бессрочно.

Автоматическое удаление истории запрещено.

Recommended SQLite setup:

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
```

Schema version хранить через:

```sql
PRAGMA user_version;
```

---

# 13. Database schema

## timers

```sql
CREATE TABLE timers (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT NOT NULL,
    is_archived     INTEGER NOT NULL DEFAULT 0,
    sort_order      INTEGER NOT NULL DEFAULT 0,
    created_at_ms   INTEGER NOT NULL,
    updated_at_ms   INTEGER NOT NULL
);
```

`*_ms` — UTC Unix milliseconds.

---

## sessions

```sql
CREATE TABLE sessions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    timer_id        INTEGER NOT NULL,
    started_at_ms   INTEGER NOT NULL,
    stopped_at_ms   INTEGER NULL,
    created_at_ms   INTEGER NOT NULL,
    updated_at_ms   INTEGER NOT NULL,

    FOREIGN KEY(timer_id)
        REFERENCES timers(id),

    CHECK (
        stopped_at_ms IS NULL
        OR stopped_at_ms > started_at_ms
    )
);
```

На Timer допускается максимум одна open Session:

```sql
CREATE UNIQUE INDEX ux_sessions_open_timer
ON sessions(timer_id)
WHERE stopped_at_ms IS NULL;
```

---

## session_pauses

```sql
CREATE TABLE session_pauses (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      INTEGER NOT NULL,
    paused_at_ms    INTEGER NOT NULL,
    resumed_at_ms   INTEGER NULL,

    FOREIGN KEY(session_id)
        REFERENCES sessions(id)
        ON DELETE CASCADE,

    CHECK (
        resumed_at_ms IS NULL
        OR resumed_at_ms > paused_at_ms
    )
);
```

Максимум одна незаконченная pause:

```sql
CREATE UNIQUE INDEX ux_session_open_pause
ON session_pauses(session_id)
WHERE resumed_at_ms IS NULL;
```

---

## hotkeys

```sql
CREATE TABLE hotkeys (
    timer_id        INTEGER NOT NULL,
    action          TEXT NOT NULL,
    modifiers       INTEGER NOT NULL,
    virtual_key     INTEGER NOT NULL,

    PRIMARY KEY(timer_id, action),

    FOREIGN KEY(timer_id)
        REFERENCES timers(id),

    CHECK (
        action IN ('start', 'pause', 'resume', 'stop')
    )
);
```

Комбинация должна быть уникальной для всего приложения.

---

## settings

```sql
CREATE TABLE settings (
    key     TEXT PRIMARY KEY,
    value   TEXT NOT NULL
);
```

Минимальные keys:

```text
start_with_windows
show_duration_notification
```

---

# 14. Derived state

State отдельно в БД не хранить.

Он определяется из данных.

```text
session.stopped_at != NULL
    → finished session

open session + open pause
    → PAUSED

open session + no open pause
    → RUNNING

no open session
    → IDLE
```

Это уменьшает вероятность рассинхронизации.

---

# 15. Start operation

Atomic DB transaction:

1. проверить отсутствие open Session;
2. создать Session;
3. commit.

Timestamp берётся непосредственно в момент команды.

После commit UI получает `RUNNING`.

---

# 16. Pause operation

Atomic transaction:

1. получить open Session;
2. убедиться, что open pause отсутствует;
3. создать `session_pauses` с `paused_at_ms`;
4. commit.

После commit состояние `PAUSED`.

После Pause вычисляется accumulated active duration текущей Session.

Если включены notifications — показать duration.

---

# 17. Resume operation

Atomic transaction:

1. найти open Session;
2. найти open pause;
3. записать `resumed_at_ms`;
4. commit.

После commit состояние `RUNNING`.

---

# 18. Stop operation

## Stop from RUNNING

Записать:

```text
sessions.stopped_at_ms = now
```

## Stop from PAUSED

Open pause автоматически закрывается тем же timestamp:

```text
pause.resumed_at_ms = stop_time
session.stopped_at_ms = stop_time
```

Время между Pause и Stop не считается активным.

Все изменения выполняются одной transaction.

---

# 19. Clock semantics

Duration считается по UTC timestamps.

Это защищает расчёты от:

- daylight saving transition;
- отображения локального часового пояса;
- sleep/reboot.

Системные часы Windows считаются источником времени.

Если пользователь вручную изменил часы и получил неправильную историю, запись можно исправить через History editor.

V1 не требует отдельной системы коррекции clock drift.

---

# 20. Timer management UI

Main view содержит список активных Timer.

Для каждого:

```text
Name
State
Start / Pause / Resume / Stop controls
Configured hotkeys
Edit
Remove from list
```

State-dependent controls:

```text
IDLE:
Start enabled

RUNNING:
Pause enabled
Stop enabled

PAUSED:
Resume enabled
Stop enabled
```

Во время `RUNNING` elapsed duration отсутствует.

## Remove from list

Команда называется пользователю примерно:

```text
Remove from list
```

В БД:

```text
is_archived = 1
```

History сохраняется.

Archived Timer:

- не показывается в основном списке;
- его hotkeys не регистрируются;
- остаётся доступным в History/Statistics;
- может быть восстановлен.

Timer с open Session архивировать нельзя.

Сначала нужен Stop.

---

# 21. Global hotkeys

У каждого Timer четыре независимых configurable global hotkey:

```text
Start
Pause
Resume
Stop
```

Пример:

```text
iOS testing

Start   Ctrl+Alt+1
Pause   Ctrl+Alt+2
Resume  Ctrl+Alt+3
Stop    Ctrl+Alt+4
```

Hotkeys работают:

- приложение в фоне;
- окно скрыто;
- другое приложение имеет focus.

Не работают после полного Exit приложения.

После autostart/restart регистрируются снова.

Implementation:

```text
RegisterHotKey
UnregisterHotKey
```

Не вводить keyboard hooks, если `RegisterHotKey` решает задачу.

---

# 22. Hotkey validation

Нельзя назначить одну комбинацию двум действиям.

При назначении:

1. проверить внутренний conflict;
2. попытаться зарегистрировать комбинацию через Windows;
3. если Windows возвращает conflict — комбинацию не активировать;
4. сохранить предыдущую рабочую комбинацию;
5. показать понятное сообщение.

Пример:

```text
Ctrl+Alt+F12 is already used by Windows or another application.
```

Failure одного hotkey не должен блокировать запуск приложения.

Timer остаётся доступен через UI.

---

# 23. System tray

После запуска существует tray icon.

Close main window:

```text
→ hide
```

Tray menu минимум:

```text
Open

Timers >
    iOS testing        Running
    Android testing    Paused
    Documentation      Idle

Settings
Exit
```

Timer submenu может содержать только допустимые для текущего состояния действия.

Например:

```text
iOS testing
    Pause
    Stop
```

Не показывать elapsed time в tray menu.

---

# 24. Notifications

Setting:

```text
Show duration after Pause/Stop
```

Default:

```text
OFF
```

При OFF:

- Pause записывается молча;
- Stop записывается молча.

При ON:

```text
iOS testing paused
1 h 14 min
```

или:

```text
iOS testing stopped
2 h 07 min
```

Notification:

- non-modal;
- не должна steal focus;
- не должна запускать ticking UI.

Конкретная реализация может быть Windows notification либо небольшим app notification; transport не является частью domain contract.

Ошибки приложения могут показываться независимо от этой настройки.

---

# 25. History

History показывает sessions.

Минимальные поля:

```text
Timer
Start
Stop / Running / Paused
Active duration
Paused duration
Pause count
Status
```

Filters:

```text
Timer
Date range
Status
```

History позволяет:

- открыть Session;
- редактировать;
- удалить Session вручную.

Никакого автоматического удаления.

---

# 26. Session editor

Пользователь может менять:

```text
Start
Stop
Pause intervals
Resume timestamps
```

Также можно:

- добавить Pause interval;
- удалить Pause interval;
- удалить Session целиком.

Audit trail не нужен.

После Save сохраняется только новое состояние.

---

# 27. Timeline validation

Нельзя сохранить логически невозможную timeline.

Для stopped Session:

```text
start < stop
```

Каждый pause:

```text
start <= pause
pause < resume
resume <= stop
```

Pause intervals:

- находятся внутри Session;
- не пересекаются;
- идут в хронологическом порядке.

Для open Session:

```text
start <= now
```

Для current PAUSED Session допустим последний:

```text
pause
resume = NULL
```

Future timestamps запрещены.

---

# 28. Same-Timer overlap validation

Sessions одного Timer не могут пересекаться.

Например:

```text
Session A:
10:00–11:00

Session B:
10:45–12:00
```

Save запрещён.

Допустимо:

```text
Session A:
10:00–11:00

Session B:
11:00–12:00
```

Sessions разных Timer могут пересекаться без ограничений.

---

# 29. Validation UX

Не делать generic:

```text
Invalid input
```

Показывать конкретную проблему.

Например:

```text
Resume at 10:40 cannot be earlier than Pause at 10:55.
```

или:

```text
This session overlaps another "iOS testing" session:
14:10–15:25.
```

Save остаётся недоступен до исправления.

---

# 30. Statistics

Основные ranges:

```text
Today
This week
This month
Custom
```

Week starts Monday.

Statistics показывает:

```text
Timer                 Time
--------------------------------
iOS testing          12:40
Android testing       7:25
Documentation         3:10
--------------------------------
Total                23:15
```

Concurrent timers суммируются независимо.

---

# 31. Statistics calculation

Canonical active intervals:

```text
[start, pause1)
[resume1, pause2)
...
[last_resume, stop)
```

Для RUNNING Session временный конец при explicit statistics request:

```text
snapshot_now
```

Для PAUSED Session active time заканчивается последним `pause`.

Range calculation использует **intersection**, а не дату начала Session.

Пример:

```text
Session:
23:00–02:00

Query:
Today starting 00:00
```

В Today попадает только часть после 00:00.

Session, пересекающая midnight, должна корректно разбиваться при aggregation.

---

# 32. Statistics refresh

Statistics не обновляется автоматически.

При открытии:

```text
snapshot_at = now
```

Данные остаются статичными.

Для обновления:

```text
Refresh
```

Это сохраняет главное UX-требование: ничего не тикает перед глазами.

---

# 33. CSV export

CSV обязателен в v1.

Export работает для текущих History filters.

Encoding:

```text
UTF-8 with BOM
```

Это упрощает открытие файла в Excel под Windows.

Delimiter:

```text
,
```

Stable header names — English.

Recommended columns:

```text
timer_name
session_id
status
started_at
stopped_at
active_seconds_total
active_seconds_in_filter
paused_seconds_total
pause_count
exported_at
```

Timestamps экспортировать в ISO 8601 с timezone offset.

Пример:

```text
2026-10-03T14:25:10+03:00
```

Для RUNNING Session:

```text
status = running
stopped_at = empty
```

Duration рассчитывается как snapshot на `exported_at`.

Для PAUSED Session:

```text
status = paused
```

Текущий paused interval не увеличивает active duration.

---

# 34. CSV safety

Export:

- не изменяет SQLite;
- не меняет состояния Timer;
- не закрывает Session;
- не требует network;
- пишет только в выбранный пользователем файл.

Если файл нельзя записать, показать ошибку и оставить данные без изменений.

---

# 35. Main window structure

Минимальный UI:

```text
┌────────────────────────────────────┐
│ Timers   History   Statistics      │
│                              ⚙     │
├────────────────────────────────────┤
│ iOS testing              Running   │
│ [Pause] [Stop]                     │
│                                    │
│ Android testing          Paused    │
│ 01:12:45                           │
│ [Resume] [Stop]                    │
│                                    │
│ Documentation            Idle      │
│ [Start]                            │
│                                    │
│ [+ Add timer]                      │
└────────────────────────────────────┘
```

Во время Running duration отсутствует.

Paused duration статична.

После Stop допустимо показать итог последней Session как статичное значение.

---

# 36. Settings

Минимум:

```text
[✓] Start with Windows
[ ] Show duration notification after Pause/Stop
```

Timer hotkeys настраиваются в Edit Timer, а не в global Settings.

Не добавлять настройки ради будущего расширения.

---

# 37. Single instance

Одновременно должен работать один GaleWorkTimer process на пользователя.

Второй launch:

- не открывает вторую SQLite writer instance;
- не регистрирует второй набор hotkeys.

Минимально допустимое поведение:

```text
Application is already running in the system tray.
```

и exit второго process.

IPC для активации первой instance не требуется для v1.

---

# 38. Persistence guarantees

Каждое изменение Timer state commit-ится сразу.

Не держать единственный экземпляр текущего state только в RAM.

После successful:

```text
Start
Pause
Resume
Stop
Edit
Delete
```

SQLite уже является источником истины.

Process crash после commit не должен терять operation.

Каждая state-changing operation выполняется SQLite transaction.

---

# 39. Failure behaviour

При DB write error:

- UI не должен показывать новое состояние как успешно сохранённое;
- вернуть предыдущий state;
- показать ошибку.

Например:

```text
Could not pause "iOS testing".
The session was not changed.
```

Нельзя сначала обновить UI, а потом надеяться, что DB write сработает.

---

# 40. Data deletion

Никаких:

- cleanup jobs;
- expiration;
- automatic retention policies.

Timer removal:

```text
archive
```

Session deletion:

```text
manual explicit action
```

Session delete требует confirmation.

Например:

```text
Delete this session permanently?
```

Поскольку приложение не является audit system, soft-delete Session не требуется.

---

# 41. Privacy and networking

Application operates offline.

V1 запрещает:

- telemetry;
- analytics upload;
- cloud storage;
- accounts;
- login;
- sync;
- remote API;
- VPS dependency;
- automatic update service.

VPS NeFirma не является частью runtime architecture.

Он может использоваться отдельно как engineering/build fallback, но установленное приложение Гали от VPS не зависит.

---

# 42. Packaging

Target artifact:

```text
win-x64
self-contained
```

На ПК Гали не требуется отдельно устанавливать .NET runtime.

Для первого рабочего цикла допустим:

```text
GaleWorkTimer-win-x64.zip
```

с self-contained publish directory.

Предпочтительный постоянный install location:

```text
%LOCALAPPDATA%\Programs\GaleWorkTimer\
```

Это:

- не требует administrator rights;
- даёт стабильный путь для autostart.

Полноценный installer можно добавить после рабочего v1, если он реально понадобится.

Не блокировать первую рабочую версию из-за installer tooling.

---

# 43. Linux development behaviour

На CachyOS application должна запускаться как обычное Avalonia приложение.

Windows-only integrations должны быть изолированы за небольшими interfaces:

```text
IHotkeyService
IAutostartService
INotificationService
```

Linux implementations могут быть:

```text
Hotkeys: unsupported/no-op
Autostart: unsupported/no-op
Notifications: simple in-app implementation
```

Domain/UI/DB при этом полностью работают.

Нельзя размазывать:

```text
if (OperatingSystem.IsWindows())
```

по всему приложению.

Windows-specific код должен находиться внутри `Platform/Windows`.

---

# 44. Suggested internal services

Не обязательные классы по имени, а ownership boundaries:

```text
TimerService
    Start
    Pause
    Resume
    Stop

SessionEditor
    Load
    Validate
    Save
    Delete

StatisticsService
    BuildSnapshot

CsvExporter
    Export

HotkeyService
    Register
    Unregister

AutostartService
    Enable
    Disable
    IsEnabled
```

Не создавать generic repository/service abstractions сверх этого.

---

# 45. Tests — domain

Обязательные unit tests:

```text
Start: Idle -> Running
Pause: Running -> Paused
Resume: Paused -> Running
Stop from Running
Stop from Paused

double Start rejected
double Pause rejected
invalid Resume rejected
Stop Idle rejected
```

---

# 46. Tests — duration

Проверить:

```text
simple Start/Stop
one Pause
multiple Pause/Resume
Stop while Paused
session crossing midnight
session crossing DST boundary
running snapshot
paused snapshot
```

---

# 47. Tests — simultaneous timers

Пример:

```text
Timer A 10:00–11:00
Timer B 10:30–11:30
```

Expected:

```text
A = 60 min
B = 60 min
Total = 120 min
```

Не 90 min.

---

# 48. Tests — editing validation

Проверить rejection:

```text
start > stop
pause < start
resume < pause
resume > stop
overlapping pauses
future timestamp
same-timer overlapping sessions
```

Проверить acceptance:

```text
different timers overlap
adjacent sessions same timer
multiple valid pauses
```

---

# 49. Tests — persistence

Integration tests against temporary SQLite DB:

```text
Start
dispose application services
reopen DB
state == Running
```

То же:

```text
Pause
restart
state == Paused
```

И:

```text
multiple running timers
restart
all states restored
```

---

# 50. Windows manual acceptance

На реальном Windows 11 проверить:

### Tray

- launch;
- close window;
- process остаётся;
- tray icon работает;
- Open возвращает окно.

### Global hotkeys

- работают при focus в другом приложении;
- работают при скрытом окне;
- конфликт комбинаций определяется;
- после app restart комбинации восстанавливаются.

### Autostart

- включить;
- reboot;
- приложение появляется в tray без main window.

### Running across reboot

```text
Start
reboot
wait
Stop
```

Время включает reboot.

### Paused across reboot

```text
Start
Pause
reboot
Resume
Stop
```

Reboot time не учитывается.

### Sleep

RUNNING через sleep включает sleep duration.

### Process kill

```text
Start
kill process
restart
Stop
```

Session остаётся непрерывной.

---

# 51. Acceptance Criteria

## AC-01 Multiple timers

Можно создать несколько Timer и одновременно перевести несколько из них в RUNNING.

## AC-02 Independent state

Start/Pause/Resume/Stop одного Timer не меняет состояние остальных.

## AC-03 Hidden elapsed time

RUNNING Timer нигде автоматически не показывает изменяющийся elapsed time.

## AC-04 Pause

Pause прекращает накопление active time, но не завершает Session.

## AC-05 Resume

Resume продолжает ту же Session.

## AC-06 Stop

Stop окончательно закрывает Session.

## AC-07 Persistence

State восстанавливается после normal exit, crash или Windows reboot.

## AC-08 Wall-clock continuation

RUNNING Session продолжает учитывать время при shutdown/reboot/sleep.

## AC-09 Paused persistence

PAUSED Session не накапливает active time во время shutdown/reboot/sleep.

## AC-10 Tray

Main window можно скрыть, приложение остаётся управляемым через tray/hotkeys.

## AC-11 Hotkeys

Каждый Timer имеет собственные configurable Start/Pause/Resume/Stop global hotkeys.

## AC-12 Hotkey collision

Нельзя создать конфликтующую combination внутри приложения; OS conflict обрабатывается без потери предыдущего binding.

## AC-13 Autostart

Application может автоматически запускаться после Windows login без administrator rights.

## AC-14 History

Все Sessions сохраняются в SQLite без automatic expiration.

## AC-15 Editing

Пользователь может редактировать Start/Stop/Pause/Resume timeline.

## AC-16 Timeline integrity

Невалидная chronology не сохраняется.

## AC-17 Same timer consistency

Sessions одного Timer не могут пересекаться.

## AC-18 Cross-timer overlap

Sessions разных Timer могут пересекаться.

## AC-19 Statistics

Есть totals минимум за Today / Week / Month / Custom range.

## AC-20 Additive totals

Concurrent Timer durations суммируются.

## AC-21 No live statistics

Statistics является snapshot и не обновляется каждую секунду.

## AC-22 CSV

History можно экспортировать в UTF-8 CSV.

## AC-23 Archive

Timer можно вручную убрать из рабочего списка без удаления History.

## AC-24 No network dependency

Все основные функции работают при полностью отключённом интернете.

---

# 52. Explicit non-goals for v1

Не реализовывать:

```text
AFK detection
mouse/keyboard activity monitoring
active-window tracking
automatic project detection
mobile app
mobile synchronization
multi-user support
cloud sync
web dashboard
VPS backend
accounts/authentication
billing
screenshots
screen-recording detection
browser extension
Jira integration
Git integration
calendar integration
Pomodoro
tasks/TODO management
automatic reports
audit history
manager/admin mode
```

---

# 53. Implementation order

Рекомендуемый порядок, чтобы быстро получить реальную рабочую пользу:

### Phase 1 — Core

```text
SQLite schema
Timer CRUD/archive
Start/Pause/Resume/Stop
duration calculation
tests
```

### Phase 2 — Basic UI

```text
Timer list
controls
no ticking duration
History
```

После этого приложение уже можно дать Гале для первого реального использования.

### Phase 3 — Windows workflow

```text
tray
global hotkeys
autostart
restore after reboot
```

### Phase 4 — Data management

```text
history editor
timeline validation
delete session
```

### Phase 5 — Harvest

```text
Statistics
CSV
Windows package
real use by Gala
```

После Phase 5 не заниматься polish ради polish.

Сначала получить несколько реальных рабочих дней Гали и собрать конкретные замечания.

---

# 54. Definition of Done for v1

v1 считается готовой, когда на Windows 11 Галя может:

1. создать свои Timer;
2. назначить каждому hotkeys;
3. запустить несколько одновременно;
4. скрыть приложение в tray;
5. работать без видимого ticking counter;
6. Pause/Resume/Stop через hotkeys;
7. перезагрузить Windows во время работы и не потерять Session;
8. посмотреть историю;
9. исправить ошибочную Session;
10. получить статистику;
11. экспортировать CSV;
12. пользоваться приложением несколько рабочих дней без ручного исправления SQLite или вмешательства разработчика.

Последний пункт — главный acceptance test продукта.

Работающий build сам по себе не считается завершением задачи.
