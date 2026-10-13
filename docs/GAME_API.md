# Журнал подтверждённых игровых привязок

Структура адаптирована из game-api.md.template и setup-guide;
[источник и условия](research/template-import.md).
Метод исследования — [GAME_DISCOVERY.md](GAME_DISCOVERY.md).

## M1: текущий статус 0.1.3, 13 октября 2026

Активный host теперь подключён при явном EnableTitleInput; точные fingerprints
приведены ниже. Исторические записи 0.1.1/0.1.2 сохраняют состояние на свои даты.
Меню/переключатели/возврат, повтор и Alt+Tab, справка, rebind/cancel проверены
в игре с NVDA. Согласие и неиспользованные меню handlers статически найдены,
но не заявляются как завершённый пользовательский путь M2.

`TitleScreenUI.ShowMainView/SpawnStartupPopup` вызывают World.SpawnView с
isMainView=false: VTitleScreenRogueUI находится в живой Model.Views. Регрессия
LiveViewSelection защищает скрытые/пересозданные и non-main views.
Вложенный original handler входит в host.Poll в том же frame: return расположен
перед finally, чтобы не очистить World внешнего callback и не потерять cursor.

Rewired: GameUI.UpdateReInput → DeliverReInputEvent(InputAction, UIAction).
UIAction.CreateAxis создаёт UINaviAction из Horizontal/Vertical независимо от
текущего action loop; direction определяет действительный источник. Читаются
enabled maps/AllMaps и ActionElementMap.actionId/keyCode/modifierKey1/2/3.
Карты не меняются; независимый неприсвоенный contributor не подавляется.

Unity UI: Button.OnSubmit(BaseEventData), Toggle.OnSubmit(BaseEventData),
Selectable.OnMove(AxisEventData), EventSystem.Update(), StandaloneInputModule.Process()
и SendUpdateEventToSelectedObject(). GameUI.KeyUp остаётся штатной очисткой.
Исключение после действия не освобождает использованное нажатие.

Текст: VButtonOption._option/button/displayName, ButtonOption исходный callback
открывает UserBugReporting. VUserBugReporting.title/description — TMP_InputField.
Исходные EventSystem.SetSelectedGameObject → TMP_InputField.OnSelect(BaseEventData)
и OnPointerClick(PointerEventData) дают редактору ввод; это только native edit
ownership. TMP_InputField.LateUpdate активирует отложенный editor, OnUpdateSelected
читает Event.PopEvent, protected EditState KeyPressed(Event) выполняет правку.
InputField имеет такой же исходный путь KeyPressed(Event). Scoped guard пропускает
свежие клавиши, но не позволяет открывающему Enter завершить editor повторно.
Native focused fields/raw capture останавливают graph navigation/search.
Диагностика записывает только focused/selected/readOnly/длину, без текста/символов.
UserBugReporting.Close() вызывается исходной closeButton; отправка не предоставлена.

ShortcutsLayerStack.TopLayer.Model проверяет живого владельца modal. Неизвестное
игровое окно вызывает stand-down. Только собственная справка Ctrl+F1 эксклюзивна.
Уничтожение TitleScreen/выключение освобождает клавиши, cursor и screen state.
Окончательный запуск подтвердил native focused/selected и набор/удаление по
длине поля без записи символов. Пользователь подтвердил отсутствие чтения
пунктов и закрытие Escape. Озвучивание букв пока отсутствует и остаётся M2.
Результаты — [M1_VERIFICATION.md](M1_VERIFICATION.md).

## Текущая граница знаний

Целевая игра: **Tainted Grail: Conquest**, App ID 1199030, Windows x64.
Локальная установка исследована 12 октября 2026: русская GOG-копия Conquest,
build ID `59087960036481007`. Внешние свидетельства TG.Main.dll,
Assembly-CSharp.dll, Rewired, TMP/I2/A* записаны в research/game-evidence.md;
они не подтверждают ни одного конкретного UI handler.
`profiles/unbound.example.json` остаётся неподключённым примером.

Ниже отделены найденные в локальном коде сигнатуры от проверенных игровых действий.
Не заменять «не установлено» на «проверено» по одному наличию метода.
Пути к локальной установке, полные дампы и декомпиляция хранятся вне публикации.

## Профиль исследованной сборки

Для каждого настоящего профиля записать дату исследования, магазин/build ID,
Application.version/unityVersion, runtime, PE architecture, loader/bridge versions,
имена игровых сборок с SHA-256/MVID и ссылку на очищенное доказательство загрузки.
Первый журнал загрузки подтвердил `Application.productName=Tainted Grail`,
`Application.version=1.61`, `Application.unityVersion=2020.3.23f1`, процесс 64 бит,
Mono/CLR `4.0.30319.42000`, BepInEx `5.4.23.5`.
GOG-манифест называет продукт `Tainted Grail: Conquest`; Steam-манифеста нет.
Плагин `0.1.0` загружен. NVDA Controller Client 2026.2 x64 получен с
[официального сервера NV Access](https://download.nvaccess.org/releases/stable/).
API принял стартовую фразу; пользователь подтвердил её слышимость через NVDA.
Очередь, прерывание, повтор клавиш и игровые действия пока не проверены.

Отпечатки локальных игровых сборок:

- `TG.Main.dll`: SHA-256 `26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`;
  MVID `78793877-83a2-4bfc-970d-974256f05d98`.
- `Assembly-CSharp.dll`: SHA-256 `3c0bdc4a48303e0444694d943e66e5c5f23672c7bd04112e3c3652a6a9e7944a`;
  MVID `03862023-0960-4019-8824-7ae891a3ff55`.

Основания: read-only PE/CLI inventory, GOG info, журнал BepInEx и плагина,
метаданные Mono.Cecil и выборочная декомпиляция ILSpy 11.1.
Полные материалы остаются локально в `artifacts/discovery`; пути игрока не публикуются.

## Стартовый экран: найдено в локальном коде

Все перечисленные ниже члены находятся в `TG.Main.dll` с отпечатком выше.

- `Awaken.TG.Main.UI.TitleScreen.TitleScreen`: `World World { get; }`,
  private `bool _initialized`, private `void LateUpdate()` и `void OnGUI()`.
  `LateUpdate` работает с живым World после инициализации; `OnGUI` вызывает
  `World.Only<GameUI>().PerformGUI()`. Это подтверждает место наблюдения,
  но не подтверждает ранний порядок перехвата клавиш.
- `Awaken.TG.MVC.World.Any<T>() where T : class` читает текущий реестр моделей;
  `bool Discarded { get; }` показывает завершённый World.
- `Awaken.TG.MVC.Model`: `IsFullyInitialized`, `WasDiscarded`, `IsBeingDiscarded`
  и `View MainView { get; }`; `Awaken.TG.MVC.View.Discard()` удаляет view из
  реестра до уничтожения GameObject.
- `Awaken.TG.Main.UI.TitleScreen.PressStartUI` создаётся в
  `TitleScreenUI.CreateFocusPopup()`. Метод ждёт `WasDiscarded` перед следующим окном.
- `Awaken.TG.Main.UI.TitleScreen.VPressStartUI`: public
  `TMPro.TextMeshProUGUI text`, private `DG.Tweening.Tween _animTween`,
  `Awaken.TG.MVC.UI.UIResult Handle(Awaken.TG.MVC.UI.Events.UIEvent evt)`.
  `OnInitialize()` регистрирует собственный keyboard handler и запускает анимацию.
  `Handle` принимает UIKeyDownAction/UIEKeyDown/UIEMouseDown, обнуляет `_animTween`
  и запускает `Hide()`. Поэтому готовность подсказки требует живого view,
  активного text/Canvas и ненулевого `_animTween`. Блокирующий PopupUI
  должен исключить фоновое чтение. Сам обработчик мод пока не вызывает.

Первый адаптер ограничен пассивным чтением этой видимой подсказки.
Игровая обработка «любой кнопки» сохраняется; управление меню не включается
до проверки владения вводом.

## Главное меню и ввод: найдено, действие ещё не проверено

- `VTitleScreenRogueUI.OnInitialize()` подписывает `options.OnClick` на
  `World.Add(new Awaken.TG.Main.Settings.Windows.AllSettingsUI())`;
  `exitGame.OnClick` — на `TitleScreenUI.ExitGame()`. Поля `startGame`,
  `continueGame`, `switchSaveSlot`, `journal`, `options`, `leaderboards`,
  `exitGame` имеют тип `Awaken.TG.Main.UI.Components.ARButton`.
  `UpdateView()` задаёт локализованный текст стартовой кнопки и прячет Continue
  по `CanContinue`/постоянному прогрессу. Нельзя читать её как всегда доступную.
- `ARButton.Handle(UIEvent)` проверяет `Interactable`; `UISubmitAction`
  вызывает OnPress/OnRelease/OnClick. MouseUp вызывает OnClick только при Hovering.
  Этот метод — кандидат оригинального действия и защиты от двойного Submit.
- `GameUI.PerformGUI()` обрабатывает Unity Event и вызывает `UpdateReInput()`
  один раз за frame. `HandleKeyDown(UnityEngine.Event)` создаёт `UIEKeyDown`.
  `Focus.Select(Component, bool)` меняет EventSystem selection только для gamepad.
  Native focus остаётся диагностическим фактом, не основой интерфейса мода.

Имена Rewired action, переназначения, свободные сочетания, exclusive/raw/text
и счётчик одного реального действия ещё не проверены. Все action bindings выключены.

## Продолжение M1: обработчики стартового пути, 0.1.2

Повторно проверены SHA-256/MVID TG.Main и точные signatures через Mono.Cecil.
Исследованы меню, настройки/закрытие/подтверждение, TitlePopupUI согласия,
оригинальная доставка Accept/Cancel и raw capture VNewKeyBinding.
Для каждого элемента записаны original caller, источник подписи/состояния,
ограничения видимости и выполненный уровень проверки:
[полная запись](research/title-input-bindings.md).
Особенно: VToggle.Handle не переключает настройку; TitlePopupUI не является PopupUI;
VNewKeyBinding — view в Model.Views, а не модель в World.Any.

Host 0.1.2 содержит отключённое по умолчанию ограниченное наблюдение за вводом.
Это способ получить доказательство порядка и дублей, а не подавление клавиш.
Новые hooks собраны, но ещё не испытаны внутри игры. Action bindings и все
разрешения inputEvidence локального профиля остаются false.

## Ввод игры и доступные сочетания

Каждая найденная запись содержит: клавиша/модификаторы, действие, контекст,
original input/action handler, файл/метод источника, пользовательское
переназначение, порядок обработки, вывод о конфликте с NVDA/Windows и сценарий.

- Игровой poller и Rewired/global keymap: найден GameUI.PerformGUI/UpdateReInput;
  точные action, порядок раннего опроса и подавление **не проверены**.
- Native EventSystem/Submit: найдены Focus.Select и ARButton.Handle;
  ownership и отсутствие дубля **не проверены**.
- Raw capture: найден VNewKeyBinding в Model.Views и оригинальный Handle;
  наблюдение собрано, stand-down внутри игры **не проверен**. Реальные текстовые
  поля ещё не исследованы.
- Проверенные свободные сочетания мода: **нет записей**.
- Проверенные per-chord/ownership hooks: **нет записей**.

Клавиши переносимого Navigator в KEYBOARD_AND_SPEECH описывают модель, а не
подтверждают безопасное использование этих сочетаний в Conquest.

## Экраны, текст и жизненный цикл

Для каждого экрана фиксировать точные UI/domain types и владельца, вход/выход,
readiness/loading predicates, parent/layer/modal, логику видимости/доступности,
source локализованных label/value/tooltip, признаки text entry/raw capture,
идентичность экземпляров и оригинальные handlers. Для закрытого поля записать
объявляющий тип, имя, тип данных и способ доступа. Для PressStart данные записаны
выше; остальные экраны ещё не подключены.

Не считать факт sceneLoaded достаточным и не связывать будущий UX с native focus.
Полезные игровые hooks отдельно описывают наблюдение результата; они не заменяют
poll-and-diff экранов.

## Карточка одного binding

Пока это форма для заполнения, не рабочая привязка:

- Идентификатор контракта самого мода:
- Профиль, имя assembly, SHA-256, MVID:
- Полный declaring type, member, точная signature/перегрузка:
- Как найдено; original UI handler и его caller:
- Когда безопасно читать и вызывать; readiness/input locks:
- Визуальная видимость и причина недоступности:
- Источник локализованного текста и идентичность экземпляра:
- Способ original invoke и ownership scope/guard:
- Сценарий: клавиша, одно действие, изменившееся состояние, отмена/возврат:
- Результат portable/compiled-with-game/in-game/NVDA/user acceptance:
- Известное ограничение, дата и основание повторной проверки:

## Изучение по мере подключения

- Старт, меню, настройки, подтверждение: M1–M2.
- Обучение, классы, карты/цели/эффекты/ход и события боя: M2–M3.
- Видимость, движение, pathfinder, отмена и процедурная карта: M4.
- События, диалоги, торговля, награды, деревня/метапрогрессия: M5.
- Локализация, помощь, persisted settings, звук, сохранение/загрузка: M2–M6.

Для звуков подтверждать original themed sound и отсутствие дубля. Для сохранения
не публиковать файлы игрока и не вызывать Save/Load в качестве побочного эффекта
чтения. Обучающие тексты изучать только для нужного текущего пути: не выгружать
все будущие диалоги/награды и не публиковать извлечённые базы локализации.
Ненайденный контракт остаётся ограничением в ROADMAP/coverage.

## История

Исправление по игровой проверке 13 октября: стартовая подсказка закрылась,
но пользователь не слышал меню. `TitleScreenUI.SpawnStartupPopup()` и
`ShowMainView()` вызывают `World.SpawnView(...VTitleScreenRogueUI...)` с
`isMainView=false` по умолчанию. Следовательно, прежний вывод о меню в MainView
был неверным. Меню находится по точному типу в живой `Model.Views`, как
`World.View<T>(Model)` самой игры. Добавлена регрессия для overlay, скрытого
и заново созданного меню. Проверка слышимости исправления ещё предстоит.

### Новые статические доказательства M1, 13 октября 2026

Повторно прочитаны локальные inventory и журнал загрузки. TG.Main совпадает
с SHA-256/MVID выше. Выборочное чтение ILSpy 11.1 подтвердило:

- `TitleScreen.Start/OnGUI/LateUpdate/OnDestroy`, `World` и `_initialized`:
  контроллер сцены допускает слабую ссылку только для получения живого World;
  view, списки и обработчики между кадрами не сохраняются.
- `GameUI.PerformGUI()` сначала вызывает `UpdateReInput()`, затем
  `HandleKeyDown(Event)`/`HandleKeyUp(Event)`. Собственный ранний опрос нужен также
  перед Unity EventSystem, который выполняется независимо от OnGUI.
- `Focus.BeforeDelivery(UIEvent)` принимает `UISubmitAction` и вызывает
  `HandleSubmitButton()`: обычный Button получает onClick, Toggle меняется
  самостоятельно. Поэтому одного перехвата ARButton недостаточно.
- `UnityEngine.UI.Toggle.OnSubmit(BaseEventData)` вызывает штатный
  `InternalToggle()`, который проверяет IsActive/IsInteractable и запускает
  исходный listener VToggle. Мод не записывает isOn или ToggleOption.Enabled.
- `StandaloneInputModule.Process()` вызывает update-selected, move и submit/cancel;
  `Button/Toggle.OnSubmit`, `Selectable.OnMove` — отдельные пути Unity UI.
- `VSettingsUI._tabs: SettingsTabButton[]`, `_spawnedSettings` в AllSettingsUI:
  порядок задаёт игра. `TabButtonBase.button`, `selectedText`, `IsActive` и
  `Select()` подтверждены; Select вызывает исходный button.onClick.
- `VUserBugReporting.title/description: TMPro.TMP_InputField`: реальные поля.
  Текст и символы пользователя не записываются в диагностику.
- Rewired `ActionElementMap.actionId/keyCode/modifierKey1/2/3/controllerMap/enabled`:
  фактические назначения и enabled читаются заново; карты не изменяются.

Дополнительные сборки для этих привязок, SHA-256:

- Rewired_Core: `cf3c91bed960e0b45e67dc67fb6df6eca7d05a37b803c788bbf2c151d1bde842`.
- UnityEngine.UI: `69aea3e824546d94063dd22ee998125847d65171ab4a210b3f08ef392aabfc97`.
- Unity.TextMeshPro: `b892fcec2b444d6c2623ba456319e71697e9387bd04bee993843f5e26c8f9db9`.

MVID соответственно: `d3f4044c-7648-45c9-b50d-8d7e6d871796`,
`d8562c56-a489-4d6f-bb2b-224105bd17d4`, `5ec98d0f-fd50-4ec7-ac26-58db85bcd6ab`.
Все поля TitleUiBindings разрешены отдельной проверкой с настоящими сборками,
без запуска Unity и без игровых действий. `GameUI.HandleKeyUp(Event)` только
удаляет запись `_heldKeys`, поэтому остаётся игре, включая присвоенную модом
клавишу. Иначе клавиша, первоначально нажатая до появления дерева, могла бы
навсегда остаться в игровом словаре удержания.

Первоначальная запись подтверждала исходный код. Физическая проверка 0.1.3
и слышимость перечислены в текущем статусе выше. Для действия остаются обязательны живой активный владелец,
видимость всей цепочки Canvas/CanvasGroup, Interactable и ownership scope.

- 2026-10-12: добавлена форма журнала; игровые данные не подставлялись.
- 2026-10-12: подтверждены GOG 1.61, Mono/x64, Unity и loader; плагин 0.1.1
  прочитал живые `Стартовый экран` и `Press any button` по одному разу.
  Пользователь подтвердил обе фразы через NVDA. Повторный ввод и переходы меню
  не принимаются как проверенные этим сценарием.
- 2026-10-12: статически исследован стартовый путь и добавлены tracker/наблюдение
  ввода 0.1.2. Проверены 11 новых переносимых сценариев и сборка; новая игровая
  проверка и принятие NVDA не выполнялись.
