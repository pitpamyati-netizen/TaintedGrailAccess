# Tainted Grail Access

Проект мода доступности **Tainted Grail: Conquest**, Steam App ID 1199030.
Основная платформа: Windows x64, клавиатура, NVDA. JAWS предусмотрен через
отдельно подключаемый Tolk. Русские сообщения мода; игровые тексты берутся
из текущей локализации самой игры.

## Текущий результат

Это **проект исходников для разработки с Codex**, а не готовый мод для прохождения.
В нём есть работающее независимое ядро Graph A11y, навигатор, менеджер экранов,
адаптеры речи, диагностический плагин BepInEx и поведенческие тесты.
Подключение игровых экранов, перехват Rewired/EventSystem, бой и перемещение
требуют изучения локальной сборки игры. Совместимая версия игры ещё не зафиксирована.

Приложенное дополнение сохранено без изменений:
[graph-a11y-spec.original.md](docs/spec/graph-a11y-spec.original.md).
Его требования разложены по коду, проверкам и оставшимся работам в
[SPEC_CONFORMANCE.md](docs/SPEC_CONFORMANCE.md).

## С чего начать

1. Распакуйте проект в отдельную папку, например `C:\Mods\TaintedGrailAccess`.
2. Откройте эту папку в Codex.
3. Укажите задачу и путь к установленной **Conquest**. Правила работы —
   в [AGENTS.md](AGENTS.md), текущий результат — в [PROJECT_STATUS.md](docs/PROJECT_STATUS.md).
4. Codex сначала выполняет проверку файлов и тесты, затем подключает один
   реальный экран. Дальнейшие этапы описаны в [ROADMAP.md](ROADMAP.md).

Не копируйте весь архив в папку игры. Для разработки не нужен Unity Editor.

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

Нужен .NET 8 SDK. Обычная проверка:

```powershell
.\scripts\test.ps1
```

Тесты не требуют игровых DLL. Первый обычный build может восстановить
официальные reference packages .NET через NuGet.
Альтернативная проверка без NuGet описана в [BUILD_AND_INSTALL.md](docs/BUILD_AND_INSTALL.md).

Диагностический плагин собирается **после подтверждения Mono** и отдельной
установки совместимого BepInEx 5. Он сообщает о запуске, проверяет речевой
вывод и при явном включении экспортирует сигнатуры игровых сборок.
Он пока не озвучивает меню игры и не управляет персонажем.

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
