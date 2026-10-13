# Журнал подтверждённых игровых привязок

Структура адаптирована из game-api.md.template и setup-guide;
[источник и условия](research/template-import.md).
Метод исследования — [GAME_DISCOVERY.md](GAME_DISCOVERY.md).

## Исследование M4, 13 октября 2026

Повторно проверены inventory GOG 1.61 и журнал загрузки. Установлены реальные
правила Location.FullyVisible/VLocation.ShouldBeVisible и fullyIcon/partlyIcon
карты; частичный значок не разрешает чтение имени. BaseScene регистрирует
AstarPathfinder; найден оригинальный Hero.AcceptTarget(TargetChange),
HeroMovement.GoTo/CancelMove и отдельный прямой опрос VHeroKeys.
Точные имена, сигнатуры, отпечаток, исходные вызывающие методы и границы
собраны в [exploration-bindings.md](research/exploration-bindings.md).
Это статические доказательства; проверка движения и NVDA M4 пока отсутствует.

## Дополнение M3, 0.3.4, 13 октября 2026

Повторный PE/CLI inventory и последний журнал загрузки подтверждают ту же
GOG 1.61, Unity 2020.3.23f1, Mono x64 и BepInEx 5.4.23.5. TG.Main:
SHA-256 `26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`,
MVID `78793877-83a2-4bfc-970d-974256f05d98`. Assembly-CSharp также включена
в inventory; перечисленные ниже новые члены находятся в TG.Main.
Способ нахождения: локальные метаданные и ILSpy 11.1; игровые файлы не публикуются.

- `Awaken.TG.Main.Heroes.Fights.Deck.VRuneSkill._runeThresholds`:
  `Dictionary<UnityEngine.GameObject,int>`. `CreateThresholds()` создаёт
  видимые деления из RuneThreshold1..N, `UpdateView()` окрашивает их по заряду.
  Мод читает только значения делений с живыми видимыми GameObject, без
  вычисления результата ultimate. Действие остаётся `private void ApplyClick()`.
- `Awaken.TG.Main.Fights.NpcFighters.Tiers.VFighterTier.tierNumber`:
  `TextMeshProUGUI`. `RefreshTier()` выводит `CurrentTierNumber`; inherited
  `TooltipConstructor` через `TooltipWithText` даёт текущее описание уровня.
  Число теперь доступно отдельно и обновляется через Live-watch. Привязка
  владельца — `IStatusSource.Character`; видимость — текущий view/Canvas/TMP.
- `Awaken.TG.Main.Journal.UI.ViewComponents.VCCardPreview._normalBackground`:
  `bool`. `VRuneDeckUI.SpawnCard(Skill,bool)` передаёт сюда `available` через
  `Fill(Skill,bool,bool,bool)`: обычный фон означает доступную карту, затемнённый —
  использованную. Это показанный признак, а не чтение будущего выбора RuneDeck.GetCard.
  Все карты этого просмотра остаются без действия розыгрыша.
- `VCCardPreview.AllowCardSpec`: `bool`; `Handle(UIEvent)` открывает
  `CardSpecPage` по `UIKeyDownAction.Name=ToggleMap` только при AllowCardSpec.
  Этот оригинальный handler теперь доступен и из классовой колоды через Backspace.
  `CardSpecPage` получает отдельный приоритет; Escape возвращает прежний фокус.
- `VCCardPreview._knownDescription`: `bool` — прежняя привязка, уточнено чтение.
  TooltipConstructor возвращает пустой текст, когда у ParentView.GenericTarget
  уже открыт CardSpecPage. Поэтому Space в просмотре стороны читает фактический
  `skillOnPlayDesc`, когда tooltip пуст. При unknown сам tooltip не запрашивается;
  остаётся показанное игрой Unknown. Аналогичный fallback применён к руке.
- Классовая графическая подсказка `V3DFightBoardUI.viewCanvas` теперь содержит
  пункт чтения исходных `VRuneSkill.TooltipConstructor/RuneSubTooltip`. Пиксели
  картинки по-прежнему не расшифровываются; это исходное текстовое описание умения.
  Закрытие — тот же `closeDruidTutorialButton.OnClick`.
- `VPossibleOrbCombine.ToggleVisibility(bool,TweenDirection?,bool)` плавно
  меняет CanvasGroup.alpha; прежний и новый варианты могут одновременно иметь
  видимые view. У каждого `fuseButton` handler `PossibleOrbCombine.Fuse()`.
  Ключ кнопки теперь содержит Model.ID её настоящего владельца и роль действия.
  Это предотвращает duplicate identity при перекрывающихся анимациях;
  CanvasGroup.interactable и исходный ARButton по-прежнему решают доступность.

Локально прочитаны RuneUnlocking.RuneName и подтверждены девять статических
идентификаторов: Archer, Sharpshooter, Flipper, Wyrdhunter, Pathfinder, Berserk,
Summoner, BloodMage, Necromancer. Empty — служебный вариант. В VRuneSkill нет
раздельных handlers по этим именам: он работает с текущей RuneUI.Skill;
RuneDeck определяется наличием элемента, а не предположением о классе.
Проверены общие пути FighterTiers для BloodFighterTier/EleFighterTier/EtherealTier,
LichForm и SkillEffectCardTransform/SkillEffectFlipHandBy. Их внутренние действия
мод не вызывает: преобразование запускается исходным розыгрышем/ultimate, после
него дерево читает появившиеся UI и экземпляры. Матрица —
[m3-class-coverage.md](research/m3-class-coverage.md).

0.3.4: Graph 94, Runtime 84, Windows 18, Python 21; ошибок 0. Регрессии с прежним
способом чтения/ключей дали два ожидаемых сбоя: пустое описание стороны и
duplicate identity кнопок награды. Исправленная реализация прошла. BindingChecks
подтвердил новые члены, девять идентификаторов и восемь валют; сборка без
ошибок/предупреждений. Это portable-tested и compiled-with-game, не игровые действия.

Установка 0.3.4 выполнена при закрытой игре; шесть файлов пакета и копия прежней
0.3.3 проверены по SHA-256. На сценарий сохранения карты пользователь ответил:
«Такое окно пока не появилось». Это отсутствие игрового наблюдения, а не успешное
продолжение. Статический риск HeroFighter.EndTurn остаётся открытым: при выборе
мод не меняет `_isExecutingTurn`, token или DiscardHandDestinationOverride.
Добавлена запись состояния только при исходном подтверждении
`combat.retain.confirm`, чтобы следующий живой сценарий можно было разобрать.
Новая 0.3.4 пока без in-game-tested и blind-user-accepted.

## Названия значков ресурсов после боя, 0.3.3

13 октября пользователь указал класс «егерь» и сообщил, что после боя
слышит «b m и так далее»; до выбора награды не доходил. Сохранённый журнал
первого боя содержит `Значок: B Недоступно`, `CS`, `JP`, `RD`, `Ta`, `GT`.
Это наблюдение прежней версии, а не повторная игровая проверка исправления.

В той же исследованной TG.Main GOG 1.61, SHA-256
`26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`, MVID
`78793877-83a2-4bfc-970d-974256f05d98`, найдены точные статические поля
`Awaken.TG.Main.Realms.Realm+Stats.RunestoneDust`, `Blood`, `Tallow`,
`CardShreds`, `GoldenTokens`, `JournalPages`. Локальный inventory и ilspycmd
подтверждают пары RD/B/Ta/CS/GT/JP в `StatType.Abbreviation` и соответствующие
игровые `StatType.DisplayName`. `Realm.Currencies` перечисляет эти шесть
ресурсов вместе с уже связанными Wealth/WyrdShards.
`VCurrenciesUI.ShowStats` передаёт тип в `StatChangeDisplay.SetTextsInstant`;
тот выводит `IconTag` и видимое название либо значок с «Недоступно».

0.3.3 сопоставляет только значок, уже присутствующий в читаемом тексте,
с текущим локализованным DisplayName. Недоступность и показанные числа
сохраняются; новые ресурсы, награды и игровые действия не открываются.
Новая проверка BindingChecks до правки перечислила все шесть пропусков.
После правки BindingChecks подтвердил все восемь ресурсов; сборка с игровыми
файлами прошла без ошибок и предупреждений. 0.3.3 установлена при закрытой
игре; шесть файлов сверены с пакетом, предыдущая 0.3.2 сохранена и сверена.
Пользователь ответил «Да, всё работает» на проверку новых названий ресурсов
и последующего «Продолжить» → чтение и получение доступной награды клавиатурой
с NVDA. Класс указан ранее: егерь. Точное название награды не указано;
это подтверждение доступного пути после одного боя, а не всех видов наград,
поражения или полного M3.

На следующий сценарий выбора нескольких карт либо сохранения карты на
следующий ход пользователь ответил: «Такое окно пока не встречалось».
Этот результат означает отсутствие игрового наблюдения; выбор, счётчик,
подтверждение и продолжение боя через это окно ещё требуют проверки.

На проверку подтверждения цели с удержанием Enter около секунды пользователь
ответил: «Разыгрывается один раз». Принят однократный розыгрыш при удержании
в установленной 0.3.3. Возврат фокуса после этого удержания отдельно не описан.

Переключение окон проверено следующим отдельным сценарием: подтвердить цель
Enter, удерживать Enter, уйти в другое окно через Alt+Tab и вернуться,
затем отпустить клавишу и нажать её заново. Ответ «Подтверждаю, всё работает»
принимает отсутствие повторного розыгрыша до отпускания и работу следующего
отдельного нажатия с NVDA в 0.3.3.

На вопрос о доступных классах пользователь ответил: «Пока доступен только
Егерь». Это текущая доступность в его прохождении, а не проверка остальных
классов. Их игровые механики остаются без приёмки с NVDA до обычной
разблокировки в игре; сохранения и условия разблокировки не изменялись.

## Дополнение 0.3.1 после проверки первого боя

13 октября пользователь подтвердил работу Ctrl+H/Y/R, Ctrl+I/Alt+I и
сохранения фокуса в руке/перехода к концу хода: «Всё работает». При этом
сообщил, что Escape объявляет «Меню игры, Продолжить», но меню не остаётся
открытым. Подтверждение относится к перечисленному сценарию 0.3.1.

## Исправление Escape в 0.3.2

В свежем журнале установленной 0.3.1 первый Escape на кадре 1583 передан игре
без фокуса мода. На кадре 1584 появились «Меню игры»/«Продолжить» и немедленный
собственный вызов `combat.close`; последующие повторы этой последовательности
видны на кадрах 3523/3524 и 3599. Точный исследованный handler —
`Awaken.TG.Main.UI.Menu.MenuUI.Close()`, тот же, что у
`Awaken.TG.Main.UI.Menu.VMenuUI.resumeButton.OnClick`. Типы разрешены в
TG.Main SHA-256/MVID, указанном в [combat-bindings.md](research/combat-bindings.md).

Причина найдена в Navigator: физический tracker передаёт удерживаемый Escape
как IsRepeat каждый кадр, а OnBack вызывался и на повторе. Теперь повтор
присваивается без вызова OnBack. Первый Escape без обработчика остаётся игре;
закрытие меню требует отпускания и нового нажатия. Исходный MenuUI.Close,
правила видимости и выбора живого владельца сохранены, новые hooks не добавлены.
Четыре проверки охватывают открытие/закрытие при удержании, отмену поиска,
raw/text/модификаторы и исключение после исходного закрытия. Две проверки
воспроизвели ошибку до правки, все 81 проверки Runtime прошли после неё.
0.3.2 собрана и установлена. 13 октября 2026 пользователь с NVDA подтвердил:
Escape открывает меню, удержание оставляет его открытым, стрелки читают пункты,
отдельное нажатие закрывает меню, следующее снова открывает. Ответ:
«Да, всё работает». Это проверка меню.

Следующим ответом «Всё работает» пользователь подтвердил сценарий 0.3.2
с NVDA: в бою с несколькими врагами карта открывает выбор цели; нажатие,
удержание и отпускание Escape отменяют выбор, фокус остаётся в руке,
меню не появляется. Последующий Enter → выбор цели → Enter разыгрывает карту.
Название класса и карты не указаны. Очистка поиска при удержании Escape,
удержание подтверждающего Enter и исчезновение цели этим ответом не проверены.

## Исходные доказательства 0.3.1

13 октября пользователь сообщил, что прошёл первый бой с NVDA на исходной
0.3.0. Журнал также показывает экран результата и возврат к обучению;
отдельная победа/награда и все классовые варианты этим ответом не подтверждены.
Отмечены две проблемы: отсутствовали быстрые сведения и после удаления карты
фокус уходил к герою. Журнал показывает уход от первой карты к последнему
состоянию героя после удаления, при живых остальных картах руки.

0.3.1 использует явное предложение GraphState для выжившей карты руки и
пассивные сочетания из [KEYBOARD_AND_SPEECH.md](KEYBOARD_AND_SPEECH.md).
Новая точная привязка в том же TG.Main SHA-256/MVID:
`Awaken.TG.Main.Fights.Modifiers.Views.VBlock`, найденная inventory и локальным
ilspycmd. Исходный TooltipWithText строит локализованный текст блокирования
из действующего BlockElement; мод читает текущий IWithTooltip видимого
VBlock того же героя. Поля игрового состояния и обработчики не меняются.
Ctrl+H выбирает показанный VCStatDisplay с точным живым Stats.Health;
классовый ресурс берётся из видимых VRuneSkill.charges/specialText.
Краткое намерение включает основную игровую подсказку значка, а не только
число урона. Подробное включает вложенные подсказки. VIntent.Refresh показывает
Countdown отдельным видимым текстом; мод сохраняет это число с меткой отсчёта.
0.3.1 собрана, привязки проверены, установка сверена; эти исправления ещё
не проверены в запущенной игре. Исходные доказательства 0.3.0 ниже исторические.

## M3: исследование боя, 13 октября 2026

Повторный inventory и свежий журнал подтвердили установленную GOG 1.61.
Точные найденные владельцы, тексты, экземпляры карт, handlers, видимость,
классовые варианты, блокировки и сценарии записаны в
[combat-bindings.md](research/combat-bindings.md). Это статические доказательства;
проверка нескольких боёв и приёмка NVDA пока не выполнены.
Первый запуск 0.3.0: журнал подтверждает переход в боевой Graph, чтение
показанных здоровья/намерений/ресурсов, вызовы исходных ApplyClick и MarkTarget,
возврат из targeting и появление статуса блокирования. Ошибок адаптера на этом
отрезке не зарегистрировано. Это узкое наблюдение первого боя, ещё без
подтверждения слышимости пользователем на момент записи. Позже пользователь
сообщил о прохождении первого боя, как указано выше; весь M3 ещё не принят. Позже в том
же запуске подтверждены исходный конец хода и новый раунд, открытие/закрытие
колоды и сброса, вызовы ultimate и предмета. Сам вызов предмета ещё не
доказывает его расход: доступность и результат определяет игра.
Уточнён оригинальный `HeroFighter.FindTargetWithResult(Skill, ARButton,
RectTransform)`: caller=null включает штатный QuickCast-путь одной цели без
mouse-dependent OnRelease. Он передаётся только для собственного вызова;
нативный caller сохраняется. Metadata подтверждена; on/off и повторный ввод
этого последнего уточнения ещё не проверены в игре.

## M2: новые статические привязки, 13 октября 2026

Повторный inventory GOG 1.61 подтвердил прежний SHA-256 TG.Main; конструктор
всех новых привязок прошёл BindingChecks с настоящими DLL. Это проверка
метаданных и сборки. Ползунок и текстовый редактор затем проверены в игре с NVDA;
после исправления перехода пользователь самостоятельно дошёл до первого боя.

- `VSlider.displayText/slider/selectable/_option`, `private void MoveSlider(float)`:
  исходный Handle(UINaviAction) вызывает именно MoveSlider; шаг игры — 0.1
  диапазона, clamp/wholeNumbers/listeners выполняет Unity Slider. Мод вызывает
  этот handler, не присваивает SliderOption.Value самостоятельно.
- `VEnumArrows.settingNameText/optionNameText/leftButton/rightButton/_option`:
  ARButton.OnClick вызывает PreviousOption/NextOption; край и запрещённые варианты
  определяет EnumArrowsOption. Циклического перехода нет.
- `VDependentOption._baseOptionView/_otherOptionsViews/_option` и
  `DependentOption.ShowDependent`: вложенные живые view существуют только при
  включённой базе. Чтение не раскрывает OtherOptions из невидимого шаблона.
- `PrefOption.ID/TooltipConstructor`, VLinkOption.displayName/button/_option:
  стабильный ID опции, локализованные tooltip и штатный callback ссылки.
- `BaseScene.World/IsInitialized`, private `OnGUI()/OnDestroy()`:
  OnGUI вызывает GameUI.PerformGUI после IsInitialized. Слабая ссылка на
  контроллер позволяет получить текущий World; UI-ссылки освобождаются в callback.
- TitleScreenGameModes/VTitleScreenGameMode, LoadSaveUI/VLoadSaveUI/SaveSlotUI,
  PatchNotesUI/VPatchNotesUI, WelcomePopupUI/VWelcomePopupUI: штатные кнопки
  входа/возврата/выбора ячейки; удаление по-прежнему проходит отдельное PopupUI.
- HeroCreatorUI/VHeroCreatorButtons и GenderUI/VGenderUI.heroNameField:
  новая пустая ячейка требует создания героя и имени перед обучением. Кнопки
  accept/close и SetSelectedGameObject→TMP_InputField.OnPointerClick используют
  оригинальный поток; не записываются имя и содержимое редактора.
- ClassSelection.Visible, VRuneSelection.IsInitialized/IsUnlocked/Interactable,
  Handle(UIEvent): UISubmitAction вызывает RuneSelection.Select; NewRunSelection
  и VNewRunSelection читают текущие тексты класса, умения и сложности. Кнопки
  start/changeClass/lower/increase вызывают оригинальные обработчики.
- Story.VStoryPanel/VStartStoryPanel, VChoice.choiceButton и VStartStoryChoice:
  текст берётся из живого storyParent/choicesParent, действие — ARButton.Handle,
  причина недоступности — уже отрисованный additionalText. Будущие графы истории
  не читаются. VChoice прикрепляет callback с задержкой 0.5 с; активация разрешена
  только после наличия собственного Action в ARButton.OnClick (точный backing field).
- VSmartText.textMesh/bigTextMesh на реальном ViewHosting.OnHUD(string[]) и
  VVideoSubtitles.text: только отрисованная текущая подсказка/субтитр. VCAccessibility
  меняет шрифты, собственного речевого канала игры не найдено.
- FightBoard.IsFightStarted/IsFightOver обозначают вход в бой; управление картами
  не относится к M2. Наличие FightBoard до появления Round не считается стартом.
- TitlePopupUI.LinkClicked(string) вызывает Application.OpenURL. Читаются
  реально отрисованные TMP links и текст решения; согласие автоматически не даётся.
- TMP_InputField.selectionStringAnchorPosition/selectionStringFocusPosition/text:
  снимок после native KeyPressed и LateUpdate даёт фактическую правку/курсор.
  Ctrl+F1 читает текст, Ctrl+Enter завершает через оригинальный
  EventSystem.SetSelectedGameObject(null)→OnDeselect; повтор и открывающий Enter
  сохраняют ownership. Частная речь исключает содержимое из transcript.

Все новые game members — TG.Main с SHA-256/MVID раздела профиля ниже;
text/editor — Unity.TextMeshPro с точным профилем из InspectedGameBuild.
Подробная проверка и границы — в [M2_VERIFICATION.md](M2_VERIFICATION.md).
В игре подтверждены MoveSlider, создание героя и native текст. Первая сборка
после перехода в BaseScene не находила доступного экрана. Диагностика использует
точный `World.AllInOrder()` без параметров: `World.All<Model>()` здесь не включает
все модели, поскольку Register добавляет только ModelUtils.ImplementedTypes.
Пишутся только типы view и результат видимости; личные тексты не экспортируются.

Причина остановки установлена по live журналу и точным обработчикам:
`ScenePreloader.Start/InitWorld` создаёт собственный World, его приватное
свойство World и слабая ссылка на ScenePreloader подключаются отдельно.
`LoadingScreenUI` остаётся в этом World при additive загрузке основной сцены.
`Scenario.RunInitialStory` ждёт SceneInitializationHandle.OnInitialized;
`Tutorial Scene Initialization` завершается только по подтверждению игрока.
`VLoadingScreenUI.hintMessage/firstGameText/firstGameMessage/waitForInput`
показывают сообщение/готовность; `WaitForInputEnabled` запрещает раннее действие.
`VLoadingScreenUI.Handle(UIEvent)` для UIEMouseDown завершает последовательность
и вызывает `CompleteTutorialLoading`. UIEMouseDown имеет публичный пустой
constructor, а этот Handle не читает координаты, GameUI или Position. Мод
вызывает его после готовности и появления подсказки, не завершает загрузку сам.
LiveWorld сначала выбирает видимое загрузочное окно промежуточной сцены.
В ней нет ShortcutsLayerStack/GameUI, поэтому обычные сервисы меню не запрашиваются.
Тот же VLoadingScreenUI.Update напрямую опрашивает `KeyBindings.Accept = "Accept"`;
узкий prefix подавляет только его Update в кадре присвоенного физического
источника Accept, с теми же проверками независимого контроллера, что Rewired.

Начальная история использует `VBigPopupUI : VPopupUI : View<IStoryApi>`,
а не VStoryPanel. `VPopupUI.titleText/parent/buttonParent` содержат только
уже отрисованный текст и ARButton; OfferChoice прикрепляет исходный callback.
Этот вариант теперь входит в живой Story adapter и пройден пользователем с NVDA.

Живой переход через загрузку и начальная история теперь читаются; пользователь
дошёл до последней реплики и сообщил, что Tab не находит следующего действия.
Точный `StoryUtils.WaitForInput` создаёт StoryWaitForInput и текущий элемент
`KeyboardListenerForInput : Element<IStoryApi>`. Его Handle(UIEvent) принимает
UIKeyDownAction с `Data.actionName` равным SkipDialogue или FocusHero; отмена
token запускает штатное завершение ожидания/аудио/cleanup. Подключён отдельный
пункт «Продолжить» только для действующего listener той же текущей Story,
через этот Handle; future Story steps и token не изменяются модом. UIKeyDownAction
имеет пустой constructor, Data — фактический InputActionData, actionName — string;
этот обработчик не читает Player/Position. KeyBindings.SkipDialogue = "SkipDialogue".

После подключения этого ожидания пользователь самостоятельно дошёл до первого
боя и услышал «Первый бой начался. Управление боем относится к следующему этапу».
Журнал подтверждает исходные вызовы продолжения истории, выбора класса и начала
забега. Это подтверждение входа в бой, а не доступности карточных действий M3.

Речевое оформление: `InputActionsToTextMapping` и публичные массивы
`keyToTextBidings/mouseBidings/gamepadToTextBidings`, вложенные
`KeyToTextBiding/ActionToTextBiding/GamepadIndexToTextBiding` с полями
`spriteName/text`, `KeyToTextBiding.keyCode`. `Services.TryGet<T>()` без параметров
возвращает текущую таблицу или null. `VSmartText.Fill` использует эту же таблицу
до FormatSprite. В речи уже показанный named sprite сопоставляется с её текстом
или фактическим KeyCode; неоднозначный/неизвестный значок сохраняет своё имя.
Форматирование удаляется, условия со знаками сравнения сохраняются. Личный
текст редактора обходит преобразование и transcript. Точные metadata проверены
BindingChecks; новая очистка проверена переносимыми тестами, отдельно с NVDA
после принятия пути не повторялась.

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
