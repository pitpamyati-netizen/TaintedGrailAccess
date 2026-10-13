# Tainted Grail Access

Проект мода доступности **Tainted Grail: Conquest**, Steam App ID 1199030.
Основная платформа: Windows x64, клавиатура, NVDA. JAWS предусмотрен через
отдельно подключаемый Tolk. Сообщения мода — русские; игровой текст — из локализации игры.

## Текущий результат

M1/M2 и первый бой пройдены с NVDA; быстрые сведения и фокус руки подтверждены.
Установлена 0.4.2: исправлены чтение имён и мешавшая Enter подсказка.
Проверки и сборка прошли; повторная проверка с NVDA и полный M3 ещё не приняты.
На GOG 1.61 / Unity 2020.3.23f1 / Mono / Windows x64 с NVDA приняты меню,
настройки, речевое редактирование, удержание, Alt+Tab, rebind/cancel и возврат.
Игрок создал героя и прошёл обучение до боя; смена разрешения не проверялась.
Точные результаты — в [M2_VERIFICATION.md](docs/M2_VERIFICATION.md),
проверка ввода M1 — в [M1_VERIFICATION.md](docs/M1_VERIFICATION.md).
Бой: Tab меняет раздел, стрелки выбирают карту/цель, Enter действует,
Space/F1 читает описание, Backspace открывает просмотр, Escape отменяет цель
или закрывает просмотр. Выбор N карт подтверждается отдельной кнопкой.
Сценарий и границы — [M3_VERIFICATION.md](docs/M3_VERIFICATION.md).
M4: Tab открывает обзор, Space оценивает путь, Enter поручает подход игре,
Escape останавливает. Направления — относительно камеры. Сценарий и приёмка —
[M4_VERIFICATION.md](docs/M4_VERIFICATION.md).
Сохранение карты между ходами требует проверки.
Неизвестная сборка сохраняет обычное управление и диагностический режим.

Приложенное дополнение сохранено без изменений:
[graph-a11y-spec.original.md](docs/spec/graph-a11y-spec.original.md).
Его требования разложены по коду, проверкам и оставшимся работам в
[SPEC_CONFORMANCE.md](docs/SPEC_CONFORMANCE.md).

## С чего начать

1. Распакуйте проект в отдельную папку и откройте её в Codex.
2. Укажите задачу и папку установленной **Conquest**.
3. Сначала прочитайте [AGENTS.md](AGENTS.md),
   [PROJECT_STATUS.md](docs/PROJECT_STATUS.md) и [ROADMAP.md](ROADMAP.md).


## Проверка файлов игры

Нужен Python 3.9 или новее. Из папки проекта в PowerShell:

```powershell
py -3 .\scripts\inspect_game.py --game-dir "D:\SteamLibrary\steamapps\common\Tainted Grail"
```

Путь — пример; укажите свою папку. Инструмент не запускает и не изменяет игру.
Он создаёт `artifacts/discovery/game-inventory.json`, выводит пять строк и
различает свидетельства Mono, IL2CPP и неоднозначную установку.
Окончательная проверка runtime выполняется по журналу запущенной игры.

## Сборка и тесты

Нужен .NET SDK 8 или новее и runtime .NET 8 для тестов. Обычная проверка:

```powershell
.\scripts\test.ps1
```

Тесты не требуют игровых DLL. Первый обычный build может восстановить
официальные reference packages .NET через NuGet.
Альтернативная проверка без NuGet описана в [BUILD_AND_INSTALL.md](docs/BUILD_AND_INSTALL.md).

Плагин требует **Mono** и совместимый BepInEx 5. Он читает стартовую подсказку;
`Accessibility.EnableTitleInput=true` включает меню, обучение, карточный бой и исследование
только при совпадении исследованных отпечатков. По умолчанию флаг выключен.
Установка обратима, игровые DLL в пакет не входят.
Подробнее — в [BUILD_AND_INSTALL.md](docs/BUILD_AND_INSTALL.md).

## Основные материалы

- [AGENTS.md](AGENTS.md): обязательные правила работы Codex.
- [ARCHITECTURE.md](docs/ARCHITECTURE.md): слои, идентичность, речь и ввод.
- [GAMEPLAY_COVERAGE.md](docs/GAMEPLAY_COVERAGE.md): требования ко всему прохождению.
- [GAME_DISCOVERY.md](docs/GAME_DISCOVERY.md): поиск настоящих игровых API.
- [KEYBOARD_AND_SPEECH.md](docs/KEYBOARD_AND_SPEECH.md): управление и озвучивание.
- [ACCEPTANCE_TESTS.md](docs/ACCEPTANCE_TESTS.md): проверка со слепым игроком.
- [TEST_RESULTS.md](docs/TEST_RESULTS.md): фактически выполненные проверки и ограничения.
- [DEVELOPMENT_GUIDE.md](docs/DEVELOPMENT_GUIDE.md): порядок работы и записи результатов.
- [GAME_API.md](docs/GAME_API.md): журнал подтверждённых игровых привязок и клавиш.
- [MENU_ACCESSIBILITY.md](docs/MENU_ACCESSIBILITY.md): проверка каждого нового экрана.
- [LOCALIZATION.md](docs/LOCALIZATION.md): подписи, перевод и формат сообщений.
- [DIAGNOSTICS.md](docs/DIAGNOSTICS.md): поиск причин ошибок и проверка речи.
- [RELEASE_GUIDE.md](docs/RELEASE_GUIDE.md): состав будущего пользовательского выпуска.
- [GIT_AND_GITHUB.md](docs/GIT_AND_GITHUB.md): работа с историей и GitHub.
- [CONTRIBUTING.md](CONTRIBUTING.md): как предложить изменение или сообщить об ошибке.
- [Разбор добавленных инструкций](docs/research/template-import.md).
- [Исследование других модов](docs/research/reference-mods.md).

Все новые исходники — самостоятельная реализация. Игровые DLL, декомпилированная
игра, загрузчик и нативные библиотеки дикторов в архив не включены.

Исходники проекта — MIT; адаптированные инструкции из Accessibility Mod Template
сохраняют атрибуцию CC BY 4.0. Подробности в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
