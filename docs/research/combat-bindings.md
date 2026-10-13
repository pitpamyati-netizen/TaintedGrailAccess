# Привязки карточного боя M3

13 октября 2026: повторный локальный PE/CLI inventory и журнал BepInEx.
Conquest GOG 1.61, Unity 2020.3.23f1, Mono x64. TG.Main SHA-256
`26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`,
MVID `78793877-83a2-4bfc-970d-974256f05d98`. Остальные обязательные отпечатки
проверяются InspectedGameBuild. Assembly-CSharp также проверена inventory;
ниже все игровые привязки принадлежат TG.Main. Основание: локальный ILSpy 11.1,
публичные/закрытые метаданные; декомпиляция остаётся в игнорируемом artifacts.
Это статическое доказательство, не проверка действий в игре или принятие NVDA.

## Рука и действия

- `Awaken.TG.Main.Heroes.Fights.Deck.CardInHand : Element<HeroPiles>`:
  `Skill`, `Board`, `IsDissolving`, унаследованный `Model.ID`. Конкретный
  CardInHand и его ID различают одинаковые карты; позиция/имя не являются ID.
- `VCardInHand`: TMP `skillTitle`, `skillOnPlayDesc`, `cardTypeText`,
  `energyCostText`, `runeChargesCostText`, `flipStatesText`; `button : ARButton`.
  `LateUpdate/UpdateView/UpdateCost` показывают живую цену и сторону.
  `Skill.Availability == Hidden` скрывает view; остальные значения окрашивают его.
  `private async void ApplyClick()` подключён к `button.OnPress`. Он проверяет
  доступность, выбирает цель, валидирует её после анимации и вызывает UseSkill.
  Вызов ARButton.Handle(UISubmitAction) включает OnRelease в том же вызове;
  `FightTargeting.CheckButtonDrop` тогда использует мышь и может отменить цель.
  Поэтому клавиатурная активация вызывает оригинальный ApplyClick в ownership
  scope, без OnRelease и без прямого UseSkill/изменения игровых ресурсов.
  Перед вызовом проверяется наличие именно этого ApplyClick в живом OnPress;
  снятый игрой handler не вызывается повторно во время асинхронного розыгрыша.
- `HeroPiles.CardsInHand`; `VHeroPiles.endTurnButton`, `combatPointsText`,
  `deckCards/discardedCards/graveyardCards/exileCars`; `public void EndTurn()`
  подключён к OnClick и сохраняет EndingTurn hooks. Просмотр колод вызывает
  исходные showDeck/showDiscard/showGrave/showExile buttons; порядок будущей
  колоды мод не читает.
- `HeroFighter.IsExecutingTurn`, `Board`, `Hero`; `FightBoard.CurrentRound`,
  `Round.CurrentCharacter`. Розыгрыш/ultimate/конец хода допустимы только в
  собственный ход; конец уже завершённого хода не повторяется.

## Цели и классы

- `Awaken.TG.Main.Heroes.Fights.FightTargeting.PossibleTargets`, `Skill`, `Caller`;
  `public void MarkTarget(ITargetable, UIPosition, EventModifiers)` — точный
  handler, вызываемый VFightTargeting.Handle для mouse-up и Submit. Он
  передаёт выбор штатному Targeting, который запускает OnChosenTarget.
  Выбирается только живая показанная цель из PossibleTargets; дополнительно
  `Skill.IsValidTarget(ICharacter)` перечитывается перед действием.
  `VFightTargeting.Handle(UIEvent)` с `UICancelAction` — штатная отмена.
  Native Hover/default-focus/координаты для навигации мода не используются.
  `HeroFighter.FindTargetWithResult(Skill, ARButton, RectTransform)` имеет
  штатный caller-less путь. Только внутри собственного ownership scope мод
  передаёт caller=null: сама игра тогда использует QuickCast для единственной
  цели и сохраняет TargetedOnlyPossibleTarget с повторной проверкой после
  анимации. QuickCast=false или несколько целей открывают обычный targeting.
  Это изменение аргумента исходного handler, без AssignNewTargets/записи
  `_autoTargeted` модом; native вызовы остаются с прежним caller.
  В локальном 0Harmony 2.9.0.0 проверен HarmonyManipulator.EmitCallParameter:
  для ссылочного аргумента и ref object используется Ldarga, без изменения
  игровых полей. Это статическая проверка совместимости prefix, не live-вызов.
- `NpcFighter.IsAlive/IsEnemy/IsAlly/Board/Name`, `V3DNpcFighter`,
  `V3DFighterHUD`, `V3DNpcFighterUI`: наличие живого view и визуальная цепочка.
  `VCStatDisplay.text/nameText/icon`, `statType.Enum.DisplayName` — уже
  отрисованные HP/параметры; `VCBarrierDisplay.text` — барьер.
  `VCRangeStatDisplay.minStat/text` — показанный диапазон без пересчёта.
  StatType.Abbreviation/DisplayName и точные Stats.Health/CombatPoints/XP,
  RuneStat.Charges дают локализованное значение соответствующих показанных иконок.
  Realm.Stats.Wealth/WyrdShards: те же DisplayName/Abbreviation для показанной
  стоимости обновления выбора и валюты наград. VChooseWindow.wealth содержит
  уже отрисованную сумму; цена Reroll входит в исходный текст кнопки.
  V3DFightBoardUI.viewCanvas, druidTutorialIconButton/closeDruidTutorialButton:
  графическая классовая подсказка открывается/закрывается оригинальными OnClick,
  имеет собственный приоритет; её пиксели не превращаются в источник текста.
  `VIntent.textComponent/cooldown/icon/canvasGroup/TooltipConstructor`:
  скрытый intent (alpha=0, DisableIntent, пустая атака) не озвучивается.
  `IWithTooltip.TooltipConstructor.Objects` содержит строки именно штатной
  подсказки. Объекты-карты/шаблоны не выгружаются как текст.
- `VOnStatusBar<T>.TooltipConstructor`, `VStatus`, `VFighterTier` и остальные
  видимые status views: чтение фактической подсказки и её счётчиков, без
  пересчёта механики. Повторный Build перечитывает текущее состояние.
- `RuneUI.Skill/Rune/Fighter/Board`; `VRuneSkill.charges/specialText/button`,
  `TooltipConstructor`, private `ApplyClick()` — общий handler классов.
  QuickCast принадлежит игре; мод не меняет настройку. RuneDeck preview
  открывается штатным `Handle(UIEMouseDown)` с Button=1, затем штатный ApplyClick
  выполняет способность. Уже использованные preview-карты не активируются.
  Private RuneSubTooltip строит классовое описание для обычного правого клика;
  оно читается вместе с TooltipConstructor. V3DNpcFighter.TooltipConstructor
  зависит от native HoveredTarget; мод не вызывает Hover и читает отдельно
  показанные stats, status и intent для обзора целей.
- `VCombatItem.CanBeUsed/TooltipConstructor`, `VFightCombatItem.Handle(UIEvent)`
  с UISubmitAction вызывает штатный QuickUse. Количество берётся из Item.Quantity.

## Выбор, просмотр и результаты

- `ChooseWindow.Configuration/Options/Selected`; Setup `minSelection`,
  `maxSelection`, `onlyDisplay`, `closeable`, `showAccept`.
  `VChooseWindow.acceptButton/skipButton/rerollButton/decorsParent`.
  `VOption.optionButton/content`, `Option.Selected/BackingElement/ParentModel`.
  Штатный OnClick вызывает Option.Clicked и игровые selection hooks; подтверждение
  разрешается только по живому min/max и Interactable. Просмотр onlyDisplay не
  выбирает карту. Escape вызывает Close только при closeable, Skip — только
  через видимую штатную кнопку. Обязательный выбор не превращается в отменяемый.
  VCCardPreview читает показанные title/description/cost/flip и известный tooltip;
  unknown description не раскрывается (_knownDescription).
- `CardToStaySelector.SelectionEnabled/SelectedCard/AllowNextTurn`,
  `VCardToStaySelector` и VCardInHand.ApplyClick — выбор сохранённой карты.
  Подтверждение вызывает VHeroPiles.EndTurn. Обнаружен статический риск:
  HeroFighter.EndTurn сначала ставит _isExecutingTurn=false, затем SpawnView
  возвращает метод до отмены turn token. Второй вызов может сразу выйти из-за
  false. Живое воспроизведение пока отсутствует; успешное продолжение не
  заявляется. Собственные назначения флага/token/DiscardHandDestination запрещены.
- `VCardInHand.Handle(UIEvent)` и `VCCardPreview.Handle(UIEvent)` с
  UIKeyDownAction.Name=ToggleMap открывают CardSpecPage. `CardSpecPage.Close()`
  и `VCardSpecPage.Handle(UICancelAction)` закрывают его. Это просмотр всех
  разрешённых сторон, а не принудительное SetFlipState. Преобразование в руке
  выполняет только игра, новый текст читается по тому же экземпляру.
- `VBattleLog.loggedMessages` — тот же список, что визуальный DynamicScrollView;
  Refresh исключает LogType.Technical. `LoggedMessage.log/roundNumber/logType`.
  Журнал мода не читает payload/будущие результаты; событийная речь сообщает
  только новые записи, краткий просмотр ограничен текущим раундом.
- `FightEndPanel`, `FightEndVictoryPanel`, VFightEndVictorySummary.continueButton
  и Handle(ISubmit) для ускорения показанной анимации; VFightEndItemsLoot.takeAllButton,
  VRewardedItem; ChooseWindow выбора карты/passive/reforge; VCombineOrbsUI
  leftArrow/rightArrow/continueButton, VPossibleOrbCombine.fuseButton.
  Читаются только появившиеся reward views. Исходные кнопки выполняют выдачу;
  мод не записывает награды. Логотип поражения/победы закрывается игрой по анимации.

## Владение вводом и блокировки

Poller/Rewired/EventSystem остаются механизмом M1. Дополнительные узкие guards
на Submit боевых view пропускают собственный ownership scope. ApplyClick/EndTurn
не блокируются общим наличием присвоенного Enter: одновременная отдельная
клавиша игры должна сохраняться. Их клавиатурные источники уже проходят через
исследованные GameUI/Rewired/ARButton guards. `VCPadShortcut` создаёт ButtonShortcut в GameUI, не отдельный
сырой Input poller. `VFightModalBlocker` пропускает четыре конкретных view и умеет
Dismiss; мод не убивает анимации. `VModalBlocker` на Card/Skill закрывает обычные
действия до завершения; самостоятельные modal-окна имеют приоритет и сохраняют
GraphState боя. Неизвестный modal по-прежнему передаёт ввод игре.

Игровые сценарии: рука → tooltip → розыгрыш → цель/отмена → конец хода;
выбор N карт; сохранение карты; девять классов; смерть цели; победа/награды;
поражение; удержание Enter, Alt+Tab, повторный вход. Результаты записываются
отдельно в M3_VERIFICATION; найденная сигнатура не означает успешный сценарий.
