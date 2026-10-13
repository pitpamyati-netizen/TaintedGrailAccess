using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>Exact GOG 1.61 combat recipes; metadata is persistent, game objects are callback-scoped.</summary>
    internal sealed partial class TitleUiBindings
    {
        private readonly Dictionary<string, MethodInfo> _combatMethods = new Dictionary<string, MethodInfo>();
        private readonly CombatLogCursor _combatLog = new CombatLogCursor();
        private static readonly string[] CombatScreens = { "combat", "target", "choose", "retain", "cardSpec", "runeDeck", "result", "combatMenu", "combatTutorial" };
        private object _availableSkill;
        private object _hiddenSkill;
        private object _cancelEvent;
        private string _combatSession;
        private string _combatTurn;
        private FieldInfo[] _combatSpriteFields;

        private void InitializeCombat(Assembly assembly)
        {
            CombatPath(assembly, "combat", "Awaken.TG.Main.Fights.FightBoard", "Awaken.TG.Main.Fights._3D.V3DFightBoardUI");
            CombatPath(assembly, "combatTutorial", "Awaken.TG.Main.Fights.FightBoard", "Awaken.TG.Main.Fights._3D.V3DFightBoardUI");
            CombatPath(assembly, "hand", "Awaken.TG.Main.Heroes.Fights.Deck.HeroPiles", "Awaken.TG.Main.Heroes.Fights.Deck.VHeroPiles");
            CombatPath(assembly, "combatCard", "Awaken.TG.Main.Heroes.Fights.Deck.CardInHand", "Awaken.TG.Main.Heroes.Fights.Deck.VCardInHand");
            CombatPath(assembly, "ultimate", "Awaken.TG.Main.Heroes.Fights.Deck.RuneUI", "Awaken.TG.Main.Heroes.Fights.Deck.VRuneSkill");
            CombatPath(assembly, "target", "Awaken.TG.Main.Heroes.Fights.FightTargeting", "Awaken.TG.Main.Heroes.Fights.VFightTargeting");
            CombatPath(assembly, "retain", "Awaken.TG.Main.Heroes.Fights.Deck.CardToStaySelector", "Awaken.TG.Main.Heroes.Fights.Deck.VCardToStaySelector");
            CombatPath(assembly, "choose", "Awaken.TG.Main.UI.ChooseWindows.ChooseWindow", "Awaken.TG.Main.UI.ChooseWindows.VChooseWindow");
            CombatPath(assembly, "combatChoice", "Awaken.TG.Main.UI.ChooseWindows.Option", "Awaken.TG.Main.UI.ChooseWindows.VOption");
            CombatPath(assembly, "cardSpec", "Awaken.TG.Main.Heroes.Fights.Deck.CardSpecPages.CardSpecPage", "Awaken.TG.Main.Heroes.Fights.Deck.CardSpecPages.VCardSpecPage");
            CombatPath(assembly, "runeDeck", "Awaken.TG.Main.Heroes.Skills.Runes.RuneDecks.RuneDeckUI", "Awaken.TG.Main.Heroes.Skills.Runes.RuneDecks.VRuneDeckUI");
            CombatPath(assembly, "result", "Awaken.TG.Main.Fights.Rewards.EndPanels.FightEndPanel", "Awaken.TG.Main.Fights.Rewards.EndPanels.VFightEndPanel");
            CombatPath(assembly, "combatMenu", "Awaken.TG.Main.UI.Menu.MenuUI", "Awaken.TG.Main.UI.Menu.VMenuUI");
            CombatPath(assembly, "battleLog", "Awaken.TG.Main.Fights.BattleLogs.BattleLog", "Awaken.TG.Main.Fights.BattleLogs.VBattleLog");
            CombatPath(assembly, "combatItem", "Awaken.TG.Main.Heroes.CombatItems.CombatItem", "Awaken.TG.Main.Heroes.CombatItems.VFightCombatItem");
            CombatPath(assembly, "npc", "Awaken.TG.Main.Fights.NpcFighters.NpcFighter", "Awaken.TG.Main.Fights._3D.V3DNpcFighter");
            CombatPath(assembly, "intent", "Awaken.TG.Main.Fights.NpcFighters.Intents.Intent", "Awaken.TG.Main.Fights.NpcFighters.Intents.VIntent");
            foreach (var pair in new[] {
                new[] { "fighter", "Awaken.TG.Main.Heroes.Fights.HeroFighter" },
                new[] { "character", "Awaken.TG.Main.Character.ICharacter" },
                new[] { "skill", "Awaken.TG.Main.Heroes.Skills.Skill" },
                new[] { "availability", "Awaken.TG.Main.Heroes.Skills.SkillAvailability" },
                new[] { "preview", "Awaken.TG.Main.Journal.UI.ViewComponents.VCCardPreview" },
                new[] { "withTooltip", "Awaken.TG.MVC.UI.Handlers.Tooltips.IWithTooltip" },
                new[] { "tooltip", "Awaken.TG.MVC.UI.Handlers.Tooltips.TooltipConstructor" },
                new[] { "statDisplay", "Awaken.TG.Main.Fights.UI.VCStatDisplay" },
                new[] { "rangeDisplay", "Awaken.TG.Main.Fights.UI.VCRangeStatDisplay" },
                new[] { "barrierDisplay", "Awaken.TG.Main.Fights.UI.VCBarrierDisplay" },
                new[] { "statType", "Awaken.TG.Main.Heroes.Stats.StatType" },
                new[] { "characterStats", "Awaken.TG.Main.Character.Stats" },
                new[] { "runeStats", "Awaken.TG.Main.Heroes.Skills.Runes.RuneStat" },
                new[] { "realmStats", "Awaken.TG.Main.Realms.Realm+Stats" },
                new[] { "item", "Awaken.TG.Main.Heroes.Items.Item" },
                new[] { "richReference", "Awaken.TG.Main.Utility.RichEnumReference.RichEnumReference" },
                new[] { "npcUi", "Awaken.TG.Main.Fights._3D.V3DNpcFighterUI" },
                new[] { "fighterHud", "Awaken.TG.Main.Fights._3D.V3DFighterHUD" },
                new[] { "fightBlocker", "Awaken.TG.Graphics.Cutscenes.Fights.Sequences.VFightModalBlocker" },
                new[] { "modalBlocker", "Awaken.TG.MVC.UI.Universal.VModalBlocker" },
                new[] { "loggedMessage", "Awaken.TG.Main.Fights.BattleLogs.LoggedMessage" },
                new[] { "summaryView", "Awaken.TG.Main.Fights.Rewards.EndPanels.Stats.VFightEndVictorySummary" },
                new[] { "lootView", "Awaken.TG.Main.Fights.Rewards.EndPanels.Items.VFightEndItemsLoot" },
                new[] { "orbCombineView", "Awaken.TG.Main.Fights.Rewards.EndPanels.VCombineOrbsUI" },
                new[] { "orbView", "Awaken.TG.Main.Fights.Rewards.EndPanels.VPossibleOrbCombine" },
                new[] { "rewardItemView", "Awaken.TG.Main.Fights.Rewards.EndPanels.Items.VRewardedItem" },
                new[] { "victoryView", "Awaken.TG.Main.Fights.Rewards.EndPanels.DefeatPanels.VFightEndVictoryLogo" },
                new[] { "defeatView", "Awaken.TG.Main.Fights.Rewards.EndPanels.DefeatPanels.VFightEndDefeatPanel" },
                new[] { "cardSpecUi", "Awaken.TG.Main.Heroes.Fights.Deck.CardSpecPages.VCardSpecPageUI" },
                new[] { "combatKey", "Awaken.TG.MVC.UI.Events.UIKeyDownAction" },
                new[] { "statusSource", "Awaken.TG.Main.Fights.UI.IStatusSource" } }) AddType(assembly, pair[0], pair[1]);
            AddType(assembly, "blockView", "Awaken.TG.Main.Fights.Modifiers.Views.VBlock");
            _availableSkill = _types["availability"].GetField("Available").GetValue(null);
            _hiddenSkill = _types["availability"].GetField("Hidden").GetValue(null);
            _cancelEvent = Activator.CreateInstance(assembly.GetType("Awaken.TG.MVC.UI.Events.UICancelAction", true));
            foreach (var name in new[] { "IsFightStarted", "IsFightOver", "CurrentRoundNumber", "Hero", "Result" }) Prop("combat", name);
            Prop("character", "Fighter"); Prop("fighter", "IsExecutingTurn", typeof(bool)); Prop("fighter", "Board");
            foreach (var name in new[] { "Skill", "Board", "IsDissolving" }) Prop("combatCard", name);
            foreach (var name in new[] { "DisplayName", "Availability", "CardTooltip" }) Prop("skill", name);
            foreach (var name in new[] { "Skill", "Fighter", "Board", "Rune" }) Prop("ultimate", name);
            Prop("hand", "FightBoard"); Prop("hand", "CardsInHand"); Prop("retain", "SelectionEnabled", typeof(bool)); Prop("retain", "SelectedCard");
            foreach (var name in new[] { "PossibleTargets", "Skill" }) Prop("target", name);
            foreach (var name in new[] { "Name", "IsAlive", "IsAlly", "IsEnemy", "Board", "Tooltip", "AdditionalTooltip" }) Prop("npc", name);
            Prop("intent", "NpcFighter");
            foreach (var name in new[] { "Options", "Selected", "Configuration" }) Prop("choose", name);
            AddType(assembly, "chooseSetup", _properties["choose.Configuration"].PropertyType.FullName);
            foreach (var name in new[] { "minSelection", "maxSelection", "onlyDisplay", "closeable" }) Field("chooseSetup", name);
            Prop("combatChoice", "Selected", typeof(bool)); Prop("combatChoice", "ParentModel");
            Prop("withTooltip", "TooltipConstructor"); Prop("tooltip", "Objects"); Prop("tooltip", "SubTooltip");
            Prop("richReference", "Enum"); Prop("statType", "DisplayName"); Prop("statType", "Abbreviation", typeof(string));
            Prop("item", "Quantity", typeof(int));
            _combatSpriteFields = new[] { _types["characterStats"].GetField("Health"), _types["characterStats"].GetField("CombatPoints"),
                _types["characterStats"].GetField("XP"), _types["runeStats"].GetField("Charges"),
                _types["realmStats"].GetField("Wealth"), _types["realmStats"].GetField("WyrdShards"),
                _types["realmStats"].GetField("RunestoneDust"), _types["realmStats"].GetField("Blood"),
                _types["realmStats"].GetField("Tallow"), _types["realmStats"].GetField("CardShreds"),
                _types["realmStats"].GetField("GoldenTokens"), _types["realmStats"].GetField("JournalPages") };
            if (_combatSpriteFields.Any(f => f == null || !f.IsStatic || !_types["statType"].IsAssignableFrom(f.FieldType))) throw new MissingFieldException("Combat stat sprite metadata");
            Prop("combatItem", "Item"); Prop("combatItemView", "CanBeUsed", typeof(bool));
            Prop("cardSpec", "Host");
            foreach (var name in new[] { "text", "nameText", "statType" }) Field("statDisplay", name);
            Field("barrierDisplay", "text", typeof(TextMeshProUGUI));
            Field("rangeDisplay", "text", typeof(TextMeshProUGUI)); Field("rangeDisplay", "minStat");
            Field("button", "OnPress", typeof(Action));
            Prop("ultimateView", "RuneSubTooltip");
            Prop("mouseDown", "Button", typeof(int));
            foreach (var name in new[] { "previousArrow", "nextArrow" }) Field("cardSpecUi", name, _types["button"]);
            Field("summaryView", "continueButton", _types["button"]); Field("lootView", "takeAllButton", _types["button"]);
            foreach (var name in new[] { "leftArrow", "rightArrow", "continueButton" }) Field("orbCombineView", name, _types["button"]);
            Field("orbView", "fuseButton", _types["button"]);
            Field("combatView", "viewCanvas", typeof(CanvasGroup));
            Field("combatView", "board", typeof(CanvasGroup));
            Field("combatView", "druidTutorialIconButton", _types["button"]); Field("combatView", "closeDruidTutorialButton", _types["button"]);
            foreach (var name in new[] { "textComponent", "cooldown" }) Field("intentView", name, typeof(TextMeshProUGUI));
            Prop("statusSource", "Character");
            foreach (var name in new[] { "skillTitle", "skillOnPlayDesc", "cardTypeText", "energyCostText", "runeChargesCostText", "flipStatesText" }) Field("combatCardView", name, typeof(TextMeshProUGUI));
            Field("combatCardView", "button", _types["button"]);
            foreach (var name in new[] { "skillTitle", "skillOnPlayDesc", "cardTypeText", "skillCostText", "runeChargesCostText", "flipStatesText" }) Field("preview", name, typeof(TextMeshProUGUI));
            Field("preview", "_knownDescription", typeof(bool)); Field("preview", "_skill", _types["skill"]);
            foreach (var name in new[] { "combatPointsText", "deckCards", "discardedCards", "graveyardCards", "exileCars" }) Field("handView", name, typeof(TextMeshProUGUI));
            foreach (var name in new[] { "endTurnButton", "showDeckButton", "showDiscardButton", "showGraveButton", "showExileButton" }) Field("handView", name, _types["button"]);
            Field("ultimateView", "charges", typeof(TextMeshProUGUI)); Field("ultimateView", "specialText", typeof(TextMeshProUGUI)); Field("ultimateView", "button", _types["button"]);
            foreach (var name in new[] { "acceptButton", "skipButton", "rerollButton" }) Field("chooseView", name, _types["button"]);
            Field("chooseView", "decorsParent", typeof(Transform)); Field("combatChoiceView", "optionButton", _types["button"]); Field("combatChoiceView", "content", typeof(Transform));
            Field("chooseView", "wealth");
            Field("runeDeckView", "cardsParent", typeof(Transform)); Field("runeDeckView", "closeButton", _types["button"]);
            Field("battleLogView", "loggedMessages"); Field("loggedMessage", "log", typeof(string)); Field("loggedMessage", "roundNumber", typeof(int));
            foreach (var name in new[] { "resumeButton", "saveButton", "loadButton", "optionsButton", "bugReportButton", "saveAndQuit", "quitToMenu", "quitGame" }) Field("combatMenuView", name, _types["button"]);
            var evt = assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true);
            foreach (var pair in new[] { new[] { "combatCardView", "ApplyClick" }, new[] { "ultimateView", "ApplyClick" },
                new[] { "handView", "EndTurn" }, new[] { "choose", "Close" }, new[] { "cardSpec", "Close" }, new[] { "combatMenu", "Close" } })
                _combatMethods.Add(pair[0] + "." + pair[1], RequireMethod(_types[pair[0]], pair[1]));
            foreach (var kind in new[] { "targetView", "cardSpecView", "combatCardView", "ultimateView", "preview", "combatItemView", "runeDeckView", "summaryView" })
                _combatMethods.Add(kind + ".Handle", RequireMethod(_types[kind], "Handle", evt));
            _combatMethods.Add("skill.IsValidTarget", RequireMethod(_types["skill"], "IsValidTarget", _types["character"]));
            _combatMethods.Add("fighter.FindTargetWithResult", RequireMethod(_types["fighter"], "FindTargetWithResult", _types["skill"], _types["button"], typeof(RectTransform)));
            _combatMethods.Add("target.MarkTarget", RequireMethod(_types["target"], "MarkTarget", assembly.GetType("Awaken.TG.MVC.UI.Handlers.Targets.ITargetable", true),
                assembly.GetType("Awaken.TG.MVC.UI.UIPosition", true), typeof(EventModifiers)));
            var available = _types["availability"];
            foreach (var name in new[] { "OnCooldown", "TooExpensive", "Inactive" })
                _combatAvailability.Add(available.GetField(name).GetValue(null), "combat.reason." + name);
        }

        private readonly Dictionary<object, string> _combatAvailability = new Dictionary<object, string>();
        private void CombatPath(Assembly assembly, string kind, string model, string view)
        {
            AddPath(assembly, kind, model, view);
            var all = assembly.GetType("Awaken.TG.MVC.World", true).GetMethods().Single(m => m.Name == "All" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            _all.Add(kind, all.MakeGenericMethod(_types[kind]));
        }

        private bool CombatActive(string kind)
        {
            if (kind == "combatTutorial") return View("combat") != null && Visible((Component)ReadField("combatView", "viewCanvas", View("combat")));
            if (kind == "cardSpec") return All("cardSpec").Any(model => ReadProp("cardSpec", "Host", model) == null && ModelLive(model) &&
                (ViewOf(model, _types["cardSpecView"]) != null || ViewOf(model, _types["cardSpecUi"]) != null));
            if (kind == "combat") return View(kind) != null && Visible((Component)ReadField("combatView", "board", View(kind))) &&
                (bool)ReadProp(kind, "IsFightStarted", Find(kind)) && !(bool)ReadProp(kind, "IsFightOver", Find(kind));
            if (kind == "retain") return View(kind) != null && (bool)ReadProp(kind, "SelectionEnabled", Find(kind));
            return View(kind) != null;
        }

        private bool CombatCanAct(string kind)
        {
            if (World == null || _actionScreen != kind || HelpOpen || Capturing() || !CombatActive(kind)) return false;
            if (View("settings") != null || View("popup") != null || View("bug") != null || StoryView() != null) return false;
            if (kind != "combatMenu" && View("combatMenu") != null) return false;
            var top = TopLayerModel();
            if (top != null && !ReferenceEquals(top, Find(kind)))
            {
                // A modal blocker on a card/skill is an in-progress action, not a new combat screen.
                if (kind == "result" && KnownCombatOwner(top)) return true;
                if (kind != "combat" && kind != "retain" && kind != "target" && kind != "combatTutorial") return false;
                if (!ReferenceEquals(top, Find("combat")) && !(_types["fighter"].IsInstanceOfType(top) && ReferenceEquals(ReadProp("fighter", "Board", top), Find("combat")))) return false;
            }
            if (kind != "combat") return true;
            var board = Find("combat");
            var hero = ReadProp("combat", "Hero", board);
            var fighter = ReadProp("character", "Fighter", hero);
            return fighter != null && (bool)ReadProp("fighter", "IsExecutingTurn", fighter) &&
                !CombatActive("target") && !CombatActive("retain") && !CombatActive("choose") && !CombatActive("cardSpec") && !CombatActive("runeDeck") && !CombatActive("combatTutorial");
        }

        private IEnumerable<Component> CombatViews()
        {
            if (World == null) yield break;
            foreach (var model in (IEnumerable)_orderedModels.Invoke(World, null))
            {
                if (!ModelLive(model)) continue;
                foreach (var view in (IEnumerable)ReadProp("model", "Views", model))
                    if (Visible(view as Component)) yield return (Component)view;
            }
        }
        private bool ModelLive(object model) => model != null && (bool)ReadProp("model", "IsFullyInitialized", model) &&
            !(bool)ReadProp("model", "WasDiscarded", model) && !(bool)ReadProp("model", "IsBeingDiscarded", model);
        private bool KnownCombatOwner(object model)
        {
            return _types["fighter"].IsInstanceOfType(model) && Find("combat") != null && ReferenceEquals(ReadProp("fighter", "Board", model), Find("combat")) ||
                Find("result") != null && CombatViews().Any(v => ReferenceEquals(ReadProp("view", "GenericTarget", v), model) &&
                    (_types["summaryView"].IsInstanceOfType(v) || _types["lootView"].IsInstanceOfType(v) || _types["orbCombineView"].IsInstanceOfType(v)));
        }
        private string InstanceKey(string prefix, object model) => prefix + "." + (string)ReadProp("model", "ID", model);
        private string ShownText(string kind, string field, Component owner)
        {
            if (owner == null) return null;
            var text = ReadField(kind, field, owner) as TMP_Text;
            return Visible(text) ? text.text : null;
        }
        private static string JoinText(IEnumerable<string> parts) => string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        private string TooltipText(Component view) => TooltipText(view, true);
        private string TooltipText(Component view, bool includeDetails)
        {
            if (!Visible(view) || !_types["withTooltip"].IsInstanceOfType(view)) return null;
            return ConstructorText(ReadProp("withTooltip", "TooltipConstructor", view), includeDetails: includeDetails);
        }
        private string ConstructorText(object constructor, int depth = 0, bool includeDetails = true)
        {
            if (constructor == null || depth > 2) return null;
            // The tooltip's non-string values are templates/icons, never hidden card data.
            return JoinText(((IEnumerable)ReadProp("tooltip", "Objects", constructor)).OfType<string>()
                .Concat(new[] { includeDetails ? ConstructorText(ReadProp("tooltip", "SubTooltip", constructor), depth + 1) : null }));
        }
        private Action ReadDetail(Func<string> text) => () => { var value = text(); _speech?.Speak(string.IsNullOrWhiteSpace(value) ? _text.Get("action.noTooltip") : value, true); };
        private bool SkillShown(object skill) => skill != null && !ReferenceEquals(ReadProp("skill", "Availability", skill), _hiddenSkill);
        private string SkillReason(object skill)
        {
            var availability = ReadProp("skill", "Availability", skill);
            string key;
            return ReferenceEquals(availability, _availableSkill) ? null : _text.Get(_combatAvailability.TryGetValue(availability, out key) ? key : "state.disabled");
        }
        private bool SkillAvailable(object skill) => skill != null && ReferenceEquals(ReadProp("skill", "Availability", skill), _availableSkill);
        private string CombatSpriteLabel(string name)
        {
            if (_combatSpriteFields == null) return null;
            foreach (var field in _combatSpriteFields)
            {
                var stat = field.GetValue(null);
                if ((string)ReadProp("statType", "Abbreviation", stat) == name) return ReadProp("statType", "DisplayName", stat).ToString();
            }
            return null;
        }

        private bool CombatInfoInput(string kind, KeyChord chord)
        {
            if (kind != "combat" && kind != "target" && kind != "choose" && kind != "retain" && kind != "cardSpec" && kind != "runeDeck") return false;
            if (_actionScreen != kind || HelpOpen || Capturing() || !CombatActive("combat")) return false;
            return CombatReadoutKeys.Handle(chord, CombatInfoText, _speech, _text.Get("combat.info.unavailable"));
        }

        private IEnumerable<Component> FighterStatRoots(object fighter)
        {
            if (!ModelLive(fighter)) return Enumerable.Empty<Component>();
            return ((IEnumerable)ReadProp("model", "Views", fighter)).Cast<Component>().Where(v => Visible(v) &&
                (_types["npcUi"].IsInstanceOfType(v) || _types["fighterHud"].IsInstanceOfType(v)));
        }

        private IEnumerable<Component> FighterEffectViews(object character)
        {
            return CombatViews().Where(v =>
            {
                if (!_types["withTooltip"].IsInstanceOfType(v)) return false;
                var model = ReadProp("view", "GenericTarget", v);
                return _types["statusSource"].IsInstanceOfType(model) && ReferenceEquals(ReadProp("statusSource", "Character", model), character);
            });
        }

        private object[] CurrentHand() => All("combatCard").Where(c => ReferenceEquals(ReadProp("combatCard", "Board", c), Find("combat")) &&
            !(bool)ReadProp("combatCard", "IsDissolving", c) && ViewOf(c, _types["combatCardView"]) != null && SkillShown(ReadProp("combatCard", "Skill", c))).ToArray();

        private string CombatInfoText(CombatReadout readout)
        {
            var board = Find("combat");
            var hero = ReadProp("combat", "Hero", board);
            var fighter = ReadProp("character", "Fighter", hero);
            switch (readout)
            {
                case CombatReadout.Health:
                    var health = _combatSpriteFields.First(f => f.Name == "Health").GetValue(null);
                    return JoinText(FighterStatRoots(fighter).SelectMany(root => root.GetComponentsInChildren(_types["statDisplay"], false).Cast<Component>())
                        .Where(Visible).Where(stat => ReferenceEquals(ReadProp("richReference", "Enum", ReadField("statDisplay", "statType", stat)), health))
                        .Select(stat => ReadProp("statType", "DisplayName", health) + ": " + ShownText("statDisplay", "text", stat)));
                case CombatReadout.Energy:
                    var energy = ShownText("handView", "combatPointsText", View("hand"));
                    return string.IsNullOrWhiteSpace(energy) ? null : _text.Get("combat.energy") + ": " + energy;
                case CombatReadout.Barrier:
                    var protection = FighterStatRoots(fighter).SelectMany(root => root.GetComponentsInChildren(_types["barrierDisplay"], false).Cast<Component>())
                        .Where(Visible).Select(v => _text.Get("combat.barrier") + ": " + ShownText("barrierDisplay", "text", v))
                        .Concat(FighterEffectViews(hero).Where(v => _types["blockView"].IsInstanceOfType(v)).Select(TooltipText));
                    var barrier = JoinText(protection);
                    return string.IsNullOrWhiteSpace(barrier) ? _text.Get("combat.info.noProtectionShown") : barrier;
                case CombatReadout.ClassResource:
                    return JoinText(All("ultimate").Where(u => ReferenceEquals(ReadProp("ultimate", "Board", u), board)).Select(u =>
                    {
                        var view = ViewOf(u, _types["ultimateView"]);
                        var skill = ReadProp("ultimate", "Skill", u);
                        return view == null || !SkillShown(skill) ? null : JoinText(new[] { (string)ReadProp("skill", "DisplayName", skill),
                            ShownText("ultimateView", "charges", view), ShownText("ultimateView", "specialText", view) });
                    }));
                case CombatReadout.Effects:
                    var effects = JoinText(FighterEffectViews(hero).Select(TooltipText));
                    return string.IsNullOrWhiteSpace(effects) ? _text.Get("combat.info.noEffectsShown") : effects;
                case CombatReadout.Intents:
                case CombatReadout.ShortIntents:
                    var enemies = All("npc").Where(n => ReferenceEquals(ReadProp("npc", "Board", n), board) && (bool)ReadProp("npc", "IsEnemy", n) && FighterShown(n)).ToArray();
                    return enemies.Length == 0 ? _text.Get("combat.info.noEnemiesShown") : JoinText(enemies.Select(n =>
                    {
                        var intentions = JoinText(All("intent").Where(i => ReferenceEquals(ReadProp("intent", "NpcFighter", i), n)).Select(i =>
                        {
                            var view = ViewOf(i, _types["intentView"]);
                            var countdown = ShownText("intentView", "cooldown", view);
                            return view == null ? null : JoinText(new[] { ShownText("intentView", "textComponent", view),
                                string.IsNullOrWhiteSpace(countdown) ? null : _text.Get("combat.info.intentCountdown", countdown),
                                TooltipText(view, readout == CombatReadout.Intents) });
                        }));
                        return (string)ReadProp("npc", "Name", n) + ": " + (string.IsNullOrWhiteSpace(intentions) ? _text.Get("combat.info.noIntentShown") : intentions);
                    }));
                case CombatReadout.Hand:
                    var cards = CurrentHand();
                    return _text.Get("combat.info.handCount", cards.Length) + (cards.Length == 0 ? "" : ": " + JoinText(cards.Select(c =>
                    {
                        var view = ViewOf(c, _types["combatCardView"]);
                        return JoinText(new[] { ShownText("combatCardView", "skillTitle", view), ShownText("combatCardView", "energyCostText", view),
                            ShownText("combatCardView", "runeChargesCostText", view), SideText("combatCardView", view) });
                    })));
                default: return null;
            }
        }

        private bool BuildCombat(string kind, GraphBuilder builder)
        {
            if (!CombatScreens.Contains(kind)) return false;
            if (kind == "combatTutorial")
            {
                builder.Add(ControlId.Structural("combat.tutorial.help"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.tutorial.help"))));
                AddButton(builder, kind, (Component)ReadField("combatView", "closeDruidTutorialButton", View("combat")), ControlId.Structural("combat.tutorial.close"), "combatTutorial.close");
                return true;
            }
            if (kind == "combatMenu") { Buttons(builder, kind, View(kind), new[] { "resumeButton", "saveButton", "loadButton", "optionsButton", "bugReportButton", "saveAndQuit", "quitToMenu", "quitGame" }); return true; }
            if (kind == "choose") { BuildCombatChoice(builder); return true; }
            if (kind == "target") { BuildCombatTargets(builder); return true; }
            if (kind == "cardSpec" || kind == "runeDeck") { BuildCombatDetails(kind, builder); return true; }
            if (kind == "result") { BuildCombatResult(builder); return true; }
            if (kind == "retain") { BuildCombatHand(kind, builder); AddEndTurn(builder, kind); return true; }
            Zone(builder, "status");
            builder.Add(ControlId.Structural("combat.turn"), new NodeVtable(new NodeAnnouncement(TurnText)));
            var board = Find("combat");
            var hero = ReadProp("combat", "Hero", board);
            var fighter = ReadProp("character", "Fighter", hero);
            AddFighterStats(builder, fighter, _text.Get("combat.hero"));
            builder.PopContext();
            Zone(builder, "hand"); BuildCombatHand(kind, builder); builder.PopContext();
            foreach (var ally in new[] { false, true })
            {
                Zone(builder, ally ? "allies" : "enemies");
                var fighters = All("npc").Where(n => ReferenceEquals(ReadProp("npc", "Board", n), board) &&
                    (bool)ReadProp("npc", ally ? "IsAlly" : "IsEnemy", n) && FighterShown(n)).ToArray();
                foreach (var npc in fighters) AddFighterStats(builder, npc, (string)ReadProp("npc", "Name", npc));
                if (fighters.Length == 0) EmptyNode(builder, ally ? "allies" : "enemies");
                builder.PopContext();
            }
            Zone(builder, "resources");
            var piles = View("hand");
            if (piles != null)
                foreach (var pair in new[] { new[] { "combatPointsText", "energy" }, new[] { "deckCards", "deck" }, new[] { "discardedCards", "discard" }, new[] { "graveyardCards", "grave" }, new[] { "exileCars", "exile" } })
                {
                    var field = pair[0]; var label = pair[1];
                    builder.Add(ControlId.Structural("combat.resource." + label), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat." + label)),
                        new NodeAnnouncement(() => ShownText("handView", field, View("hand")), "value", true)));
                }
            foreach (var ultimate in All("ultimate").Where(u => ReferenceEquals(ReadProp("ultimate", "Board", u), board))) AddUltimate(builder, ultimate);
            AddCombatItems(builder); builder.PopContext();
            Zone(builder, "actions"); AddEndTurn(builder, kind);
            if (piles != null) foreach (var field in new[] { "showDeckButton", "showDiscardButton", "showGraveButton", "showExileButton" })
                AddButton(builder, kind, (Component)ReadField("handView", field, piles), ControlId.Structural("combat." + field), "combat." + field);
            AddButton(builder, kind, (Component)ReadField("combatView", "druidTutorialIconButton", View("combat")), ControlId.Structural("combat.classHelp"), "combat.classHelp");
            builder.PopContext();
            BuildCombatLog(builder);
            return true;
        }

        private void Zone(GraphBuilder builder, string key) { builder.BeginStop("combat." + key); builder.PushContext(_text.Get("combat.section." + key)); }
        private void EmptyNode(GraphBuilder builder, string key) => builder.Add(ControlId.Structural("combat.empty." + key), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.empty"))));
        private string TurnText()
        {
            var board = Find("combat"); if (board == null) return null;
            var fighter = ReadProp("character", "Fighter", ReadProp("combat", "Hero", board));
            return _text.Get("combat.round", ReadProp("combat", "CurrentRoundNumber", board)) + ", " +
                _text.Get(fighter != null && (bool)ReadProp("fighter", "IsExecutingTurn", fighter) ? "combat.yourTurn" : "combat.waitTurn");
        }

        private void BuildCombatHand(string kind, GraphBuilder builder)
        {
            var cards = CurrentHand();
            if (kind == "combat") CombatPresentation.KeepHandFocus(builder.State,
                cards.Select(c => ControlId.Referenced(c, InstanceKey("combat.card", c))).ToArray(), ControlId.Structural("combat.endTurn"));
            foreach (var card in cards)
            {
                var key = InstanceKey("combat.card", card);
                Func<Component> view = () => ViewOf(card, _types["combatCardView"]);
                Func<string> selection = () => kind == "retain" && ReferenceEquals(ReadProp("retain", "SelectedCard", Find("retain")), card) ? _text.Get("state.selected") : null;
                Func<string> value = () => JoinText(new[] { ShownText("combatCardView", "cardTypeText", view()), ShownText("combatCardView", "energyCostText", view()),
                    ShownText("combatCardView", "runeChargesCostText", view()), SideText("combatCardView", view()) });
                CombatPresentation.Add(builder, card, key, () => ShownText("combatCardView", "skillTitle", view()), value,
                    () => !CardHandlerReady(view()) ? _text.Get("combat.processing") : kind == "retain" ? null : SkillReason(ReadProp("combatCard", "Skill", card)), selection,
                    () => { var current = view(); var skill = ReadProp("combatCard", "Skill", card);
                        if (CombatCanAct(kind) && ModelLive(card) && Actionable(current) && CardHandlerReady(current) && !(bool)ReadProp("combatCard", "IsDissolving", card) &&
                            (kind == "retain" || SkillAvailable(skill))) Invoke(key, () => _combatMethods["combatCardView.ApplyClick"].Invoke(current, null)); },
                    ReadDetail(() => TooltipText(view()) ?? ShownText("combatCardView", "skillOnPlayDesc", view())),
                    () => OpenCardSpec(kind, view(), "combatCardView"), kind == "retain" ? selection : null);
            }
            if (cards.Length == 0) EmptyNode(builder, "hand");
            if (kind == "retain") builder.Add(ControlId.Structural("combat.retain.help"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.retain.help"))));
        }
        private string SideText(string kind, Component view)
        { var side = ShownText(kind, "flipStatesText", view); return string.IsNullOrWhiteSpace(side) ? null : _text.Get("combat.side", side); }
        private bool CardHandlerReady(Component view)
        {
            if (!Visible(view)) return false;
            var callback = ReadField("button", "OnPress", ReadField("combatCardView", "button", view)) as Action;
            return callback != null && callback.GetInvocationList().Any(d => ReferenceEquals(d.Target, view) && d.Method == _combatMethods["combatCardView.ApplyClick"]);
        }
        private void OpenCardSpec(string kind, Component view, string alias)
        {
            if (!CombatCanAct(kind) || !Visible(view)) return;
            Invoke("combat.cardDetails", () => _combatMethods[alias + ".Handle"].Invoke(view, new[] { GameKey("ToggleMap") }));
        }
        private object GameKey(string action)
        {
            var evt = Activator.CreateInstance(_types["combatKey"]);
            var data = Activator.CreateInstance(_storyInputData); _storyInputName.SetValue(data, action);
            _types["combatKey"].GetProperty("Data").SetValue(evt, data); return evt;
        }

        private bool FighterShown(object character)
        {
            if (!ModelLive(character)) return false;
            if (_types["npc"].IsInstanceOfType(character))
                return ReferenceEquals(ReadProp("npc", "Board", character), Find("combat")) && (bool)ReadProp("npc", "IsAlive", character) && ViewOf(character, _types["npcView"]) != null;
            return ReferenceEquals(character, ReadProp("combat", "Hero", Find("combat")));
        }
        private void AddFighterStats(GraphBuilder builder, object fighter, string label)
        {
            if (!ModelLive(fighter)) return;
            var views = ((IEnumerable)ReadProp("model", "Views", fighter)).Cast<Component>().Where(Visible).ToArray();
            var key = InstanceKey("combat.fighter", fighter);
            builder.Add(ControlId.Referenced(fighter, key), new NodeVtable(new NodeAnnouncement(() => label)) { OnTooltip = ReadDetail(() => FighterDetail(character: _types["fighter"].IsInstanceOfType(fighter) ? ReadProp("combat", "Hero", Find("combat")) : fighter)) });
            foreach (var root in views.Where(v => _types["npcUi"].IsInstanceOfType(v) || _types["fighterHud"].IsInstanceOfType(v)))
            {
                foreach (Component stat in root.GetComponentsInChildren(_types["statDisplay"], false).Where(c => Visible(c as Component)))
                {
                    var type = ReadProp("richReference", "Enum", ReadField("statDisplay", "statType", stat));
                    var current = stat;
                    CombatPresentation.Add(builder, current, key + ".stat." + current.GetInstanceID(), () => ReadProp("statType", "DisplayName", type).ToString(),
                        () => ShownText("statDisplay", "text", current), null, null, null, ReadDetail(() => TooltipText(current)));
                }
                foreach (Component barrier in root.GetComponentsInChildren(_types["barrierDisplay"], false).Where(c => Visible(c as Component)))
                {
                    var current = barrier;
                    CombatPresentation.Add(builder, current, key + ".barrier", () => _text.Get("combat.barrier"), () => ShownText("barrierDisplay", "text", current), null, null, null, ReadDetail(() => TooltipText(current)));
                }
                foreach (Component range in root.GetComponentsInChildren(_types["rangeDisplay"], false).Where(c => Visible(c as Component)))
                {
                    var current = range;
                    var type = ReadProp("richReference", "Enum", ReadField("rangeDisplay", "minStat", current));
                    CombatPresentation.Add(builder, current, key + ".range." + current.GetInstanceID(), () => ReadProp("statType", "DisplayName", type).ToString(),
                        () => ShownText("rangeDisplay", "text", current), null, null, null, null);
                }
            }
            // Status models may live below the hero rather than its fighter. Resolve each current view.
            var character = _types["fighter"].IsInstanceOfType(fighter) ? ReadProp("combat", "Hero", Find("combat")) : fighter;
            foreach (var view in CombatViews().Where(v => _types["withTooltip"].IsInstanceOfType(v)))
            {
                var model = ReadProp("view", "GenericTarget", view);
                if (!_types["statusSource"].IsInstanceOfType(model) || !ReferenceEquals(ReadProp("statusSource", "Character", model), character)) continue;
                var current = view;
                var detail = TooltipText(current); if (string.IsNullOrWhiteSpace(detail)) continue;
                CombatPresentation.Add(builder, model, key + ".effect." + current.GetInstanceID(), () => TooltipText(current), null, null, null, null, ReadDetail(() => TooltipText(current)));
            }
            foreach (var intent in All("intent").Where(i => ReferenceEquals(ReadProp("intent", "NpcFighter", i), character)))
            {
                var current = ViewOf(intent, _types["intentView"]); if (current == null) continue;
                CombatPresentation.Add(builder, intent, InstanceKey("combat.intent", intent), () => _text.Get("combat.intent"),
                    () => JoinText(new[] { ShownText("intentView", "textComponent", current), ShownText("intentView", "cooldown", current), TooltipText(current) }), null, null, null, ReadDetail(() => TooltipText(current)));
            }
        }

        private void AddUltimate(GraphBuilder builder, object ultimate)
        {
            Func<Component> view = () => ViewOf(ultimate, _types["ultimateView"]);
            var skill = ReadProp("ultimate", "Skill", ultimate); if (view() == null || !SkillShown(skill)) return;
            var key = InstanceKey("combat.ultimate", ultimate);
            CombatPresentation.Add(builder, ultimate, key, () => (string)ReadProp("skill", "DisplayName", ReadProp("ultimate", "Skill", ultimate)),
                () => JoinText(new[] { ShownText("ultimateView", "charges", view()), ShownText("ultimateView", "specialText", view()) }),
                () => SkillReason(ReadProp("ultimate", "Skill", ultimate)), null,
                () => { var current = view(); if (CombatCanAct("combat") && Actionable(current) && SkillAvailable(ReadProp("ultimate", "Skill", ultimate)))
                        Invoke(key, () => _combatMethods["ultimateView.ApplyClick"].Invoke(current, null)); },
                ReadDetail(() => JoinText(new[] { TooltipText(view()), ConstructorText(ReadProp("ultimateView", "RuneSubTooltip", view())) })),
                () => { var current = view(); if (!CombatCanAct("combat") || !Actionable(current)) return;
                    var evt = Activator.CreateInstance(_types["mouseDown"]); _properties["mouseDown.Button"].SetValue(evt, 1);
                    Invoke("combat.ultimateDetails", () => _combatMethods["ultimateView.Handle"].Invoke(current, new[] { evt })); });
        }
        private void AddCombatItems(GraphBuilder builder)
        {
            foreach (var item in All("combatItem"))
            {
                var current = ViewOf(item, _types["combatItemView"]); if (current == null || ReadProp("combatItem", "Item", item) == null) continue;
                CombatPresentation.Add(builder, item, InstanceKey("combat.item", item), () => TooltipText(current),
                    () => _text.Get("combat.quantity", ReadProp("item", "Quantity", ReadProp("combatItem", "Item", item))),
                    () => (bool)ReadProp("combatItemView", "CanBeUsed", current) ? null : _text.Get("state.disabled"), null,
                    () => { if (CombatCanAct("combat") && Actionable(current) && (bool)ReadProp("combatItemView", "CanBeUsed", current))
                            Invoke("combat.item", () => _combatMethods["combatItemView.Handle"].Invoke(current, new[] { _submit })); }, ReadDetail(() => TooltipText(current)));
            }
        }
        private void AddEndTurn(GraphBuilder builder, string kind)
        {
            var view = View("hand"); if (view == null) return;
            var button = (Component)ReadField("handView", "endTurnButton", view);
            CombatPresentation.Add(builder, Find("hand"), "combat.endTurn", () => _text.Get(kind == "retain" ? "combat.retain.confirm" : "combat.endTurn"), null,
                () => CanEndTurn(kind, button) ? null : _text.Get("combat.processing"), null,
                () => { var current = View("hand"); if (CanEndTurn(kind, (Component)ReadField("handView", "endTurnButton", current)))
                    { Invoke("combat.endTurn", () => _combatMethods["handView.EndTurn"].Invoke(current, null)); _combatTurn = TurnText(); } }, null, null, TurnText);
        }
        private bool CanEndTurn(string kind, Component button) => CombatCanAct(kind) && Actionable(button) && ((Selectable)button).IsInteractable() &&
            !CombatViews().Any(v => _types["fightBlocker"].IsInstanceOfType(v) || _types["modalBlocker"].IsInstanceOfType(v));

        private void BuildCombatTargets(GraphBuilder builder)
        {
            var targeting = Find("target");
            var skill = ReadProp("target", "Skill", targeting);
            var targets = ((IEnumerable)ReadProp("target", "PossibleTargets", targeting)).Cast<object>().Where(t => FighterShown(t) &&
                (bool)_combatMethods["skill.IsValidTarget"].Invoke(skill, new[] { t })).ToArray();
            foreach (var target in targets)
            {
                var character = target;
                var hero = ReferenceEquals(character, ReadProp("combat", "Hero", Find("combat")));
                CombatPresentation.Add(builder, character, InstanceKey("combat.target", character),
                    () => hero ? _text.Get("combat.hero") : (string)ReadProp("npc", "Name", character), null, null, null,
                    () => { var current = Find("target"); if (current == null) return;
                        bool listed = ((IEnumerable)ReadProp("target", "PossibleTargets", current)).Cast<object>().Any(t => ReferenceEquals(t, character));
                        bool legal = (bool)_combatMethods["skill.IsValidTarget"].Invoke(ReadProp("target", "Skill", current), new[] { character });
                        if (!CombatPresentation.CanTarget(CombatCanAct("target"), FighterShown(character), ModelLive(character), listed, legal)) return;
                        var method = _combatMethods["target.MarkTarget"];
                        Invoke("combat.target", () => method.Invoke(current, new[] { character, Activator.CreateInstance(method.GetParameters()[1].ParameterType), Activator.CreateInstance(method.GetParameters()[2].ParameterType) })); },
                    ReadDetail(() => FighterDetail(character)));
            }
            if (targets.Length == 0) EmptyNode(builder, "targets");
            builder.Add(ControlId.Structural("combat.target.cancel"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.cancel"))) { OnActivate = () => CombatBack("target") });
        }

        private string FighterDetail(object character)
        {
            if (!FighterShown(character)) return null;
            var fighter = ReadProp("character", "Fighter", character);
            var parts = new List<string>();
            foreach (Component root in (IEnumerable)ReadProp("model", "Views", fighter))
            {
                if (!Visible(root) || (!_types["npcUi"].IsInstanceOfType(root) && !_types["fighterHud"].IsInstanceOfType(root))) continue;
                foreach (Component stat in root.GetComponentsInChildren(_types["statDisplay"], false).Where(c => Visible(c as Component)))
                {
                    var type = ReadProp("richReference", "Enum", ReadField("statDisplay", "statType", stat));
                    parts.Add(ReadProp("statType", "DisplayName", type) + ": " + ShownText("statDisplay", "text", stat));
                }
                foreach (Component barrier in root.GetComponentsInChildren(_types["barrierDisplay"], false).Where(c => Visible(c as Component)))
                    parts.Add(_text.Get("combat.barrier") + ": " + ShownText("barrierDisplay", "text", barrier));
            }
            foreach (var view in CombatViews())
            {
                var model = ReadProp("view", "GenericTarget", view);
                if (_types["statusSource"].IsInstanceOfType(model) && ReferenceEquals(ReadProp("statusSource", "Character", model), character)) parts.Add(TooltipText(view));
            }
            foreach (var intent in All("intent").Where(i => ReferenceEquals(ReadProp("intent", "NpcFighter", i), character)))
            {
                var view = ViewOf(intent, _types["intentView"]);
                if (view != null) parts.Add(JoinText(new[] { _text.Get("combat.intent"), ShownText("intentView", "textComponent", view), ShownText("intentView", "cooldown", view), TooltipText(view) }));
            }
            return JoinText(parts);
        }

        private string SelectionText()
        {
            var window = Find("choose"); if (window == null) return null;
            var configuration = ReadProp("choose", "Configuration", window);
            var min = (int?)ReadField("chooseSetup", "minSelection", configuration); var max = (int?)ReadField("chooseSetup", "maxSelection", configuration);
            int selected = ((IEnumerable)ReadProp("choose", "Selected", window)).Cast<object>().Count();
            return _text.Get("combat.selection", selected, min.HasValue ? min.Value.ToString() : _text.Get("combat.noMinimum"), max.HasValue ? max.Value.ToString() : _text.Get("combat.noMaximum"));
        }
        private void BuildCombatChoice(GraphBuilder builder)
        {
            var window = Find("choose"); var view = View("choose");
            var configuration = ReadProp("choose", "Configuration", window);
            bool display = (bool)ReadField("chooseSetup", "onlyDisplay", configuration);
            var decor = (Transform)ReadField("chooseView", "decorsParent", view);
            foreach (var text in decor.GetComponentsInChildren<TMP_Text>(false).Where(Visible))
            { var current = text; builder.Add(ControlId.Referenced(current, "combat.choose.decor." + current.GetInstanceID()), new NodeVtable(new NodeAnnouncement(() => current.text))); }
            if (!display) builder.Add(ControlId.Structural("combat.choose.count"), new NodeVtable(new NodeAnnouncement(SelectionText, "label", true)));
            builder.BeginStop("combat.choose.options");
            foreach (var option in ((IEnumerable)ReadProp("choose", "Options", window)).Cast<object>())
            {
                var current = ViewOf(option, _types["combatChoiceView"]); if (current == null) continue;
                var button = (Component)ReadField("combatChoiceView", "optionButton", current);
                var content = (Transform)ReadField("combatChoiceView", "content", current);
                var preview = content.GetComponentInChildren(_types["preview"]) as Component;
                Func<string> label = () => preview != null ? ShownText("preview", "skillTitle", preview) : JoinText(content.GetComponentsInChildren<TMP_Text>(false).Where(Visible).Select(t => t.text));
                Func<string> selected = () => !display && (bool)ReadProp("combatChoice", "Selected", option) ? _text.Get("state.selected") : null;
                CombatPresentation.Add(builder, option, InstanceKey("combat.choose.option", option), label,
                    () => preview == null ? null : PreviewValue(preview), () => display || ((Selectable)button).IsInteractable() ? null : _text.Get("state.disabled"), selected,
                    display ? null : (Action)(() => { if (CombatCanAct("choose") && ModelLive(option) && ReferenceEquals(ReadProp("combatChoice", "ParentModel", option), Find("choose"))) ActivateButton("choose", button, "combat.choose.option"); }),
                    ReadDetail(() => preview != null ? TooltipText(preview) : ChoiceTooltip(content)),
                    () => { if (preview != null) OpenCardSpec("choose", preview, "preview"); }, () => display ? null : JoinText(new[] { selected() ?? _text.Get("state.unselected"), SelectionText() }));
            }
            builder.BeginStop("combat.choose.confirm");
            var accept = (Component)ReadField("chooseView", "acceptButton", view);
            if (!display && Visible(accept)) builder.Add(ControlId.Structural("combat.choose.confirm"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.confirm")),
                new NodeAnnouncement(() => CanConfirmChoice() ? null : _text.Get("combat.selectionIncomplete"), "reason", true))
                { OnActivate = () => { if (CanConfirmChoice()) ActivateButton("choose", accept, "combat.choose.confirm"); } });
            Buttons(builder, "choose", view, new[] { "skipButton", "rerollButton" });
            var wealth = (Component)ReadField("chooseView", "wealth", view);
            if (Visible(wealth))
                foreach (var text in wealth.GetComponentsInChildren<TMP_Text>(false).Where(t => Visible(t) && !string.IsNullOrWhiteSpace(t.text)))
                { var current = text; builder.Add(ControlId.Referenced(current, "combat.choose.wealth." + current.GetInstanceID()), new NodeVtable(new NodeAnnouncement(() => current.text, "label", true))); }
            if ((bool)ReadField("chooseSetup", "closeable", configuration)) builder.Add(ControlId.Structural("combat.choose.close"),
                new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.close"))) { OnActivate = () => CombatBack("choose") });
        }
        private bool CanConfirmChoice()
        {
            if (!CombatCanAct("choose")) return false;
            var window = Find("choose"); var config = ReadProp("choose", "Configuration", window);
            var button = (Component)ReadField("chooseView", "acceptButton", View("choose"));
            return CombatPresentation.CanConfirmSelection(((IEnumerable)ReadProp("choose", "Selected", window)).Cast<object>().Count(),
                (int?)ReadField("chooseSetup", "minSelection", config), (int?)ReadField("chooseSetup", "maxSelection", config),
                (bool)ReadField("chooseSetup", "onlyDisplay", config), Actionable(button) && ((Selectable)button).IsInteractable());
        }
        private string ChoiceTooltip(Transform content) => JoinText(content.GetComponentsInChildren<Component>(false).Where(v => _types["withTooltip"].IsInstanceOfType(v)).Select(TooltipText));
        private string PreviewValue(Component preview) => JoinText(new[] { ShownText("preview", "cardTypeText", preview), ShownText("preview", "skillCostText", preview), ShownText("preview", "runeChargesCostText", preview), SideText("preview", preview) });

        private void BuildCombatDetails(string kind, GraphBuilder builder)
        {
            var views = kind == "runeDeck" ? new[] { View(kind) } : CombatViews().Where(v => (_types["cardSpecView"].IsInstanceOfType(v) || _types["cardSpecUi"].IsInstanceOfType(v)) &&
                ReadProp("cardSpec", "Host", ReadProp("view", "GenericTarget", v)) == null).ToArray();
            foreach (var root in views)
            {
                foreach (Component preview in root.GetComponentsInChildren(_types["preview"], false).Where(c => Visible(c as Component)))
                {
                    var current = preview;
                    CombatPresentation.Add(builder, current, "combat.preview." + current.GetInstanceID(), () => ShownText("preview", "skillTitle", current),
                        () => PreviewValue(current), null, null, null, ReadDetail(() => TooltipText(current)));
                }
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false).Where(t => Visible(t) && t.GetComponentInParent(_types["preview"]) == null))
                { var current = text; builder.Add(ControlId.Referenced(current, "combat.detail.text." + current.GetInstanceID()), new NodeVtable(new NodeAnnouncement(() => current.text, "label", true))); }
                if (_types["cardSpecUi"].IsInstanceOfType(root))
                    foreach (var field in new[] { "previousArrow", "nextArrow" }) AddButton(builder, kind, (Component)ReadField("cardSpecUi", field, root),
                        ControlId.Structural("combat.detail." + field), "cardSpec." + field);
            }
            builder.Add(ControlId.Structural("combat.details.close"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.close"))) { OnActivate = () => CombatBack(kind) });
        }

        private void BuildCombatResult(GraphBuilder builder)
        {
            var roots = CombatViews().Where(v => _types["summaryView"].IsInstanceOfType(v) || _types["lootView"].IsInstanceOfType(v) ||
                _types["orbCombineView"].IsInstanceOfType(v) || _types["orbView"].IsInstanceOfType(v) || _types["rewardItemView"].IsInstanceOfType(v) ||
                _types["defeatView"].IsInstanceOfType(v) || _types["victoryView"].IsInstanceOfType(v)).ToArray();
            builder.Add(ControlId.Structural("combat.result"), new NodeVtable(new NodeAnnouncement(() =>
                _text.Get(roots.Any(v => _types["defeatView"].IsInstanceOfType(v)) ? "combat.defeat" :
                    roots.Any(v => _types["victoryView"].IsInstanceOfType(v) || _types["summaryView"].IsInstanceOfType(v) || _types["lootView"].IsInstanceOfType(v)) ? "combat.victory" : "combat.resultShown"))));
            var seen = new HashSet<int>();
            foreach (var root in roots)
            {
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false).Where(t => Visible(t) && !string.IsNullOrWhiteSpace(t.text) && seen.Add(t.GetInstanceID())))
                { var current = text; builder.Add(ControlId.Referenced(current, "combat.result.text." + current.GetInstanceID()), new NodeVtable(new NodeAnnouncement(() => current.text, "label", true))); }
                foreach (var binding in new[] { new[] { "summaryView", "continueButton" }, new[] { "lootView", "takeAllButton" },
                    new[] { "orbCombineView", "leftArrow" }, new[] { "orbCombineView", "rightArrow" }, new[] { "orbCombineView", "continueButton" }, new[] { "orbView", "fuseButton" } })
                {
                    if (!_types[binding[0]].IsInstanceOfType(root)) continue;
                    var button = (Component)ReadField(binding[0], binding[1], root);
                    if (button == null || !seen.Add(button.GetInstanceID())) continue;
                    AddButton(builder, "result", button, ControlId.Referenced(ReadProp("view", "GenericTarget", root), "combat.result." + binding[1]), "result." + binding[1]);
                }
            }
            // XP's normal Submit accelerates its own animation; it does not grant another reward.
            foreach (var root in roots.Where(v => _types["summaryView"].IsInstanceOfType(v)))
            { var current = root; builder.Add(ControlId.Structural("combat.result.finishAnimation"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.finishAnimation")))
                { OnActivate = () => { if (CombatCanAct("result")) Invoke("combat.result.finishAnimation", () => _combatMethods["summaryView.Handle"].Invoke(current, new[] { _submit })); } }); }
        }

        private object[] LogMessages()
        {
            var view = View("battleLog"); return view == null ? Array.Empty<object>() : ((IEnumerable)ReadField("battleLogView", "loggedMessages", view)).Cast<object>().ToArray();
        }
        private void BuildCombatLog(GraphBuilder builder)
        {
            Zone(builder, "log");
            builder.Add(ControlId.Structural("combat.log.short"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.log.short"))) { OnActivate = ReadDetail(() => ReadLog(true)) });
            builder.Add(ControlId.Structural("combat.log.full"), new NodeVtable(new NodeAnnouncement(() => _text.Get("combat.log.full"))) { OnActivate = ReadDetail(() => ReadLog(false)) });
            var messages = LogMessages();
            for (int i = 0; i < messages.Length; i++)
            { var message = messages[i]; builder.Add(ControlId.Referenced(message, "combat.log.line." + i), new NodeVtable(new NodeAnnouncement(() => (string)ReadField("loggedMessage", "log", message)))); }
            builder.PopContext();
        }
        private string ReadLog(bool brief)
        {
            var board = Find("combat"); if (board == null) return null;
            var round = (int)ReadProp("combat", "CurrentRoundNumber", board);
            var messages = LogMessages().Where(m => !brief || (int)ReadField("loggedMessage", "roundNumber", m) == round);
            return JoinText(messages.Select(m => (string)ReadField("loggedMessage", "log", m)));
        }
        public void ObserveCombat()
        {
            var board = Find("combat");
            if (board == null) { ClearCombat(); return; }
            var session = InstanceKey("fight", board);
            var turn = TurnText();
            if (_combatSession == session && _combatTurn != turn && CombatActive("combat")) _speech?.Speak(turn, false);
            _combatSession = session; _combatTurn = turn;
            var messages = LogMessages().Select(m => (string)ReadField("loggedMessage", "log", m)).ToArray();
            foreach (var message in _combatLog.Observe(InstanceKey("fight", board), messages)) _speech?.Speak(message, false);
        }
        public void ClearCombat() { _combatLog.Reset(); _combatSession = null; _combatTurn = null; }
        private bool CombatBack(string kind)
        {
            if (!CombatCanAct(kind)) return false;
            if (kind == "combatTutorial") { ActivateButton(kind, (Component)ReadField("combatView", "closeDruidTutorialButton", View("combat")), "combatTutorial.close"); return true; }
            if (kind == "target" || kind == "runeDeck")
            { Invoke("combat.cancel", () => _combatMethods[kind + "View.Handle"].Invoke(View(kind), new[] { _cancelEvent })); return true; }
            if (kind == "choose")
            {
                var config = ReadProp("choose", "Configuration", Find(kind));
                if (!(bool)ReadField("chooseSetup", "closeable", config)) return true;
            }
            if (kind != "choose" && kind != "cardSpec" && kind != "combatMenu") return false;
            Invoke("combat.close", () => _combatMethods[kind + ".Close"].Invoke(Find(kind), null)); return true;
        }
    }
}
