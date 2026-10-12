using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaintedGrailAccess.Runtime;
using UnityEngine;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>
    /// Opt-in, bounded observation of the inspected title scene's input paths. Every patch
    /// lets the original method run. It never claims a key, invokes a handler, or reads text input.
    /// </summary>
    internal sealed class GameInputAudit : IDisposable
    {
        private const string HarmonyId = "org.taintedgrailaccess.input-audit";
        private const int MaxRecords = 600;
        private static GameInputAudit _owner;
        private readonly Harmony _harmony;
        private readonly IRuntimeLog _log;
        private readonly FaultBoundary _faults;
        private readonly KeyboardSnapshotTracker _keyboard = new KeyboardSnapshotTracker();
        private readonly PropertyInfo _titleWorld;
        private readonly FieldInfo _titleReady;
        private readonly PropertyInfo _mainView;
        private readonly PropertyInfo _modelReady;
        private readonly PropertyInfo _discarded;
        private readonly PropertyInfo _discarding;
        private readonly PropertyInfo _views;
        private readonly PropertyInfo _actionName;
        private readonly PropertyInfo _keyCode;
        private readonly MethodInfo _findMenu;
        private readonly MethodInfo _findSettings;
        private readonly MethodInfo _findPrompt;
        private readonly MethodInfo _findConsent;
        private readonly MethodInfo _findPopup;
        private readonly Type _rawView;
        private readonly Type _submit;
        private readonly Type _keyDown;
        private readonly Type _uiAction;
        private readonly object _accept;
        private readonly FieldInfo[] _menuButtons;
        private readonly FieldInfo[] _settingsButtons;
        // This owner is present only during one original TitleScreen.OnGUI call, including
        // nested original callbacks. The Harmony finalizer clears it even on an exception.
        private object _callbackWorld;
        private int _lastFrame = -1;
        private int _records;
        private bool _recording = true;
        private bool _disposed;
        private string _lastContext;

        private static readonly KeyCode[] WatchedKeys =
        {
            KeyCode.UpArrow, KeyCode.RightArrow, KeyCode.DownArrow, KeyCode.LeftArrow,
            KeyCode.Tab, KeyCode.Home, KeyCode.End, KeyCode.Return, KeyCode.KeypadEnter,
            KeyCode.Escape, KeyCode.Backspace, KeyCode.Space, KeyCode.F1
        };
        private static readonly UiKey[] LogicalKeys =
        {
            UiKey.Up, UiKey.Right, UiKey.Down, UiKey.Left, UiKey.Tab, UiKey.Home, UiKey.End,
            UiKey.Enter, UiKey.Enter, UiKey.Escape, UiKey.Backspace, UiKey.Space, UiKey.F1
        };

        public GameInputAudit(IRuntimeLog log)
        {
            if (_owner != null) throw new InvalidOperationException("An input audit is already installed.");
            _log = log;
            _faults = new FaultBoundary(log);
            var assembly = InspectedGameBuild.RequireAssembly();
            var title = assembly.GetType("Awaken.TG.Main.UI.TitleScreen.TitleScreen", true);
            var world = assembly.GetType("Awaken.TG.MVC.World", true);
            var model = assembly.GetType("Awaken.TG.MVC.Model", true);
            var view = assembly.GetType("Awaken.TG.MVC.View", true);
            var gameUi = assembly.GetType("Awaken.TG.MVC.UI.GameUI", true);
            var uiEvent = assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true);
            var result = assembly.GetType("Awaken.TG.MVC.UI.UIResult", true);
            _accept = Enum.Parse(result, "Accept");
            _uiAction = assembly.GetType("Awaken.TG.MVC.UI.Events.UIAction", true);
            _keyDown = assembly.GetType("Awaken.TG.MVC.UI.Events.UIEKeyDown", true);
            _submit = assembly.GetType("Awaken.TG.MVC.UI.Events.UISubmitAction", true);
            _rawView = assembly.GetType("Awaken.TG.Main.Settings.Options.Views.VNewKeyBinding", true);
            _titleWorld = Property(title, "World", world);
            _titleReady = Field(title, "_initialized", typeof(bool));
            _mainView = Property(model, "MainView", view);
            _modelReady = Property(model, "IsFullyInitialized", typeof(bool));
            _discarded = Property(model, "WasDiscarded", typeof(bool));
            _discarding = Property(model, "IsBeingDiscarded", typeof(bool));
            _views = model.GetProperty("Views");
            if (_views == null || !typeof(IEnumerable).IsAssignableFrom(_views.PropertyType) || _views.GetGetMethod() == null)
                throw new MissingMemberException(model.FullName, "Views");
            _actionName = Property(_uiAction, "Name", typeof(string));
            _keyCode = Property(_keyDown, "Key", typeof(KeyCode));
            var any = world.GetMethods().Single(m => m.Name == "Any" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0 &&
                m.ReturnType == m.GetGenericArguments()[0]);
            _findMenu = any.MakeGenericMethod(assembly.GetType("Awaken.TG.Main.UI.TitleScreen.TitleScreenUI", true));
            _findSettings = any.MakeGenericMethod(assembly.GetType("Awaken.TG.Main.Settings.Windows.AllSettingsUI", true));
            _findPrompt = any.MakeGenericMethod(assembly.GetType("Awaken.TG.Main.UI.TitleScreen.PressStartUI", true));
            _findConsent = any.MakeGenericMethod(assembly.GetType("Awaken.TG.Main.UI.TitleScreen.TitlePopupUI", true));
            _findPopup = any.MakeGenericMethod(assembly.GetType("Awaken.TG.Main.UI.Popup.PopupUI", true));
            var button = assembly.GetType("Awaken.TG.Main.UI.Components.ARButton", true);
            _menuButtons = Buttons(assembly.GetType("Awaken.TG.Main.UI.TitleScreen.VTitleScreenRogueUI", true), button,
                "startGame", "continueGame", "switchSaveSlot", "journal", "options", "leaderboards", "exitGame");
            _settingsButtons = Buttons(assembly.GetType("Awaken.TG.Main.Settings.Windows.VSettingsUI", true), button,
                "restoreDefaults", "apply", "cancel");

            // Validate every target before installing the first hook.
            var gui = Method(title, "OnGUI", typeof(void));
            var performGui = Method(gameUi, "PerformGUI", typeof(void));
            var rawKey = Method(gameUi, "HandleKeyDown", typeof(void), typeof(Event));
            // Resolving the game's exact reference does not initialize a player or alter maps.
            var rewiredReference = assembly.GetReferencedAssemblies().Single(a => a.Name == "Rewired_Core");
            var reInput = Method(gameUi, "DeliverReInputEvent", typeof(void),
                Assembly.Load(rewiredReference).GetType("Rewired.InputAction", true), _uiAction);
            var handleButton = Method(button, "Handle", result, uiEvent);
            var focus = Method(assembly.GetType("Awaken.TG.MVC.UI.Handlers.Focuses.Focus", true), "BeforeDelivery", result, uiEvent);
            var rawCapture = Method(_rawView, "Handle", result, uiEvent);
            _harmony = new Harmony(HarmonyId);
            try
            {
                _owner = this;
                _harmony.Patch(gui, prefix: Hook(nameof(BeginTitleGui)), finalizer: Hook(nameof(EndTitleGui)));
                _harmony.Patch(performGui, prefix: Hook(nameof(BeforeGameGui)));
                _harmony.Patch(rawKey, prefix: Hook(nameof(BeforeRawKey)));
                _harmony.Patch(reInput, prefix: Hook(nameof(BeforeReInput)));
                _harmony.Patch(handleButton, prefix: Hook(nameof(BeforeButton)), postfix: Hook(nameof(AfterButton)));
                _harmony.Patch(focus, prefix: Hook(nameof(BeforeFocus)), postfix: Hook(nameof(AfterFocus)));
                _harmony.Patch(rawCapture, prefix: Hook(nameof(BeforeRawCapture)));
                Record("installed; observation only; no claims, suppression or game actions");
            }
            catch
            {
                if (ReferenceEquals(_owner, this)) _owner = null;
                _harmony.UnpatchSelf();
                throw;
            }
        }

        private static void BeginTitleGui(object __instance, out bool __state)
        {
            __state = false;
            var owner = _owner;
            if (owner == null || owner._disposed || !owner._recording || owner._callbackWorld != null) return;
            __state = owner._faults.Read("input-audit.begin", () => owner.Begin(__instance), false);
        }
        private bool Begin(object title)
        {
            var behaviour = title as Behaviour;
            if (behaviour == null || !behaviour.isActiveAndEnabled || !(bool)_titleReady.GetValue(title)) return false;
            var world = _titleWorld.GetValue(title);
            if (world == null) return false;
            // Set only after all fallible reads. From this point finalizer owns the reference.
            _callbackWorld = world;
            return true;
        }
        private static void EndTitleGui(bool __state)
        {
            if (__state && _owner != null) _owner._callbackWorld = null;
        }
        private static void BeforeGameGui()
        {
            var owner = Active();
            if (owner != null) owner._faults.Run("input-audit.poll", owner.Poll);
        }
        private void Poll()
        {
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            var settings = _findSettings.Invoke(_callbackWorld, null);
            bool rawCapture = false;
            if (settings != null)
                foreach (var view in (IEnumerable)_views.GetValue(settings))
                    if (_rawView.IsInstanceOfType(view) && view is Behaviour behaviour && behaviour.isActiveAndEnabled)
                        rawCapture = true;
            var context = (rawCapture ? "raw-keybinding" :
                Ready(_findPrompt.Invoke(_callbackWorld, null)) ? "press-start" :
                Ready(_findConsent.Invoke(_callbackWorld, null)) ? "consent" :
                Ready(_findPopup.Invoke(_callbackWorld, null)) ? "popup" :
                Ready(settings) ? "settings" : Ready(_findMenu.Invoke(_callbackWorld, null)) ? "menu" : "unmapped") +
                "; window-focused=" + Application.isFocused;
            if (_lastContext != context) { _lastContext = context; Record("context=" + context); }
            var modifiers = Modifiers.None;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) modifiers |= Modifiers.Shift;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) modifiers |= Modifiers.Control;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) modifiers |= Modifiers.Alt;
            var snapshot = new List<HeldKey>();
            for (int i = 0; i < WatchedKeys.Length; i++)
                if (Input.GetKey(WatchedKeys[i])) snapshot.Add(new HeldKey((int)WatchedKeys[i], new KeyChord(LogicalKeys[i], modifiers)));
            var keys = _keyboard.Sample(Time.frameCount, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency,
                Application.isFocused, rawCapture, snapshot);
            foreach (var key in keys)
                if (!key.IsRepeat) Record("poll-before-PerformGUI; key=" + key.Key + "; modifiers=" + key.Modifiers);
        }
        private bool Ready(object model)
        {
            if (model == null || !(bool)_modelReady.GetValue(model) || (bool)_discarded.GetValue(model) ||
                (bool)_discarding.GetValue(model)) return false;
            var view = _mainView.GetValue(model) as Behaviour;
            return view != null && view.isActiveAndEnabled;
        }
        private static void BeforeRawKey(Event __0)
        {
            var owner = Active();
            if (owner != null && Array.IndexOf(WatchedKeys, __0.keyCode) >= 0)
                owner.SafeRecord("raw-keydown=" + __0.keyCode);
        }
        private static void BeforeReInput(object __1) { Observe("rewired-delivery", __1); }
        private static void BeforeFocus(object __0) { Observe("focus-before", __0); }
        private static void AfterFocus(object __0, object __result)
        {
            var owner = Active();
            if (owner != null && Equals(__result, owner._accept)) Observe("focus-accepted", __0);
        }
        private static void BeforeRawCapture() { Active()?.SafeRecord("raw-keybinding-handler; key value omitted"); }
        private static void BeforeButton(object __instance, object __0) { ObserveButton("button-before", __instance, __0); }
        private static void AfterButton(object __instance, object __0, object __result)
        {
            var owner = Active();
            if (owner != null && Equals(__result, owner._accept)) ObserveButton("button-submit-accepted", __instance, __0);
        }
        private static void ObserveButton(string path, object button, object evt)
        {
            var owner = Active();
            if (owner == null || !owner._submit.IsInstanceOfType(evt)) return;
            owner._faults.Run("input-audit.button", () =>
            {
                var id = owner.ButtonId(button);
                if (id != null) owner.Record(path + "; control=" + id);
            });
        }
        private string ButtonId(object button)
        {
            var menu = _findMenu.Invoke(_callbackWorld, null);
            var settings = _findSettings.Invoke(_callbackWorld, null);
            return ButtonId(menu, _menuButtons, button, "menu.") ?? ButtonId(settings, _settingsButtons, button, "settings.");
        }
        private string ButtonId(object model, FieldInfo[] fields, object button, string prefix)
        {
            if (!Ready(model)) return null;
            var view = _mainView.GetValue(model);
            if (!fields[0].DeclaringType.IsInstanceOfType(view)) return null;
            foreach (var field in fields) if (ReferenceEquals(field.GetValue(view), button)) return prefix + field.Name;
            return null;
        }
        private static void Observe(string path, object evt)
        {
            var owner = Active();
            if (owner == null) return;
            owner._faults.Run("input-audit.event", () =>
            {
                if (owner._uiAction.IsInstanceOfType(evt))
                {
                    var name = (string)owner._actionName.GetValue(evt);
                    if (name == "Accept" || name == "Cancel" || name == "Horizontal" || name == "Vertical" ||
                        name == "DPad_Up" || name == "DPad_Down" || name == "DPad_Left" || name == "DPad_Right")
                        owner.Record(path + "; action=" + name + "; event=" + evt.GetType().Name);
                }
                else if (owner._keyDown.IsInstanceOfType(evt) &&
                    Array.IndexOf(WatchedKeys, (KeyCode)owner._keyCode.GetValue(evt)) >= 0)
                    owner.Record(path + "; named-keydown");
            });
        }
        private static GameInputAudit Active()
        {
            var owner = _owner;
            return owner != null && !owner._disposed && owner._recording && owner._callbackWorld != null ? owner : null;
        }
        private void SafeRecord(string message) { _faults.Run("input-audit.log", () => Record(message)); }
        private void Record(string message)
        {
            if (!_recording) return;
            _records++;
            // Stop even if the last write fails: a broken logger cannot defeat the limit.
            if (_records >= MaxRecords) _recording = false;
            _log.Write("input.audit", "frame=" + Time.frameCount + "; sequence=" + _records + "; " + message);
            if (!_recording)
                _log.Write("input.audit", "record-limit-reached; observation stopped; original input remains enabled");
        }
        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(typeof(GameInputAudit), name); }
        private static MethodInfo Method(Type type, string name, Type result, params Type[] arguments)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
            if (method == null || method.ReturnType != result) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static PropertyInfo Property(Type type, string name, Type expected)
        {
            var property = type.GetProperty(name);
            if (property == null || property.PropertyType != expected || property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static FieldInfo[] Buttons(Type type, Type button, params string[] names)
        { return names.Select(name => Field(type, name, button)).ToArray(); }

        /// <summary>Blur may pause GUI ticks entirely; do not rely on receiving an unfocused snapshot.</summary>
        public void OnWindowFocusChanged(bool focused)
        {
            if (_disposed || focused) return;
            _keyboard.Reset();
            _lastContext = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _callbackWorld = null;
            _keyboard.Reset();
            if (ReferenceEquals(_owner, this)) _owner = null;
            _harmony.UnpatchSelf();
        }
    }
}
