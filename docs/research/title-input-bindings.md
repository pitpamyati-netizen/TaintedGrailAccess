# Стартовый путь и обработка ввода: исследование 12 октября 2026

Эта запись сохраняет исследование 0.1.2 на 12 октября. Актуальные привязки 0.1.3
и игровая проверка 13 октября описаны в [GAME_API.md](../GAME_API.md) и
[M1_VERIFICATION.md](../M1_VERIFICATION.md). Уточнение: VTitleScreenRogueUI
создаётся с isMainView=false и ищется в живой Model.Views; MainView не является
источником этого меню. Native text ownership устанавливает selection только
редактору, затем вызывает исходные обработчики, сохраняя независимый cursor.

Это результат чтения локального кода и сборки, а не проверки действий в игре.
В 0.1.2 добавлено наблюдение, которое позволит получить настоящий порядок вызовов.
Управление меню модом ещё не включено. Настройки и согласие не менялись.

## Сборка и способ исследования

GOG Conquest 1.61, Unity 2020.3.23f1, Mono/x64. Все игровые члены ниже
принадлежат `TG.Main.dll`, SHA-256
`26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`,
MVID `78793877-83a2-4bfc-970d-974256f05d98`.
Повторное чтение SHA-256 и метаданных Mono.Cecil подтвердило тот же файл.
Использованы ранее полученный журнал загрузки и выборочное чтение ILSpy 11.1.
Игровой код, DLL, полные журналы и личные пути в этот документ не включены.

Для наблюдения дополнительно используются реальные локальные Unity модули
InputLegacy и IMGUI; их SHA-256 проверяются build/deploy и остаются в местном
манифесте пакета. `Rewired.InputAction` объявлен в `Rewired_Core`;
TG.Main ссылается на `Rewired_Core, Version=1.0.0.0, PublicKeyToken=null`.
Наблюдатель разрешает эту точную ссылку, не создаёт Player и не меняет его карты.

## Найденные экраны и первоначальные действия

- Главное меню: `Awaken.TG.Main.UI.TitleScreen.TitleScreenUI` и
  `VTitleScreenRogueUI`. Живую модель предоставляет `World.Any<TitleScreenUI>()`,
  основной view — `Model.MainView`. Требуются `IsFullyInitialized`, отсутствие
  `WasDiscarded`/`IsBeingDiscarded`, активные view/контролы, видимые Canvas/CanvasGroup
  и отсутствие покрывающего окна. `content: UnityEngine.CanvasGroup` отвечает
  за появление и исчезновение меню; одного наличия модели недостаточно.
- Поля `startGame`, `continueGame`, `switchSaveSlot`, `journal`, `options`,
  `leaderboards`, `exitGame` имеют тип `Awaken.TG.Main.UI.Components.ARButton`.
  Их смысл подтверждён `VTitleScreenRogueUI.OnInitialize()` и `UpdateView()`.
  Подписи находятся в текущих `TextMeshProUGUI` самих кнопок; технические имена
  полей не являются текстом для игрока. Continue скрывается по двум условиям
  сохранения, а стартовая кнопка может означать отказ от текущего забега.
  В последнем случае её original handler открывает подтверждение удаления;
  мод не должен напрямую вызывать удаление или автоматически выбирать ответ.
- Настройки: `Awaken.TG.Main.Settings.Windows.AllSettingsUI` и `VSettingsUI`.
  `VSettingsUI.apply/cancel/restoreDefaults: ARButton` подписаны соответственно
  на `void AllSettingsUI.Apply()`, `void Close()`, `void RestoreDefaults()`.
  `Close()` отменяет без изменений либо открывает подтверждение при изменённых
  параметрах. `Apply()` вызывает штатное применение SettingsMaster и закрывает окно.
  Это найденные обработчики; мод их пока не вызывает.
- Вкладки: `void AllSettingsUI.ReplaceContent(SettingsTabType tabType)`,
  `SettingsTabType CurrentTabType { get; }`. Список видимых настроек создаётся
  штатной `SpawnSettingsViews`; скрытую вкладку нельзя объявлять по одному enum.
  Вкладка клавиатуры зависит от наличия клавиатуры, gamepad — от числа контроллеров.
- Переключатель: `Awaken.TG.Main.Settings.Options.Views.VToggle` содержит
  `displayName: TextMeshProUGUI`, `toggle: UnityEngine.UI.Toggle`,
  private `_option: ToggleOption`. `void Setup(PrefOption option)` связывает
  подпись и состояние с этой опцией; listener штатного Toggle меняет её Enabled.
  `VToggle.Handle(UIEvent)` всегда возвращает Ignore и **не является** обработчиком
  переключения. Самостоятельная запись в Enabled или isOn модом не допускается;
  оригинальный путь Toggle ещё нужно отдельно подключить и испытать.
- Согласие на сбор данных: `Awaken.TG.Main.UI.TitleScreen.TitlePopupUI`,
  `string MessageText { get; }`, `void AcceptClicked()`, `void CancelClicked()`,
  `void LinkClicked(string text)`. `VTitlePopupUI` имеет `text: TextMeshProUGUI`
  и `accept/cancel: ARButton`; `OnInitialize` устанавливает видимый текст и
  подписывает кнопки на эти действия. Отдельный VModalBlocker покрывает меню.
  Это отдельная модель, не подкласс PopupUI. Решение остаётся у игрока.
- Обычное подтверждение: `Awaken.TG.Main.UI.Popup.PopupUI` и `VSmallPopupUI`.
  Видимый текст — `smallText: TextMeshProUGUI`; выборы создаются внутри
  `buttonParent: Transform` методом `void OfferChoice(ChoiceConfig choiceConfig)`.
  Кнопка получает исходный callback из ChoiceConfig. Количество, порядок и ответы
  нужно читать заново; нельзя угадывать, что первая кнопка всегда означает «Да».

У фиксированных кнопок меню есть стабильный структурный ключ по роли поля.
У настроек backing object — конкретный PrefOption, не индекс view в списке.
Это вывод для будущего дерева; карточные instance ids пока не исследованы.
Наблюдатель 0.1.2 сохраняет только MemberInfo и простые записи, не старые view.

## Найденные пути ввода

- `private void TitleScreen.OnGUI()` вызывает `World.Only<GameUI>().PerformGUI()`
  после `_initialized`. `public void GameUI.PerformGUI()` сначала, один раз
  за frame, вызывает `UpdateReInput()`, затем обрабатывает Unity Event.
  Prefix на PerformGUI расположен раньше именно этих операций.
  Это **не доказывает**, что он раньше Unity EventSystem или любых иных Update.
- `private void GameUI.DeliverReInputEvent(Rewired.InputAction action,
  Awaken.TG.MVC.UI.Events.UIAction eventToDeliver)` передаёт действие в keyboard
  stack. `UIAction.CreateButton` превращает `Accept` в UISubmitAction,
  `Cancel` в UICancelAction; `Horizontal/Vertical` участвуют в навигации.
  Фактические назначения и переназначения клавиш ещё не захвачены.
- `private void GameUI.HandleKeyDown(UnityEngine.Event unityEvent)` создаёт
  `UIEKeyDown` и учитывает `_heldKeys`; KeyUp убирает held key. Исходный словарь
  нельзя сбрасывать модом ради подавления повторов.
- `UIResult Awaken.TG.MVC.UI.Handlers.Focuses.Focus.BeforeDelivery(UIEvent evt)`
  обрабатывает native focus/Submit, включая обычные Button и Toggle.
  После этого другая часть маршрута может доставлять событие IUIAware.
  `UIResult ARButton.Handle(UIEvent evt)` при UISubmitAction вызывает исходные
  OnPress, OnRelease и OnClick. Он проверяет Interactable, но не визуальную
  доступность всей цепочки; будущий адаптер обязан проверять её перед invoke.
- Raw capture: `Awaken.TG.Main.Settings.Options.Views.VNewKeyBinding`.
  `UIResult Handle(UIEvent evt)` принимает UIEKeyDown, отменяется Escape и
  иначе передаёт NewKeyBinding. Это view в `Model.Views`, а не отдельная модель,
  которую можно найти через World.Any. Его создаёт `VKeyBinding.StartNewBinding()`
  через `World.SpawnView(Target, typeof(VNewKeyBinding))`.
  Наблюдатель ищет его в живых Views модели настроек и прекращает предложения
  клавиш для навигатора. Реальные текстовые поля остаются отдельным исследованием.

Уведомление Unity о потере фокуса также сбрасывает tracker: при остановке GUI ticks
нельзя рассчитывать, что он обязательно получит снимок с windowFocused=false.

Полные пространства имён событий: `Awaken.TG.MVC.UI.Events.UIEvent`,
`UIAction`, `UIEKeyDown`, `UISubmitAction`. UIResult находится в `Awaken.TG.MVC.UI`.
`UIEKeyDown.Key` унаследован от `UIKeyEvent` и имеет тип `UnityEngine.KeyCode`.

## Что реализовано в 0.1.2

`KeyboardSnapshotTracker` преобразует снимки удерживаемых именованных клавиш в
новые нажатия и повтор. Возврат фокуса окна, завершение raw/text capture и старт
требуют отпускания уже удерживаемой клавиши. Return и keypad Enter различаются
по физическому коду; смена Shift/Ctrl не превращает удержание в новое действие.
Для стрелок есть задержка и ограниченный повтор без серии запоздалых действий.
Это обработка снимков, не физическое подавление: будущий host должен отдельно
сохранять присвоение удерживаемой клавиши между повторными шагами навигации.

`GameInputAudit` по явному флагу ObserveTitleInput устанавливает только
наблюдателей перечисленных методов. Он читает именованные клавиши перед
PerformGUI, отмечает Rewired delivery, focus routing, accepted button Submit и
raw handler. Все оригиналы продолжают работать. Принятый Submit ещё не доказывает
изменение параметра: сравнение настоящего состояния обязательно в игровом сценарии.
Клавиши не передаются в Navigator, input claims и action bindings не включены.

Ограничение — 600 записей на запуск, после него запись прекращается. Флаг
сбрасывается при запуске. Буквы, цифры, содержимое полей, подписи, сохранения
и значения назначаемой новой клавиши не записываются. Наблюдение ограничено
выполнением original TitleScreen.OnGUI; World освобождается в finalizer,
включая исключение игры. Ошибки наблюдения изолируются. Отключение снимает
только hooks со своим Harmony id; пассивное чтение стартовой подсказки независимо.
Неизвестный fingerprint оставляет обычный диагностический режим.

## Проверки и следующий необходимый результат

- implemented: tracker и ограниченный read-only input audit.
- portable-tested: 11 новых сценариев; полный Runtime — 45, ошибок нет.
  Проверены отрицательные случаи и связка snapshot → Runtime → один action.
- compiled-with-game: 0.1.2 собран build.ps1 с настоящими Unity/BepInEx/Harmony.
- in-game-tested: **не выполнено для новой записи ввода**; прежний 0.1.1
  проверен только на чтении стартовой подсказки.
- blind-user-accepted: **не выполнено для 0.1.2**.

Следующий результат: получить trace на обратимом параметре и при удержании,
отпускании, Alt+Tab, возврате, открытии подтверждения и назначения клавиши.
Установить реальный порядок EventSystem и live Rewired maps, затем реализовать
узкое подавление chord и original-action ownership. Только после этого включать
первый активный путь «меню → настройки → изменение → назад» и принимать его с NVDA.
