using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;

namespace TaintedGrailAccess.BepInExHost
{
    internal sealed partial class TitleUiBindings
    {
        private readonly Dictionary<string, MethodInfo> _all = new Dictionary<string, MethodInfo>();
        private MethodInfo _runeHandle;
        private MethodInfo _consentLink;
        private MethodInfo _getHosting;
        private MethodInfo _onHud;
        private MethodInfo _orderedModels;
        private MethodInfo _loadingHandle;
        private MethodInfo _storyWaitHandle;
        private Type _storyInputData;
        private FieldInfo _storyInputName;
        private FieldInfo _buttonClick;
        private string _actionScreen;
        private string _auxiliaryText;
        private readonly HashSet<string> _observedPathViews = new HashSet<string>();
        private static readonly string[] PathScreens = { "mode", "slots", "patch", "welcome", "creator", "class", "run", "story", "video", "tutorial", "loading" };
        private static readonly Dictionary<string, string> ButtonLabels = new Dictionary<string, string>
        {
            ["mode.conquest"] = "path.conquest", ["mode.weeklyEvent"] = "path.weekly", ["mode.backButton"] = "path.back",
            ["creator.maleButton"] = "path.male", ["creator.femaleButton"] = "path.female",
            ["creator.closeButton"] = "path.back", ["class.closeButton"] = "path.back", ["slots.closeButton"] = "path.back",
            ["run.lowerCorruption"] = "path.lowerDifficulty", ["run.increaseCorruption"] = "path.raiseDifficulty",
            ["run.selectRuneLeft"] = "path.previousClass", ["run.selectRuneRight"] = "path.nextClass",
            ["run.expandRuneDescription"] = "path.classDetails", ["run.expandPassiveDescription"] = "path.passiveDetails",
            ["run.toggleCorruptionDescription"] = "path.difficultyDetails",
            ["combat.showDeckButton"] = "combat.showDeck", ["combat.showDiscardButton"] = "combat.showDiscard",
            ["combat.showGraveButton"] = "combat.showGrave", ["combat.showExileButton"] = "combat.showExile",
            ["result.leftArrow"] = "combat.previousOrb", ["result.rightArrow"] = "combat.nextOrb",
            ["result.fuseButton"] = "combat.fuseOrb", ["result.continueButton"] = "path.continue",
            ["result.takeAllButton"] = "combat.takeAll", ["choose.skipButton"] = "combat.skip",
            ["choose.rerollButton"] = "combat.reroll",
            ["cardSpec.previousArrow"] = "combat.previousSide", ["cardSpec.nextArrow"] = "combat.nextSide",
            ["combat.classHelp"] = "combat.classHelp", ["combatTutorial.close"] = "combat.close"
        };

        private void InitializePath(Assembly assembly)
        {
            AddPath(assembly, "mode", "Awaken.TG.Main.UI.TitleScreen.TitleScreenGameModes", "Awaken.TG.Main.UI.TitleScreen.VTitleScreenGameMode");
            AddPath(assembly, "slots", "Awaken.TG.Main.UI.TitleScreen.LoadSaveUI", "Awaken.TG.Main.UI.TitleScreen.VLoadSaveUI");
            AddPath(assembly, "slot", "Awaken.TG.Main.UI.TitleScreen.SaveSlotUI", "Awaken.TG.Main.UI.TitleScreen.VSaveSlotUI");
            AddPath(assembly, "patch", "Awaken.TG.Main.UI.TitleScreen.PatchNotes.PatchNotesUI", "Awaken.TG.Main.UI.TitleScreen.PatchNotes.VPatchNotesUI");
            AddPath(assembly, "welcome", "Awaken.TG.Main.UI.TitleScreen.WelcomePopupUI", "Awaken.TG.Main.UI.TitleScreen.VWelcomePopupUI");
            AddPath(assembly, "creator", "Awaken.TG.Main.UI.HeroCreator.HeroCreatorUI", "Awaken.TG.Main.UI.HeroCreator.VHeroCreatorButtons");
            AddPath(assembly, "gender", "Awaken.TG.Main.UI.HeroCreator.Tabs.Gender.GenderUI", "Awaken.TG.Main.UI.HeroCreator.Tabs.Gender.VGenderUI");
            AddPath(assembly, "class", "Awaken.TG.Main.Classes.ClassSelection", "Awaken.TG.Main.Classes.VClassSelection");
            AddPath(assembly, "rune", "Awaken.TG.Main.Classes.RuneSelection", "Awaken.TG.Main.Classes.VRuneSelection");
            AddPath(assembly, "run", "Awaken.TG.Main.Classes.NewRunSelection", "Awaken.TG.Main.Classes.VNewRunSelection");
            AddPath(assembly, "passive", "Awaken.TG.Main.Classes.PassivePossibility", "Awaken.TG.Main.Classes.VPassivePossibility");
            AddPath(assembly, "story", "Awaken.TG.Main.Stories.Story", "Awaken.TG.Main.Stories.VStoryPanel");
            AddType(assembly, "storyStartView", "Awaken.TG.Main.Stories.VStartStoryPanel");
            AddType(assembly, "storyPopupView", "Awaken.TG.Main.UI.Popup.VPopupUI");
            AddPath(assembly, "loading", "Awaken.TG.Main.UI.TitleScreen.LoadingScreenUI", "Awaken.TG.Main.UI.TitleScreen.VLoadingScreenUI");
            AddType(assembly, "mouseDown", "Awaken.TG.MVC.UI.Events.UIEMouseDown");
            if (_types["mouseDown"].GetConstructor(Type.EmptyTypes) == null) throw new MissingMethodException(_types["mouseDown"].FullName, ".ctor()");
            _loadingHandle = RequireMethod(_types["loadingView"], "Handle", assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true));
            Prop("loading", "WaitForInputEnabled", typeof(bool));
            Field("loadingView", "hintMessage", typeof(TextMeshProUGUI));
            Field("loadingView", "firstGameText", typeof(TextMeshProUGUI));
            Field("loadingView", "firstGameMessage", typeof(GameObject));
            Field("loadingView", "waitForInput", typeof(CanvasGroup[]));
            Field("storyPopupView", "titleText", typeof(TextMeshProUGUI));
            foreach (var name in new[] { "parent", "buttonParent" }) Field("storyPopupView", name, typeof(Transform));
            AddPath(assembly, "video", "Awaken.TG.Main.Utility.Video.Video", "Awaken.TG.Main.Utility.Video.VVideo");
            AddPath(assembly, "subtitles", "Awaken.TG.Main.Utility.Video.Subtitles.VideoSubtitles", "Awaken.TG.Main.Utility.Video.Subtitles.VVideoSubtitles");
            AddPath(assembly, "fightEntry", "Awaken.TG.Main.Fights.FightBoard", "Awaken.TG.Main.Fights.UI.VFightBoardKeys");
            AddPath(assembly, "hud", "Awaken.TG.Main.UI.HUD.HUD", "Awaken.TG.Main.Tutorials.Views.VTutorialHUD");
            AddType(assembly, "smartView", "Awaken.TG.Main.Tutorials.Views.VSmartText");
            AddType(assembly, "step", "Awaken.TG.Main.Tutorials.Steps.TutorialStep");
            AddType(assembly, "view", "Awaken.TG.MVC.View"); Prop("view", "GenericTarget");
            AddType(assembly, "choiceView", "Awaken.TG.Main.Stories.Choices.VChoice");
            AddType(assembly, "startChoiceView", "Awaken.TG.Main.Stories.Choices.VStartStoryChoice");
            AddType(assembly, "choiceButton", "Awaken.TG.Main.Stories.Choices.ChoiceButton");
            AddType(assembly, "runeTemplate", "Awaken.TG.Main.Heroes.Skills.Runes.RuneTemplate");
            AddType(assembly, "storyOnTop", "Awaken.TG.Main.Stories.StoryOnTop");
            AddType(assembly, "storyWait", "Awaken.TG.Main.Stories.Steps.Helpers.KeyboardListenerForInput");
            AddType(assembly, "storyKeyDown", "Awaken.TG.MVC.UI.Events.UIKeyDownAction");
            Prop("storyWait", "ParentModel"); Prop("storyKeyDown", "Data");
            _storyInputData = _properties["storyKeyDown.Data"].PropertyType;
            _storyInputName = _storyInputData.GetField("actionName") ?? throw new MissingFieldException(_storyInputData.FullName, "actionName");
            if (_storyInputName.FieldType != typeof(string)) throw new MissingFieldException(_storyInputData.FullName, "actionName: string");
            _storyWaitHandle = RequireMethod(_types["storyWait"], "Handle", assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true));
            var world = assembly.GetType("Awaken.TG.MVC.World", true);
            var all = world.GetMethods().Single(m => m.Name == "All" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            var hosting = assembly.GetType("Awaken.TG.MVC.ViewHosting", true);
            _getHosting = assembly.GetType("Awaken.TG.MVC.Services", true).GetMethods().Single(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(hosting);
            _onHud = RequireMethod(hosting, "OnHUD", typeof(string[]));
            foreach (var kind in new[] { "slot", "rune", "passive", "story", "step", "storyOnTop", "storyWait" }) _all.Add(kind, all.MakeGenericMethod(_types[kind]));
            _orderedModels = world.GetMethods().Single(m => m.Name == "AllInOrder" && !m.IsGenericMethod && m.GetParameters().Length == 0);
            Prop("model", "ID", typeof(string)); Prop("class", "Visible", typeof(bool));
            Prop("rune", "RuneTemplate"); Prop("rune", "Selected", typeof(bool));
            Prop("runeView", "IsInitialized", typeof(bool)); Prop("runeView", "IsUnlocked", typeof(bool)); Prop("runeView", "Interactable", typeof(bool));
            Field("runeTemplate", "displayName"); Field("runeTemplate", "description");
            Field("genderView", "heroNameField", typeof(TMP_InputField));
            foreach (var name in new[] { "maleButton", "femaleButton" }) Field("genderView", name, _types["button"]);
            foreach (var name in new[] { "closeButton", "acceptButton" }) Field("creatorView", name, _types["button"]);
            foreach (var name in new[] { "conquest", "weeklyEvent", "backButton" }) Field("modeView", name, _types["button"]);
            Field("slotsView", "title", typeof(TextMeshProUGUI)); Field("slotsView", "closeButton", _types["button"]);
            Field("slotView", "slotText", typeof(TextMeshProUGUI)); Field("slotView", "chooseSlot", _types["button"]); Field("slotView", "clearSlot", _types["button"]);
            Prop("slot", "Selected", typeof(bool));
            Field("patchView", "title", typeof(TextMeshProUGUI)); Field("patchView", "message", typeof(TextMeshProUGUI)); Field("patchView", "closeButton", _types["button"]);
            Field("welcomeView", "continueButton", _types["button"]);
            Field("classView", "closeButton", _types["button"]);
            foreach (var name in new[] { "corruptionLevel", "className", "runeName", "runeDescription", "runeUltimateDescription", "passiveDescription", "corruptionDescription" }) Field("runView", name, typeof(TextMeshProUGUI));
            foreach (var name in new[] { "changeClass", "lowerCorruption", "increaseCorruption", "startGame", "selectRuneLeft", "selectRuneRight", "expandRuneDescription", "expandPassiveDescription", "toggleCorruptionDescription" }) Field("runView", name, _types["button"]);
            Field("passiveView", "button", _types["button"]); Prop("passive", "Description", typeof(string)); Prop("passive", "Selected", typeof(bool));
            Field("storyView", "storyTitleText", typeof(TextMeshProUGUI)); Field("storyView", "storyParent", typeof(Transform));
            Field("storyStartView", "titleText", typeof(TextMeshProUGUI)); Field("storyStartView", "choicesParent", typeof(Transform));
            Field("choiceView", "choiceButton", _types["choiceButton"]);
            Field("choiceButton", "button", _types["button"]); Field("choiceButton", "mainText", typeof(TextMeshProUGUI)); Field("choiceButton", "additionalText", typeof(TextMeshProUGUI));
            Field("startChoiceView", "button", _types["button"]); Field("startChoiceView", "mainText", typeof(TextMeshProUGUI)); Field("startChoiceView", "additionalText", typeof(TextMeshProUGUI));
            Field("subtitlesView", "text", typeof(TMP_Text));
            foreach (var name in new[] { "textMesh", "bigTextMesh" }) Field("smartView", name, typeof(TMP_Text));
            Prop("fightEntry", "IsFightStarted", typeof(bool)); Prop("fightEntry", "IsFightOver", typeof(bool));
            _runeHandle = RequireMethod(_types["runeView"], "Handle", assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true));
            _consentLink = RequireMethod(_types["consent"], "LinkClicked", typeof(string));
            _buttonClick = _types["button"].GetField("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(_types["button"].FullName, "OnClick");
            if (_buttonClick.FieldType != typeof(Action)) throw new MissingFieldException(_types["button"].FullName, "OnClick: Action");
        }

        private void AddPath(Assembly assembly, string kind, string modelName, string viewName)
        {
            AddType(assembly, kind, modelName); AddType(assembly, kind + "View", viewName);
            var any = assembly.GetType("Awaken.TG.MVC.World", true).GetMethods().Single(m => m.Name == "Any" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            _find.Add(kind, any.MakeGenericMethod(_types[kind]));
        }

        private IEnumerable<object> All(string kind) => World == null ? Enumerable.Empty<object>() : ((IEnumerable)_all[kind].Invoke(World, null)).Cast<object>();
        private Component ViewOf(object model, Type viewType)
        {
            if (model == null || !(bool)ReadProp("model", "IsFullyInitialized", model) || (bool)ReadProp("model", "WasDiscarded", model) || (bool)ReadProp("model", "IsBeingDiscarded", model)) return null;
            return LiveViewSelection.Find((IEnumerable)ReadProp("model", "Views", model), viewType, v => Visible(v as Component)) as Component;
        }

        private bool PathActive(string kind)
        {
            if (kind == "class") return Find(kind) != null && (bool)ReadProp("class", "Visible", Find(kind)) && View(kind) != null;
            if (kind == "story") return StoryView() != null;
            if (kind == "tutorial") return SmartViews().Any();
            if (kind == "fightEntry") return Find(kind) != null && (bool)ReadProp(kind, "IsFightStarted", Find(kind)) && !(bool)ReadProp(kind, "IsFightOver", Find(kind));
            return View(kind) != null;
        }
        private Component StoryView() => All("story").Select(model => ViewOf(model, _types["storyView"]) ?? ViewOf(model, _types["storyStartView"]) ?? ViewOf(model, _types["storyPopupView"])).FirstOrDefault(v => v != null);
        public bool HasLoadingScreen(object world) => world != null && ViewOf(_find["loading"].Invoke(world, null), _types["loadingView"]) != null;

        private bool LoadingReady(Component view) => (bool)ReadProp("loading", "WaitForInputEnabled", Find("loading")) &&
            ((GameObject)ReadField("loadingView", "firstGameMessage", view)).activeInHierarchy &&
            ((CanvasGroup[])ReadField("loadingView", "waitForInput", view)).Any(group => Visible(group) && group.alpha > 0f);
        private IEnumerable<Component> SmartViews()
        {
            // TextPart binds to its current target model. The live HUD canvas contains only
            // rendered smart texts; no templates, tutorial future steps or scene-wide scan.
            if (World == null || Find("menu") != null) yield break;
            var services = _properties["world.Services"].GetValue(World);
            var hosting = _getHosting.Invoke(services, null);
            var host = _onHud.Invoke(hosting, new object[] { Array.Empty<string>() }) as Transform;
            if (host == null) yield break;
            foreach (Component view in host.GetComponentsInChildren(_types["smartView"], false))
                if (Visible(view)) yield return view;
        }

        private void TextNode(GraphBuilder builder, Component owner, string fieldOwner, string field, string key)
        {
            var label = ReadField(fieldOwner, field, owner) as TMP_Text;
            if (!Visible(label) || string.IsNullOrWhiteSpace(label.text)) return;
            builder.Add(ControlId.Referenced(label, key), new NodeVtable(new NodeAnnouncement(() => Visible(label) ? label.text : null, "label", true)));
        }

        private bool BuildPath(string kind, GraphBuilder builder)
        {
            if (!PathScreens.Contains(kind)) return false;
            if (kind == "loading")
            {
                var loading = View(kind);
                TextNode(builder, loading, "loadingView", "firstGameText", "loading.firstGame");
                TextNode(builder, loading, "loadingView", "hintMessage", "loading.hint");
                if (LoadingReady(loading))
                    builder.Add(ControlId.Structural("loading.continue"), new NodeVtable(new NodeAnnouncement(() => _text.Get("path.continue")),
                        new NodeAnnouncement(() => _text.Get("role.button"), "role"))
                        { OnActivate = () => { if (CanAct(kind) && LoadingReady(loading))
                            Invoke("loading.continue", () => _loadingHandle.Invoke(loading, new[] { Activator.CreateInstance(_types["mouseDown"]) })); } });
                return true;
            }
            if (kind == "tutorial")
            {
                foreach (var smart in SmartViews())
                {
                    var large = (TMP_Text)ReadField("smartView", "bigTextMesh", smart);
                    var small = (TMP_Text)ReadField("smartView", "textMesh", smart);
                    var label = Visible(large) ? large : Visible(small) ? small : null;
                    if (label != null && !string.IsNullOrWhiteSpace(label.text))
                        builder.Add(ControlId.ForObject(smart), new NodeVtable(new NodeAnnouncement(() => label.text, "label")));
                }
                return true;
            }
            if (kind == "fightEntry")
            {
                builder.Add(ControlId.Structural("first-fight.entry"), new NodeVtable(new NodeAnnouncement(() => _text.Get("path.firstFight"))));
                return true;
            }
            if (kind == "story") { BuildStory(builder); return true; }
            var view = View(kind);
            if (kind == "mode") Buttons(builder, kind, view, new[] { "conquest", "weeklyEvent", "backButton" });
            else if (kind == "slots")
            {
                TextNode(builder, view, "slotsView", "title", "slots.title");
                foreach (var model in All("slot"))
                {
                    var slot = ViewOf(model, _types["slotView"]);
                    if (slot == null) continue;
                    var label = (TMP_Text)ReadField("slotView", "slotText", slot);
                    var choose = (Component)ReadField("slotView", "chooseSlot", slot);
                    var id = (string)ReadProp("model", "ID", model);
                    builder.Add(ControlId.Referenced(model, "slot." + id), new NodeVtable(new NodeAnnouncement(() => Visible(label) ? label.text : null),
                        new NodeAnnouncement(() => (bool)ReadProp("slot", "Selected", model) ? _text.Get("state.selected") : null, "selected"))
                        { OnActivate = () => ActivateButton(kind, choose, "slots.choose") });
                    AddButton(builder, kind, (Component)ReadField("slotView", "clearSlot", slot), ControlId.Referenced(model, "slot.delete." + id), "slots.delete");
                }
                Buttons(builder, kind, view, new[] { "closeButton" });
            }
            else if (kind == "patch")
            {
                TextNode(builder, view, "patchView", "title", "patch.title"); TextNode(builder, view, "patchView", "message", "patch.message");
                Buttons(builder, kind, view, new[] { "closeButton" });
            }
            else if (kind == "welcome")
            {
                foreach (var text in view.GetComponentsInChildren<TMP_Text>(false).Where(Visible))
                    if (text.GetComponentInParent(_types["button"]) == null && !string.IsNullOrWhiteSpace(text.text))
                        builder.Add(ControlId.ForObject(text), new NodeVtable(new NodeAnnouncement(() => text.text)));
                Buttons(builder, kind, view, new[] { "continueButton" });
            }
            else if (kind == "creator")
            {
                var gender = View("gender");
                if (gender != null)
                {
                    AddTextField(builder, kind, (TMP_InputField)ReadField("genderView", "heroNameField", gender), ControlId.Structural("creator.name"), "text.heroName");
                    foreach (var name in new[] { "maleButton", "femaleButton" })
                        AddButton(builder, kind, (Component)ReadField("genderView", name, gender), ControlId.Structural("creator." + name), "creator." + name);
                }
                Buttons(builder, kind, view, new[] { "acceptButton", "closeButton" });
            }
            else if (kind == "class")
            {
                foreach (var model in All("rune"))
                {
                    var rune = ViewOf(model, _types["runeView"]);
                    if (rune == null || !(bool)ReadProp("runeView", "IsInitialized", rune)) continue;
                    var template = ReadProp("rune", "RuneTemplate", model);
                    var name = ReadField("runeTemplate", "displayName", template);
                    builder.Add(ControlId.Referenced(model, "class." + ReadProp("model", "ID", model)), new NodeVtable(
                        new NodeAnnouncement(() => name.ToString(), "label"),
                        new NodeAnnouncement(() => (bool)ReadProp("runeView", "IsUnlocked", rune) ? null : _text.Get("state.disabled"), "enabled"),
                        new NodeAnnouncement(() => (bool)ReadProp("rune", "Selected", model) ? _text.Get("state.selected") : null, "selected"))
                        { OnActivate = () => { if (CanAct(kind) && (bool)ReadProp("runeView", "IsUnlocked", rune) && (bool)ReadProp("runeView", "Interactable", rune))
                            Invoke("class.choose", () => _runeHandle.Invoke(rune, new[] { _submit })); } });
                }
                Buttons(builder, kind, view, new[] { "closeButton" });
            }
            else if (kind == "run")
            {
                builder.BeginStop("description");
                foreach (var name in new[] { "className", "runeName", "corruptionLevel", "runeDescription", "runeUltimateDescription", "passiveDescription", "corruptionDescription" })
                    TextNode(builder, view, "runView", name, "run." + name);
                builder.BeginStop("choices");
                foreach (var model in All("passive"))
                {
                    var passive = ViewOf(model, _types["passiveView"]);
                    if (passive == null) continue;
                    var button = (Component)ReadField("passiveView", "button", passive);
                    builder.Add(ControlId.Referenced(model, "passive." + ReadProp("model", "ID", model)), new NodeVtable(
                        new NodeAnnouncement(() => _text.Get("path.passive"), "label"),
                        new NodeAnnouncement(() => (bool)ReadProp("passive", "Selected", model) ? _text.Get("state.selected") : null, "selected"))
                        { OnActivate = () => ActivateButton(kind, button, "run.passive") });
                }
                builder.BeginStop("commands");
                Buttons(builder, kind, view, new[] { "lowerCorruption", "increaseCorruption", "selectRuneLeft", "selectRuneRight", "expandRuneDescription", "expandPassiveDescription", "toggleCorruptionDescription", "startGame", "changeClass" });
            }
            else if (kind == "video")
            {
                var subtitles = View("subtitles");
                if (subtitles != null) TextNode(builder, subtitles, "subtitlesView", "text", "video.subtitles");
            }
            return true;
        }

        private void BuildStory(GraphBuilder builder)
        {
            var view = StoryView();
            if (_types["storyPopupView"].IsInstanceOfType(view))
            {
                TextNode(builder, view, "storyPopupView", "titleText", "story.title");
                builder.BeginStop("story.text");
                var textParent = (Transform)ReadField("storyPopupView", "parent", view);
                foreach (var label in textParent.GetComponentsInChildren<TMP_Text>(false).Where(Visible))
                    if (!string.IsNullOrWhiteSpace(label.text)) builder.Add(ControlId.ForObject(label), new NodeVtable(new NodeAnnouncement(() => label.text)));
                builder.BeginStop("story.choices");
                foreach (Component button in ((Transform)ReadField("storyPopupView", "buttonParent", view)).GetComponentsInChildren(_types["button"], false))
                    AddButton(builder, "story", button, ControlId.ForObject(button), "story.popup.choice");
                AddStoryContinue(builder, view);
                return;
            }
            bool start = _types["storyStartView"].IsInstanceOfType(view);
            var root = (Transform)ReadField(start ? "storyStartView" : "storyView", start ? "choicesParent" : "storyParent", view);
            TextNode(builder, view, start ? "storyStartView" : "storyView", start ? "titleText" : "storyTitleText", "story.title");
            builder.BeginStop("story.text");
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false).Where(Visible))
            {
                if (label.GetComponentInParent(_types["choiceView"]) != null || label.GetComponentInParent(_types["startChoiceView"]) != null) continue;
                if (!string.IsNullOrWhiteSpace(label.text)) builder.Add(ControlId.ForObject(label), new NodeVtable(new NodeAnnouncement(() => label.text, "label", true)));
            }
            builder.BeginStop("story.choices");
            foreach (Component choice in root.GetComponentsInChildren(start ? _types["startChoiceView"] : _types["choiceView"], false))
            {
                if (!Visible(choice)) continue;
                var buttonOwner = start ? (object)choice : ReadField("choiceView", "choiceButton", choice);
                var alias = start ? "startChoiceView" : "choiceButton";
                var button = (Component)ReadField(alias, "button", buttonOwner);
                var main = (TMP_Text)ReadField(alias, "mainText", buttonOwner);
                var detail = (TMP_Text)ReadField(alias, "additionalText", buttonOwner);
                var model = ReadProp("view", "GenericTarget", choice);
                if (!Visible(main) || !Visible(button)) continue;
                builder.Add(ControlId.Referenced(model, "story.choice." + ReadProp("model", "ID", model)), new NodeVtable(
                    new NodeAnnouncement(() => main.text, "label"),
                    new NodeAnnouncement(() => Visible(detail) ? detail.text : null, "reason"),
                    new NodeAnnouncement(() => ((UnityEngine.UI.Selectable)button).IsInteractable() ? null : _text.Get("state.disabled"), "enabled"))
                    { OnActivate = () => { if (_buttonClick.GetValue(button) != null) ActivateButton("story", button, "story.choice"); } });
            }
            AddStoryContinue(builder, view);
        }

        private object CurrentStoryWait(Component view)
        {
            var parent = ReadProp("view", "GenericTarget", view);
            return All("storyWait").FirstOrDefault(wait => ReferenceEquals(parent, ReadProp("storyWait", "ParentModel", wait)) &&
                !(bool)ReadProp("model", "WasDiscarded", wait) && !(bool)ReadProp("model", "IsBeingDiscarded", wait));
        }

        private void AddStoryContinue(GraphBuilder builder, Component view)
        {
            var wait = CurrentStoryWait(view);
            if (wait == null) return;
            builder.Add(ControlId.Referenced(wait, "story.wait-input"), new NodeVtable(new NodeAnnouncement(() => _text.Get("path.continue")),
                new NodeAnnouncement(() => _text.Get("role.button"), "role"))
            {
                OnActivate = () =>
                {
                    if (!CanAct("story")) return;
                    var current = CurrentStoryWait(view);
                    if (current == null) return;
                    // Exact original wait handler only reads Data.actionName. It cancels its
                    // live wait and preserves the game's audio/cleanup/step completion flow.
                    var data = Activator.CreateInstance(_storyInputData);
                    _storyInputName.SetValue(data, "SkipDialogue");
                    var evt = Activator.CreateInstance(_types["storyKeyDown"]);
                    _properties["storyKeyDown.Data"].SetValue(evt, data);
                    Invoke("story.continue", () => _storyWaitHandle.Invoke(current, new[] { evt }));
                }
            });
        }

        private bool PathBack(string kind)
        {
            var field = kind == "mode" ? "backButton" : kind == "creator" || kind == "class" || kind == "slots" || kind == "patch" ? "closeButton" : kind == "run" ? "changeClass" : null;
            if (field == null || !CanAct(kind)) return false;
            ActivateButton(kind, (Component)ReadField(kind + "View", field, View(kind)), kind + ".back");
            return true;
        }

        private void ConsentLinks(GraphBuilder builder, Component view)
        {
            var text = (TMP_Text)ReadField("consentView", "text", view);
            if (!Visible(text)) return;
            // Read only links actually rendered in this popup; opening uses its original callback.
            for (int index = 0; index < text.textInfo.linkCount; index++)
            {
                var link = text.textInfo.linkInfo[index];
                string target = link.GetLinkID(), label = link.GetLinkText();
                if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(label)) continue;
                builder.Add(ControlId.Structural("consent.link." + index), new NodeVtable(new NodeAnnouncement(() => label),
                    new NodeAnnouncement(() => _text.Get("role.link"), "role"))
                    { OnActivate = () => { if (CanAct("consent")) Invoke("consent.link", () => _consentLink.Invoke(Find("consent"), new object[] { target })); } });
            }
        }

        /// <summary>Visible tutorial instructions and subtitles are background events, never focus announcements.</summary>
        public void ClearAuxiliaryText() { _auxiliaryText = null; _observedPathViews.Clear(); }

        public void ObserveAuxiliaryText()
        {
            // A bounded, type-only observation explains gaps at a real scene transition.
            // No labels, names, save data or fields of unrelated models enter diagnostics.
            if (_actionScreen == null || _actionScreen == "unmapped")
                foreach (var model in ((IEnumerable)_orderedModels.Invoke(World, null)).Cast<object>())
                    foreach (var candidate in (IEnumerable)ReadProp("model", "Views", model))
                        if (_observedPathViews.Count < 160 && _observedPathViews.Add(candidate.GetType().FullName + ":" + Visible(candidate as Component)))
                            _log.Write("binding.path.view", candidate.GetType().FullName + "; visible=" + Visible(candidate as Component));
            if (HelpOpen || UnsupportedModal() || View("popup") != null || View("settings") != null || View("creator") != null || PathActive("class")) return;
            var lines = new List<string>();
            var subtitles = View("subtitles");
            if (subtitles != null)
            {
                var text = ReadField("subtitlesView", "text", subtitles) as TMP_Text;
                if (Visible(text)) lines.Add(text.text);
            }
            foreach (var smart in SmartViews())
            {
                var large = ReadField("smartView", "bigTextMesh", smart) as TMP_Text;
                var small = ReadField("smartView", "textMesh", smart) as TMP_Text;
                var text = Visible(large) ? large : Visible(small) ? small : null;
                if (text != null) lines.Add(text.text);
            }
            var value = string.Join("\n", lines.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            if (value == _auxiliaryText) return;
            _auxiliaryText = value;
            // Tutorial starts without a graph cursor: its first instruction still needs speech.
            // Background changes have this one observer; rereading a focused node is user input.
            if (value.Length > 0) _speech?.Speak(value, false);
        }
    }
}
