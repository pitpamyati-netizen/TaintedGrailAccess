# Соответствие приложенному Graph A11y Draft 1

Нормативный источник: [неизменённый пользовательский файл](spec/graph-a11y-spec.original.md).
Дата внутри источника: 17 июля 2026. Описание происхождения RTAccess/WrathAccess
приведено самим дополнением; это не независимая сертификация данной реализации.

Обозначения: **код** — реализованная переносимая логика; **тест** — собственная
поведенческая проверка; **интеграция** — проверка с живой игрой.
Ни одна строка «код/тест» не означает, что соответствующая функция доступна в
запущенной Conquest. Точные результаты последнего прогона — в TEST_RESULTS.md.

## Аксиомы и порты

| Требование | Реализация / проверка | Граница |
| --- | --- | --- |
| A1: параллельное дерево | GraphBuilder/KeyGraph; TitleUiBindings | Меню использует независимое дерево; native selection только для текстового редактора |
| A2: immediate mode | Rerender, новый builder; TitleUiBindings/LiveViewSelection | World читается в callback и освобождается; view/delegates не сохраняются |
| A3: identity, не render reference | ControlId; reference/structural/backward survivor tests; CardInHand reference + Model.ID | Перестановка/удаление одинаковых карт проверены переносимо; первый бой пройден с NVDA; возврат в руку подтверждён пользователем |
| A4: announce once | Navigator.AnnounceLanding, differ/direct memory; speech/sound-failure tests | Игровые hooks не должны добавлять сторонние focus Speak |
| A5: оригинальные handlers | ARButton.Handle, MoveSlider, native text/loading/story; ApplyClick, MarkTarget, EndTurn и reward buttons | M2 принят; M3 подключён и собран, первый бой пройден с NVDA; расширенная приёмка M3 открыта |
| A6: видимость как у зрячего | Живые Model.Views, active/Canvas/CanvasGroup/TMP, top modal, Interactable | Меню, загрузка и начальные диалоги GOG 1.61; скрытые игровые данные не читаются |
| A7: provenance речи | Interrupt для прямого input, queue для differ/Live, отдельный private канал | NVDA принят для первого пути; другие backends не приняты |
| A8: локализация | Localizer ru/en, игровой текст, InputActionsToTextMapping и исследованные StatType icons | Неизвестные иконки обозначаются явно; игровые числа/описания не пересчитываются |
| P1–P9 | Подробная таблица в ARCHITECTURE.md | Особо P4: ownership model не заменяет физические hooks |

## Ядро: разделы 3–6

| Раздел | Что реализовано | Где проверяется |
| --- | --- | --- |
| 3.1 ControlId | Structural-only equality/hash; отдельное ReferenceEquals; structural/referenced/for-object | IdentityTests, ReconciliationTests |
| 3.2 Announcement | Живая Func<string>, kind, Live; пустые значения молчат | AnnouncementTests; Runtime Live cases |
| 3.3 ControlType | Тип — data, common parts, stable order, node override | AnnouncementTests |
| 3.4 Vtable | Primary/secondary/modified/tooltip/adjust/state/search/sounds/expand slots | OperationTests, TreeTests; Runtime |
| 3.5 Nodes/render | Directions+labels, parent/context, focusable, group/stop/region/position | BuilderTests |
| 3.6 GraphState | Cursor/order/suggestion/stop memory/expanded per screen | ReconciliationTests; Runtime stack cases |
| 4.1 Menu rows | Left/right, same-stop vertical, optional column preservation | BuilderTests |
| 4.2 Raw graph | AddNode/Connect; dangling/nonfocusable destinations dropped | BuilderTests |
| 4.3 Stops/regions | Stable auto/explicit keys; arrows не пересекают stops | BuilderTests, OperationTests |
| 4.4 Context/group | Нефокусируемые родители, вложенность, подавление subtree | BuilderTests, TreeTests |
| 4.5 Seam stitching | Menu/raw обе стороны; только missing edges; разрыв skip-chain | BuilderTests; hidden-scope Connect regression |
| 4.6 Positions | Row positions и siblings(parent,stop), raw none, parent suppression | BuilderTests, AnnouncementTests |
| 4.7 Validation | Duplicate IDs, empty announcements, незакрытые scopes; empty=null | BuilderTests |
| 5.0 Fresh operations | Move/edge/stop/region/focus/tree/action/search перестраивают graph | Mutation/reorder/action tests |
| 5.1 Total order | Down-right order, cycles guarded, остальные declared узлы appended | ReconciliationTests, OperationTests |
| 5.2 Reconciliation | Suggestion; reference; structural; прежний предшественник; selected/start | ReconciliationTests |
| 5.2 Selected | Selected metadata учитывает common и override; речь не определяет выбор | Selected-common regression, Runtime initial-stop case |
| 5.3 Move | Direction, no-move result, edge loop visited set | OperationTests |
| 5.3 Stops | Memory → selected → first; wrap/nonwrap | OperationTests |
| 5.3 Regions | Только текущий stop; first node целевого region | OperationTests |
| 5.3 Focus | Focus(id), FocusByReference(obj) | ReconciliationTests, OperationTests |
| 5.3 Trees | Expand/descend/collapse/ascend, empty auto-recollapse, sibling edges | TreeTests, cross-stop regressions |
| 5.3 Invokers | False для отсутствующего slot; original delegate для имеющегося | OperationTests |
| 6.1 Effective parts | Common merged/overridden; stable type order; kind filter | AnnouncementTests |
| 6.2 Path diff | Общий prefix по Id, outermost-first, label dedupe, transition label | AnnouncementTests |
| 6.3 Leaf readout | Живые parts, localized expansion/position, own flags | AnnouncementTests |

Ядро не содержит ссылок на Unity, BepInEx, Harmony, Win32 или speech output.
Сборка Graph отдельной DLL без этих ссылок проверяет эту границу практически.

## Политики: раздел 7

| Раздел | В переносимом коде | Осталось в игре |
| --- | --- | --- |
| 7.1 Keys | Arrows, adjust, tree edges, Tab, Home/End, regions, actions, tooltip, Back, search | Raw capture и OS key repeat; пользовательский remapping |
| 7.2 Differ | Queued screen title/automatic landing; direct interrupt updates shared memory | Не создавать дополнительный focus narrator в patches |
| 7.3 Consume/bubble | No focus passes actions; optional exploration edges; contextual first | Physical per-chord gating во всех input paths |
| 7.4 StateText | Сравнение до/после, immediate speech, live rebaseline | Действительные state providers с отказом stale targets |
| 7.5 Live watch | Focus baseline; changed part queue; stable part keys | Текст должен отражать видимый текущий state |
| 7.6 Unfocused/blur | Tab seats; nonwrapped край возвращает world; cleared cursor/differ | Исследование/движение и его commands |
| 7.7 Search | Stop scope, rich text stripping, exclude, live results, arrows/edges/Escape | Полное определение native text fields, layout/IME/raw capture |
| 7.8 Build isolation | FaultBoundary по screen+exception type; state survives empty | Верные IsActive и UI lifetime guards |

Дополнительные регрессии защищают от двойного действия после исключения,
повторного Enter при удержании, HoverSound failure после уже сказанного фокуса,
перехода в modal/raw screen тем же нажатием и потери selection из-за verbosity.

## Четыре пути ввода: раздел 8

| Путь | Статус 0.3.4 |
| --- | --- |
| Poller мода | Early Update и prefixes TitleScreen, BaseScene, GameUI, EventSystem, loading; once/frame, held/release/blur; приоритет живой загрузки |
| Global keymap/Rewired | Живые enabled keyboard maps; suppress только присвоенных источников. UINaviAction использует фактические Horizontal/Vertical |
| UI focus/Submit | Own-action scope; Focus/ARButton/Unity и боевые Submit guards, живой OnPress; отдельные игровые сочетания не блокируются общим наличием Enter |
| Exclusive modal | Только собственная справка Ctrl+F1. GameUI, Rewired, selected-object keyboard gate; Escape возвращает сохранённый cursor |

Меню, физический повтор, Alt+Tab, справка, rebind/cancel и возврат проверены
в игре с NVDA; native text capture и набор/удаление также подтверждены.
Речевой просмотр и редактирование текста приняты с NVDA; сценарии —
M1_VERIFICATION.md и M2_VERIFICATION.md. Вход в первый бой принят отдельно.
Семнадцать переносимых проверок M3 и BindingChecks описаны в M3_VERIFICATION.md;
они не заменяют несколько боёв с NVDA и проверку всех классовых вариантов.
В 0.3.3 BindingChecks проверяет связь всех восьми значков ресурсов результата
с игровыми названиями; пользователь за егеря подтвердил названия и получение
доступной награды с NVDA. Полный M3 этим отдельным путём не подтверждён.
0.3.4 добавляет видимое описание при пустом tooltip без запроса скрытого,
различение повторяющихся действий награды и вложенный просмотр классовой
колоды. Видимые пороги и уровни читаются из UI; никакие классовые значения
не рассчитываются модом. Общий контракт девяти классов проверен по metadata,
новые пути не проверены в игре/NVDA. Gate M3 остаётся открытым.
Raw/text отключают навигацию и поиск. Unknown chords вне собственной modal
проходят игре; KeyUp не мешает штатной очистке held state. Exit/disable/failure
освобождают ownership; Dispose снимает только собственные Harmony patches.
Полное muted input допускается только для mod-owned modal.

## Экраны: раздел 9

Реализованы IsActive polling, parent/layer, lifecycle, state отдельного экрана,
top-root/deepest-child priority, сохранение покрытого cursor, reset popped screen,
KeepStateOnPop, StartUnfocused, Exclusive, CapturesRawInput, InitialFocusStop,
ScreenName. Title произносится в очереди перед landing.

Проверяются покрытие/возврат, pop/reset/keep, inactive parent, смена контекста
во время действия и Build exceptions. Реальные screen predicates игры не входят
в синтетические тесты и должны пройти ручные сценарии.

## Явные решения для неоднозначных деталей Draft 1

- Неявно созданная группа сначала свёрнута.
- На границе raw → menu ищется последний raw node без Down; существующие
  связи не переписываются. Полностью подавленная группа не добавляет даже Connect.
- Поиск — вхождение подстроки с OrdinalIgnoreCase, в том числе кириллица;
  варианты е/ё и полноценный IME не обещаны.
- Полностью пустой render сохраняет state, включая suggestion до следующего
  пригодного render. Невалидный suggestion в пригодном render поглощается один раз.
- Rerender(false) поддерживает намеренно неусаженный cursor; обычные kernel
  операции всё равно выполняют reconcile.
- Home/End в одноосной горизонтальной строке используют Left/Right; в обычном
  меню Up/Down; в дереве first/last sibling. Специальным grids нужен screen design.
- Английские defaults компоновщика пригодны для автономных тестов; Runtime
  обязательно подставляет локализованные expansion/position strings.

## Что нельзя писать о соответствии

Комплект тестов создан для этого проекта по дополнению. Оригинальная suite
RTAccess/WrathAccess, упомянутая как примерно сто тестов, не была приложена и
не запускалась. Правильная формулировка: «реализовано по Graph A11y Draft 1,
переносимая логика проверена собственными поведенческими тестами».
Полное соответствие игрового мода требует P1–P5 и in-game acceptance.
