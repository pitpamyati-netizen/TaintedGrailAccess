using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>Live recipes for menus, settings and the first path through tutorial entry.</summary>
    internal sealed partial class TitleUiBindings
    {
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        private readonly Dictionary<string, FieldInfo> _fields = new Dictionary<string, FieldInfo>();
        private readonly Dictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();
        private readonly Dictionary<string, MethodInfo> _find = new Dictionary<string, MethodInfo>();
        private readonly Localizer _text;
        private readonly IRuntimeLog _log;
        private readonly ISpeechOutput _speech;
        private readonly MethodInfo _buttonHandle;
        private readonly MethodInfo _closeSettings;
        private readonly MethodInfo _getLayers;
        private readonly object _submit;
        private readonly Type _tabEnum;
        private int _actions;
        private int _pendingTextFrame = -1;
        private string _lastTextState;
        // These are callback-scoped references, always cleared in the poller's finally.
        public object World { get; set; }
        public bool HelpOpen { get; set; }

        public TitleUiBindings(Assembly assembly, Localizer text, IRuntimeLog log, ISpeechOutput speech)
        {
            _text = text; _log = log; _speech = speech == null ? null : new FormattedSpeechOutput(speech, PlainGameText);
            AddType(assembly, "model", "Awaken.TG.MVC.Model");
            AddType(assembly, "menu", "Awaken.TG.Main.UI.TitleScreen.TitleScreenUI");
            AddType(assembly, "menuView", "Awaken.TG.Main.UI.TitleScreen.VTitleScreenRogueUI");
            AddType(assembly, "settings", "Awaken.TG.Main.Settings.Windows.AllSettingsUI");
            AddType(assembly, "settingsView", "Awaken.TG.Main.Settings.Windows.VSettingsUI");
            AddType(assembly, "consent", "Awaken.TG.Main.UI.TitleScreen.TitlePopupUI");
            AddType(assembly, "consentView", "Awaken.TG.Main.UI.TitleScreen.VTitlePopupUI");
            AddType(assembly, "popup", "Awaken.TG.Main.UI.Popup.PopupUI");
            AddType(assembly, "popupView", "Awaken.TG.Main.UI.Popup.VSmallPopupUI");
            AddType(assembly, "toggle", "Awaken.TG.Main.Settings.Options.Views.VToggle");
            AddType(assembly, "optionButton", "Awaken.TG.Main.Settings.Options.Views.VButtonOption");
            AddType(assembly, "keybinding", "Awaken.TG.Main.Settings.Options.Views.VKeyBinding");
            AddType(assembly, "raw", "Awaken.TG.Main.Settings.Options.Views.VNewKeyBinding");
            AddType(assembly, "bug", "Awaken.TG.Main.UI.Bugs.UserBugReporting");
            AddType(assembly, "bugView", "Awaken.TG.Main.UI.Bugs.VUserBugReporting");
            AddType(assembly, "button", "Awaken.TG.Main.UI.Components.ARButton");
            AddType(assembly, "tab", "Awaken.TG.Main.Settings.Windows.SettingsTabButton");
            AddType(assembly, "layerStack", "Awaken.TG.Main.UI.Components.PadShortcuts.ShortcutsLayerStack");
            AddType(assembly, "layer", "Awaken.TG.Main.UI.Components.PadShortcuts.ShortcutsLayer");
            var model = _types["model"];
            Prop("model", "MainView"); Prop("model", "Views");
            foreach (var name in new[] { "IsFullyInitialized", "WasDiscarded", "IsBeingDiscarded" }) Prop("model", name, typeof(bool));
            Prop("layerStack", "TopLayer"); Prop("layer", "Model");
            var world = assembly.GetType("Awaken.TG.MVC.World", true);
            var any = world.GetMethods().Single(m => m.Name == "Any" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0 && m.ReturnType == m.GetGenericArguments()[0]);
            foreach (var name in new[] { "menu", "settings", "consent", "popup", "bug" }) _find.Add(name, any.MakeGenericMethod(_types[name]));
            var services = assembly.GetType("Awaken.TG.MVC.Services", true);
            _properties.Add("world.Services", world.GetProperty("Services") ?? throw new MissingMemberException(world.FullName, "Services"));
            _getLayers = services.GetMethods().Single(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(_types["layerStack"]);
            foreach (var name in new[] { "startGame", "continueGame", "switchSaveSlot", "journal", "options", "leaderboards", "exitGame" }) Field("menuView", name, _types["button"]);
            Field("menuView", "content", typeof(CanvasGroup));
            foreach (var name in new[] { "apply", "cancel", "restoreDefaults" }) Field("settingsView", name, _types["button"]);
            Field("settingsView", "_tabs", _types["tab"].MakeArrayType()); Field("settings", "_spawnedSettings");
            Field("toggle", "displayName", typeof(TextMeshProUGUI)); Field("toggle", "toggle", typeof(Toggle)); Field("toggle", "_option");
            Field("optionButton", "displayName", typeof(TextMeshProUGUI)); Field("optionButton", "button", _types["button"]); Field("optionButton", "_option");
            Field("keybinding", "displayName", typeof(TextMeshProUGUI)); Field("keybinding", "binding", typeof(TextMeshProUGUI));
            Field("keybinding", "button", _types["button"]); Prop("keybinding", "Option");
            Field("tab", "button", typeof(Button)); Field("tab", "selectedText", typeof(TextMeshProUGUI));
            Prop("tab", "Type"); Prop("tab", "IsActive", typeof(bool));
            Field("consentView", "text", typeof(TextMeshProUGUI));
            foreach (var name in new[] { "accept", "cancel" }) Field("consentView", name, _types["button"]);
            Field("popupView", "smallText", typeof(TextMeshProUGUI)); Field("popupView", "buttonParent", typeof(Transform));
            foreach (var name in new[] { "title", "description" }) Field("bugView", name, typeof(TMP_InputField));
            Field("bugView", "closeButton", _types["button"]);
            _tabEnum = assembly.GetType("Awaken.TG.Main.Settings.Windows.SettingsTabType", true);
            foreach (var name in TabNames) if (_tabEnum.GetField(name, BindingFlags.Public | BindingFlags.Static) == null)
                throw new MissingFieldException(_tabEnum.FullName, name);
            var evt = assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true);
            _buttonHandle = RequireMethod(_types["button"], "Handle", evt);
            _closeSettings = RequireMethod(_types["settings"], "Close");
            _submit = Activator.CreateInstance(assembly.GetType("Awaken.TG.MVC.UI.Events.UISubmitAction", true));
            InitializeSettings(assembly);
            InitializePath(assembly);
            InitializeGameText(assembly);
            InitializeText();
            InitializeCombat(assembly);
            InitializeExploration(assembly);
        }

        public void Register(ScreenManager screens)
        {
            screens.Register(new Recipe(this, "menu", 0));
            screens.Register(new Recipe(this, "exploration", 2));
            screens.Register(new Recipe(this, "locationBrief", 7));
            foreach (var kind in PathScreens) screens.Register(new Recipe(this, kind,
                kind == "tutorial" ? 1 : kind == "mode" ? 4 : kind == "class" ? 6 : kind == "run" ? 7 :
                kind == "fightEntry" ? 8 : kind == "story" ? 9 : kind == "slots" ? 12 : kind == "creator" ? 13 :
                kind == "video" ? 18 : kind == "loading" ? 25 : 19));
            foreach (var kind in CombatScreens) screens.Register(new Recipe(this, kind,
                kind == "combat" ? 8 : kind == "result" ? 9 : kind == "retain" ? 12 : kind == "target" ? 15 :
                kind == "runeDeck" ? 16 : kind == "choose" ? 26 : kind == "cardSpec" ? 27 : kind == "combatTutorial" ? 28 : 50));
            screens.Register(new Recipe(this, "settings", 60));
            screens.Register(new Recipe(this, "consent", 70));
            screens.Register(new Recipe(this, "popup", 80));
            screens.Register(new Recipe(this, "bug", 85));
            screens.Register(new Recipe(this, "raw", 90));
            screens.Register(new Recipe(this, "unmapped", 95));
            screens.Register(new Recipe(this, "help", 100));
        }

        public bool Capturing()
        {
            // TMP starts editing on the next Update; stop navigation in the opening frame too.
            if (_pendingTextFrame == Time.frameCount) return true;
            if (UnsupportedModal()) return true;
            if (RawView() != null) return true;
            var bug = View("bug");
            if (bug != null && _types["bugView"].IsInstanceOfType(bug))
                foreach (var name in new[] { "title", "description" })
                {
                    var field = ReadField("bugView", name, bug) as TMP_InputField;
                    if (field == null || !field.isActiveAndEnabled || !field.isFocused) continue;
                    var events = EventSystem.current;
                    var state = name + "; focused=True; selected=" + (events != null && events.currentSelectedGameObject == field.gameObject) +
                        "; readOnly=" + field.readOnly + "; length=" + field.text.Length;
                    if (state != _lastTextState) { _lastTextState = state; _log.Write("input.text-state", state); }
                    return true;
                }
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected == null) return false;
            var tmp = selected.GetComponent<TMP_InputField>();
            var legacy = selected.GetComponent<InputField>();
            return (tmp != null && tmp.isActiveAndEnabled && tmp.isFocused) || (legacy != null && legacy.isActiveAndEnabled && legacy.isFocused);
        }

        private object Find(string kind)
        {
            if (World == null) return null;
            if (kind == "cardSpec") return All(kind).FirstOrDefault(model => ModelLive(model) && ReadProp("cardSpec", "Host", model) == null &&
                (ViewOf(model, _types["cardSpecView"]) != null || ViewOf(model, _types["cardSpecUi"]) != null));
            return _find[kind].Invoke(World, null);
        }
        private Component View(string kind)
        {
            var model = Find(kind);
            if (model == null || !(bool)ReadProp("model", "IsFullyInitialized", model) || (bool)ReadProp("model", "WasDiscarded", model) ||
                (bool)ReadProp("model", "IsBeingDiscarded", model)) return null;
            // TitleScreenUI spawns the menu with isMainView=false. Resolve the
            // exact researched view from the model's live collection instead.
            return LiveViewSelection.Find((IEnumerable)ReadProp("model", "Views", model),
                _types[kind + "View"], view => Visible(view as Component)) as Component;
        }
        private Component RawView()
        {
            var settings = Find("settings");
            if (settings == null) return null;
            foreach (var view in (IEnumerable)ReadProp("model", "Views", settings))
                if (_types["raw"].IsInstanceOfType(view) && Visible(view as Component)) return view as Component;
            return null;
        }
        private object TopLayerModel()
        {
            // ScenePreloader owns just LoadingScreenUI and has no shortcut/menu services.
            if (HasLoadingScreen(World)) return null;
            var services = _properties["world.Services"].GetValue(World);
            var layers = _getLayers.Invoke(services, null);
            return ReadProp("layer", "Model", ReadProp("layerStack", "TopLayer", layers));
        }
        private bool Active(string kind)
        {
            if (World == null) return false;
            if (kind == "help") return HelpOpen;
            if (kind == "raw") return RawView() != null;
            if (kind == "unmapped") return UnsupportedModal();
            if (ExplorationScreens.Contains(kind)) return ExplorationActive(kind);
            if (CombatScreens.Contains(kind)) return CombatActive(kind);
            if (PathScreens.Contains(kind)) return PathActive(kind);
            var view = View(kind);
            if (view == null || !_types[kind + "View"].IsInstanceOfType(view)) return false;
            if (kind == "menu")
            {
                var content = (CanvasGroup)ReadField("menuView", "content", view);
                return content != null && content.alpha > 0f;
            }
            return true;
        }

        private bool UnsupportedModal()
        {
            if (World == null) return false;
            var top = TopLayerModel();
            return top != null && !new[] { "menu", "settings", "consent", "popup", "bug" }.Concat(PathScreens.Where(k => k != "tutorial")).Concat(CombatScreens).Any(kind => ReferenceEquals(top, Find(kind)))
                && !KnownCombatOwner(top)
                && !ReferenceEquals(top, Find("locationBrief"))
                && !All("storyOnTop").Any(model => ReferenceEquals(model, top));
        }
        private bool CanAct(string kind)
        {
            if (ExplorationScreens.Contains(kind)) return ExplorationCanAct(kind);
            if (CombatScreens.Contains(kind)) return CombatCanAct(kind);
            if (!Active(kind) || Capturing() || HelpOpen || _actionScreen != kind) return false;
            if (kind == "loading") return View("popup") == null && View("settings") == null;
            var top = TopLayerModel();
            return (top == null || ReferenceEquals(top, Find(kind)) ||
                (kind == "run" && ReferenceEquals(top, Find("class"))) ||
                (kind == "story" && All("storyOnTop").Any(model => ReferenceEquals(model, top)))) &&
                (kind != "menu" || (View("settings") == null && View("consent") == null && View("popup") == null)) &&
                (kind != "settings" || View("popup") == null);
        }

        private static readonly string[] TabNames = { "GeneralSettings", "GameplaySettings", "AudioSettings", "GraphicSettings", "ControlsSettings", "GamepadControlsSettings" };
        private void Build(string kind, GraphBuilder builder)
        {
            if (!Active(kind)) return;
            if (kind == "raw" || kind == "unmapped") return;
            if (ExplorationScreens.Contains(kind)) { BuildExploration(kind, builder); return; }
            if (BuildCombat(kind, builder)) return;
            if (BuildPath(kind, builder)) return;
            if (kind == "help")
            {
                builder.Add(ControlId.Structural("help.text"), new NodeVtable(new NodeAnnouncement(() => _text.Get("input.help.text"))));
                if (_helpSource == "exploration" || _helpSource == "locationBrief")
                    builder.Add(ControlId.Structural("help.exploration"), new NodeVtable(new NodeAnnouncement(() => _text.Get("exploration.help"))));
                builder.Add(ControlId.Structural("help.close"), new NodeVtable(new NodeAnnouncement(() => _text.Get("input.help.close"))) { OnActivate = () => HelpOpen = false });
                return;
            }
            var view = View(kind);
            var model = Find(kind);
            if (kind == "settings")
            {
                builder.BeginStop("tabs");
                foreach (Component tab in (IEnumerable)ReadField("settingsView", "_tabs", view))
                {
                    if (!Visible(tab)) continue;
                    var type = ReadProp("tab", "Type", tab);
                    var name = TabNames.FirstOrDefault(n => ReferenceEquals(_tabEnum.GetField(n).GetValue(null), type));
                    if (name == null) continue;
                    var button = (Button)ReadField("tab", "button", tab);
                    if (!Visible(button)) continue;
                    // The inactive tab is an icon. Its verified type supplies the mod's localized label.
                    builder.Add(ControlId.Referenced(type, "tab." + name), new NodeVtable(
                        new NodeAnnouncement(() => _text.Get("tab." + name), "label"),
                        new NodeAnnouncement(() => (bool)ReadProp("tab", "IsActive", tab) ? _text.Get("state.selected") : null, "selected"))
                        { OnActivate = () => { if (CanAct(kind) && Actionable(button) && button.IsInteractable()) Invoke("tab." + name, () => button.OnSubmit(new BaseEventData(EventSystem.current))); } });
                }
                builder.BeginStop("values");
                var visited = new HashSet<object>();
                foreach (Component setting in (IEnumerable)ReadField("settings", "_spawnedSettings", model))
                    BuildSetting(builder, setting, visited);
                builder.BeginStop("commands");
                Buttons(builder, kind, view, new[] { "apply", "cancel", "restoreDefaults" });
                return;
            }
            if (kind == "bug")
            {
                foreach (var name in new[] { "title", "description" })
                {
                    var field = (TMP_InputField)ReadField("bugView", name, view);
                    AddTextField(builder, kind, field, ControlId.Referenced(model, "bug." + name), "text." + name);
                }
                Buttons(builder, kind, view, new[] { "closeButton" });
                return;
            }
            if (kind == "popup" || kind == "consent")
            {
                var label = (TextMeshProUGUI)ReadField(kind + "View", kind == "popup" ? "smallText" : "text", view);
                if (Visible(label)) builder.Add(ControlId.Referenced(model, kind + ".text"), new NodeVtable(new NodeAnnouncement(() => label.text)));
                if (kind == "popup")
                {
                    var parent = (Transform)ReadField("popupView", "buttonParent", view);
                    foreach (var button in parent.GetComponentsInChildren(_types["button"], false)) AddButton(builder, kind, button, ControlId.ForObject(button), "popup.choice");
                }
                else { ConsentLinks(builder, view); Buttons(builder, kind, view, new[] { "cancel", "accept" }); }
                return;
            }
            Buttons(builder, kind, view, new[] { "startGame", "continueGame", "switchSaveSlot", "journal", "options", "leaderboards", "exitGame" });
        }

        private void Buttons(GraphBuilder builder, string kind, Component view, IEnumerable<string> fields)
        {
            foreach (var name in fields) AddButton(builder, kind, (Component)ReadField(kind + "View", name, view), ControlId.Structural(kind + "." + name), kind + "." + name);
        }
        private void AddButton(GraphBuilder builder, string kind, Component button, ControlId id, string actionId)
        {
            if (!Visible(button)) return;
            var label = button.GetComponentsInChildren<TextMeshProUGUI>(false).FirstOrDefault(Visible);
            string labelKey;
            ButtonLabels.TryGetValue(actionId, out labelKey);
            if ((label == null || string.IsNullOrWhiteSpace(label.text)) && labelKey == null) return;
            builder.Add(id, new NodeVtable(new NodeAnnouncement(() => Visible(label) && !string.IsNullOrWhiteSpace(label.text) ? label.text : labelKey == null ? null : _text.Get(labelKey), "label"),
                new NodeAnnouncement(() => _text.Get("role.button"), "role"),
                new NodeAnnouncement(() => ((Selectable)button).IsInteractable() ? null : _text.Get("state.disabled"), "enabled", true))
                { OnActivate = () => ActivateButton(kind, button, actionId) });
        }
        private void ActivateButton(string kind, Component button, string actionId)
        {
            if (!CanAct(kind) || !Actionable(button) || !((Selectable)button).IsInteractable()) return;
            // The callback runs inside AccessibilityRuntime's ownership scope. No game state writes.
            Invoke(actionId, () => _buttonHandle.Invoke(button, new[] { _submit }));
        }
        private void ActivateToggle(string kind, Toggle toggle)
        {
            if (!CanAct(kind) || !Actionable(toggle) || !toggle.IsInteractable()) return;
            bool before = toggle.isOn;
            Invoke("settings.toggle", () => toggle.OnSubmit(new BaseEventData(EventSystem.current)));
            _log.Write("input.state", "frame=" + Time.frameCount + "; toggle-before=" + before + "; toggle-after=" + toggle.isOn);
        }
        private void Invoke(string id, Action callback)
        {
            _actions++;
            _log.Write("input.original", "frame=" + Time.frameCount + "; call=" + _actions + "; control=" + id);
            callback();
        }
        private bool Back(string kind)
        {
            if (kind == "help") { HelpOpen = false; return true; }
            if (ExplorationScreens.Contains(kind)) return ExplorationBack(kind);
            if (CombatScreens.Contains(kind)) return CombatBack(kind);
            if (PathBack(kind)) return true;
            if (kind != "settings" || !CanAct(kind)) return false;
            Invoke("settings.back", () => _closeSettings.Invoke(Find("settings"), null));
            return true;
        }
        private void AddType(Assembly assembly, string alias, string name) => _types.Add(alias, assembly.GetType(name, true));
        private void Field(string alias, string name, Type expected = null)
        {
            var field = _types[alias].GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || (expected != null && field.FieldType != expected)) throw new MissingFieldException(_types[alias].FullName, name);
            _fields.Add(alias + "." + name, field);
        }
        private void Prop(string alias, string name, Type expected = null)
        {
            var property = _types[alias].GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || (expected != null && property.PropertyType != expected)) throw new MissingMemberException(_types[alias].FullName, name);
            _properties.Add(alias + "." + name, property);
        }
        private object ReadField(string alias, string name, object owner) => _fields[alias + "." + name].GetValue(owner);
        private object ReadProp(string alias, string name, object owner) => _properties[alias + "." + name].GetValue(owner);
        internal static MethodInfo RequireMethod(Type type, string name, params Type[] arguments)
        { return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null) ?? throw new MissingMethodException(type.FullName, name); }
        internal static bool Visible(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy || (component is Behaviour behaviour && !behaviour.isActiveAndEnabled)) return false;
            for (var parent = component.transform; parent != null; parent = parent.parent)
            {
                var canvas = parent.GetComponent<Canvas>();
                if (canvas != null && !canvas.isActiveAndEnabled) return false;
                var groups = parent.GetComponents<CanvasGroup>();
                foreach (var group in groups) if (group.alpha <= 0f) return false;
            }
            return !(component is TMP_Text text) || text.alpha > 0f;
        }
        private static bool Actionable(Component component)
        {
            if (!Visible(component)) return false;
            for (var parent = component.transform; parent != null; parent = parent.parent)
                foreach (var group in parent.GetComponents<CanvasGroup>()) if (!group.interactable) return false;
            return true;
        }

        private sealed class Recipe : AccessibleScreen
        {
            private readonly TitleUiBindings _owner;
            private readonly string _kind;
            private readonly int _layer;
            public Recipe(TitleUiBindings owner, string kind, int layer) { _owner = owner; _kind = kind; _layer = layer; }
            public override string Id => "title." + _kind;
            public override string ScreenName => _owner._text.Get("screen." + _kind);
            public override int Layer => _layer;
            public override bool Exclusive => _kind == "help";
            public override bool CapturesRawInput => _kind == "raw" || _kind == "unmapped" || _kind == "video";
            public override bool StartUnfocused => _kind == "tutorial" || _kind == "fightEntry" || _kind == "exploration";
            public override string UnfocusedAnnouncement => _kind == "exploration" ? _owner._text.Get("exploration.unfocused") : base.UnfocusedAnnouncement;
            public override void OnFocus() { _owner._actionScreen = _kind; }
            public override void OnUnfocus() { if (_owner._actionScreen == _kind) _owner._actionScreen = null; }
            public override bool IsActive() => _owner.Active(_kind);
            public override void Build(GraphBuilder builder) => _owner.Build(_kind, builder);
            public override bool OnBack() => _owner.Back(_kind);
            public override bool OnContextualInput(KeyChord chord)
            {
                if (_owner.CombatInfoInput(_kind, chord)) return true;
                if (chord.Key != UiKey.F1 || chord.Modifiers != Modifiers.Control || chord.IsRepeat) return false;
                _owner.HelpOpen = true;
                _owner._helpSource = _kind;
                return true;
            }
        }
    }
}
