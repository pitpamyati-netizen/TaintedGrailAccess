# Исследование M4: GOG 1.61

13 октября 2026 повторно проверены PE/CLI inventory и последний журнал загрузки.
Mono x64, Unity 2020.3.23f1, BepInEx 5.4.23.5. TG.Main SHA-256:
`26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca`,
MVID `78793877-83a2-4bfc-970d-974256f05d98`. Assembly-CSharp также проверена.
Способ нахождения: локальные метаданные и выборочное чтение ILSpy 11.1.
Игровые DLL и декомпиляция остаются вне публикуемых файлов.

Подтверждено по исходным обработчикам; живая проверка 0.4.0 выявила отказ
предварительной оценки, журнал 0.4.1 подтвердил подходы/диалоги/возвраты:

- `Awaken.TG.Main.Locations.Location.VisibleToPlayer: bool` исключает Hidden
  и HiddenPassiveProgress; `FullyVisible: bool` дополнительно исключает InFOW.
  `VLocation.ShouldBeVisible: bool` дополнительно учитывает InRenderingRange.
  Нельзя извлекать скрытое имя и сведения шаблона из частичного маркера.
- `Awaken.TG.Main.Map.Minimap.VMapMarkerUI.fullyIcon/partlyIcon: Image`:
  UpdateVisibility учитывает FullyVisible, загрузку спрайта и границу биома.
  `MapMarkerUI.reference: MapMarker`, `MapMarker.MapCoords: MapCoords` —
  координаты именно показанного значка; TooltipText доступен лишь для fullyIcon.
  Частичный маркер имеет анонимную подпись и направление; в 0.4.4 допускается
  смысл именно его показанного специализированного символа, без чтения full sprite.
- `Awaken.TG.Main.Heroes.Hero.AcceptTarget(TargetChange): bool` — исходный
  обработчик IOrderable, вызываемый `Targeting.RetargetSelected` при клике.
  Он вызывает HeroMovement.AcceptNewTarget и ExecuteMovement. TargetChange
  содержит ordered, targeted, UIPosition, EventModifiers и isDrag. Place.HeroTarget
  выбирает штатную точку подхода; Move.CheckArrived запускает Hero.ArriveAt и
  оригинальное взаимодействие. Прямой записи Coords/transform.position нет.
- `HeroMovement.GoTo(MapCoords,bool,float): void` — исходный путь VHeroKeys;
  с withPathfinding=true выполняет PlanMove, без раскрытого target и события.
  `HeroMovement.CancelMove(): void` освобождает исходный Move.
- `BaseScene` регистрирует `AstarPathfinder` как IPathfinder. PlanMove вызывает
  `FindWaypointsFor(Seeker,MapCoords,MapCoords,bool,OnPathDelegate)`; `Move`
  пересчитывает путь каждые 0.5 секунды. Для явной проверки применяется
  `FindWaypoints(MapCoords,MapCoords,bool,OnPathDelegate)` без Seeker героя.
  Общий запрос задаёт NNConstraint.None и вручную применяет SimpleSmoothModifier;
  запрос через Seeker использует настройки и обработку пути самого героя.
  PlanMove отклоняет только null, а не отклонение конечной точки общего запроса.
  Радиус Move.NextWaypointDistance — 3 единицы до очередной точки его пути,
  он не является разрешением для общего предварительного запроса.
  В 0.4.1 Space/F1 оценивает начало/конец/длину без вмешательства в Seeker.
  Enter не вызывает этот запрос, а передаёт решение исходному обработчику.
- `Move.CurrentPosition/TargetPosition: MapCoords`, `AnyWaypointsRemaining`,
  `CanExecute`, `Progress`, `Cancellable`; Progress имеет значения 0/2,
  поэтому процент завершения из него не вычисляется. UIStateStack.State.
  IsMapInteractive определяет возможность команд. HeroMoveBlock запрещает
  новый приказ. При FightBoard/StoryOnTop игра сама отменяет Move.
- `VHeroKeys.Horizontal/Vertical/CameraRotation: float` читают Rewired напрямую
  в Update, вне GameUI delivery. Нужны узкие guards этих трёх getters по живым
  физическим источникам; один перехват DeliverReInputEvent недостаточен.
- `VGameCamera.Camera: Transform` — горизонтальное направление камеры, как
  в VHeroKeys.Update. Речь «впереди/справа» относится к текущему виду камеры.
  MapCoords.x/z — единицы карты; расстояние по прямой отдельно от длины пути.
- `VWyrdcandlesUI.wyrdcandlesCounter: TextMeshProUGUI`,
  `wyrdcandleBurnRange: Image`, `wyrdTouchEffect: GameObject`,
  `TooltipConstructor`, `arButton: ARButton`: читается только показанный HUD.
  BurnRange — показанная полоска/исходный tooltip, будущие WyrdTouches не читаются.
  Действие свечи идёт через исходный ARButton.Handle(UISubmitAction).
- `LocationBrief/VLocationBrief`: показанное имя и живые кнопки; закрытие
  проходит через `VModalBlocker.Handle(UIEvent)` и ModalDismissed, как клик
  по исходной подложке. Внутренний список будущих действий/врагов не экспортируется.
  `IsOnHoverInfo: bool` отделяет подсказку от окна выбора: только второе
  блокирует движение. В 0.4.2 проверяется вся коллекция живых LocationBrief,
  а не первая модель, которая может оказаться обычной подсказкой.
- Имя читается из показанного `VLocationBrief.nameText`, затем из
  `MapMarker.TooltipText: string` на показанном fullyIcon, затем из раскрытого
  `Location.DisplayName`. MapMarker сохраняет текст при создании Place;
  DisplayName применяет текущие LocationState/ILocationInfoModifier overrides.
  `LocString.ToString()` использует исходную LocalizationManager.GetTranslation.
  Значение «…» не даёт произносимого имени и заменяется общей подписью мода,
  без чтения исходного скрытого Template.displayName или перевода заново.
- `HeroInteraction.ShouldHappen(Hero,Place)` требует не-Invisible действий
  и `Location.Interactability.interactable`. Менгир с false остаётся доступен
  для подхода, но не взаимодействия; в 0.4.1 это подтверждено журналом.
- 0.4.4: VMapMarkerUI.SetSprite использует MarkerIcons или customMarker;
  LazySprites.TryGet(string) выбирает точное Sprite.name. Проверена авторская
  MapMarkerIconsBetter (sharedassets0.assets SHA-256
  `ade1c493197c482c119d58d3e7eb96aaa7535233b6e640c806976a4d1eca07db`, объект 528):
  23 правила, восемь общих смыслов. Менгир/портал и магазин/другой NPC имеют
  общий символ, поэтому узкое назначение по нему не утверждается. Рантайм
  читает только показанный включённый Image.sprite.name; скрытые Tags/Template
  не читаются. Неизвестный символ остаётся с номером. Нужна проверка NVDA.

AstarPathfindingProject SHA-256 `2e45f26e3a09194232aebf342f6c5aa22627f6533d409b30e00c2278fa2778fe`,
MVID `48d59566-cb6c-457f-8635-ba85dab68d1c`. Совпадение проверяется до
подключения ввода, а build/deploy сверяют эту зависимость с пакетом.

У каждого действия повторно проверяются живой владелец, карта, модальность,
видимость и доступность. Связи M2/M3 остаются владельцами диалога и боя.
Проверка в игре, NVDA, процедурных границ и повторного ввода обязательна отдельно.
