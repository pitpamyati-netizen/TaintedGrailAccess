using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Rewired;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>
    /// Fingerprinted menu/first-path input host. Polls at each input entry before its original,
    /// claims physical presses, and lets original UI calls through an ownership scope.
    /// </summary>
    internal sealed class TitleInputHost : IDisposable
    {
        private const string HarmonyId = "org.taintedgrailaccess.title-input";
        private static TitleInputHost _owner;
        private readonly Harmony _harmony;
        private readonly AccessibilityRuntime _runtime;
        private readonly TitleUiBindings _bindings;
        private readonly PhysicalInputGate _gate = new PhysicalInputGate();
        private readonly IRuntimeLog _log;
        private readonly FaultBoundary _faults;
        private readonly PropertyInfo _world;
        private readonly PropertyInfo _discarded;
        private readonly PropertyInfo _sceneWorld;
        private readonly PropertyInfo _sceneReady;
        private readonly PropertyInfo _loaderWorld;
        private readonly FieldInfo _ready;
        private readonly PropertyInfo _player;
        private readonly Type _submit;
        private readonly Type _navigation;
        private readonly FieldInfo _direction;
        private readonly object _left;
        private readonly object _right;
        private readonly object _accept;
        private readonly object _continueTmp;
        private readonly object _continueLegacy;
        private WeakReference _title;
        private WeakReference _scene;
        private WeakReference _loader;
        private int _lastFrame = -1;
        private int _traceCount;
        private bool _disposed;
        private bool _failed;
        private string _lastSnapshot;

        private static readonly KeyCode[] Keys = { KeyCode.UpArrow, KeyCode.RightArrow, KeyCode.DownArrow,
            KeyCode.LeftArrow, KeyCode.Tab, KeyCode.Home, KeyCode.End, KeyCode.Return, KeyCode.KeypadEnter,
            KeyCode.Escape, KeyCode.Backspace, KeyCode.Space, KeyCode.F1 };
        private static readonly UiKey[] Logical = { UiKey.Up, UiKey.Right, UiKey.Down, UiKey.Left, UiKey.Tab,
            UiKey.Home, UiKey.End, UiKey.Enter, UiKey.Enter, UiKey.Escape, UiKey.Backspace, UiKey.Space, UiKey.F1 };

        public TitleInputHost(ISpeechOutput speech, Localizer text, IRuntimeLog log)
        {
            if (_owner != null) throw new InvalidOperationException("Title input host is already installed.");
            _log = log;
            var assembly = InspectedGameBuild.RequireAssembly();
            InspectedGameBuild.RequireInputAssemblies();
            var title = assembly.GetType("Awaken.TG.Main.UI.TitleScreen.TitleScreen", true);
            var world = assembly.GetType("Awaken.TG.MVC.World", true);
            _world = title.GetProperty("World") ?? throw new MissingMemberException(title.FullName, "World");
            _ready = title.GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(title.FullName, "_initialized");
            _discarded = world.GetProperty("Discarded") ?? throw new MissingMemberException(world.FullName, "Discarded");
            var scene = assembly.GetType("Awaken.TG.Main.Scenes.SceneConstructors.BaseScene", true);
            _sceneWorld = scene.GetProperty("World") ?? throw new MissingMemberException(scene.FullName, "World");
            _sceneReady = scene.GetProperty("IsInitialized") ?? throw new MissingMemberException(scene.FullName, "IsInitialized");
            var loader = assembly.GetType("Awaken.TG.Main.UI.RoguePreloader.ScenePreloader", true);
            _loaderWorld = loader.GetProperty("World", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(loader.FullName, "World");
            _player = assembly.GetType("Awaken.TG.Main.Utility.UI.RewiredHelper", true).GetProperty("Player", BindingFlags.Static | BindingFlags.Public)
                ?? throw new MissingMemberException("RewiredHelper", "Player");
            _submit = assembly.GetType("Awaken.TG.MVC.UI.Events.UISubmitAction", true);
            _navigation = assembly.GetType("Awaken.TG.MVC.UI.Events.UINaviAction", true);
            _direction = _navigation.GetField("direction") ?? throw new MissingFieldException(_navigation.FullName, "direction");
            _left = _direction.FieldType.GetField("Left").GetValue(null);
            _right = _direction.FieldType.GetField("Right").GetValue(null);
            var result = assembly.GetType("Awaken.TG.MVC.UI.UIResult", true);
            _accept = Enum.Parse(result, "Accept");
            _continueTmp = Enum.Parse(typeof(TMP_InputField).GetNestedType("EditState", BindingFlags.NonPublic), "Continue");
            _continueLegacy = Enum.Parse(typeof(InputField).GetNestedType("EditState", BindingFlags.NonPublic), "Continue");
            _bindings = new TitleUiBindings(assembly, text, log, speech);
            var faults = new FaultBoundary(log);
            _faults = faults;
            var screens = new ScreenManager(faults);
            _bindings.Register(screens);
            _runtime = new AccessibilityRuntime(screens, new Navigator(new FormattedSpeechOutput(speech, _bindings.PlainGameText), text, faults, log), new InputArbitration());
            var gameUi = assembly.GetType("Awaken.TG.MVC.UI.GameUI", true);
            var evt = assembly.GetType("Awaken.TG.MVC.UI.Events.UIEvent", true);
            // Resolve all targets before the first patch; no partially installed ownership.
            var targets = new[]
            {
                Target(title, "Start", nameof(BeforeTitle)),
                Target(title, "OnGUI", nameof(BeforeTitle)),
                Target(title, "OnDestroy", nameof(DestroyTitle)),
                Target(scene, "OnGUI", nameof(BeforeScene)),
                Target(scene, "OnDestroy", nameof(DestroyScene)),
                Target(loader, "Start", nameof(BeforeLoaderStart)),
                Target(assembly.GetType("Awaken.TG.Main.UI.TitleScreen.VLoadingScreenUI", true), "Update", nameof(BeforeLoadingUpdate)),
                Target(gameUi, "PerformGUI", nameof(BeforeGameGui)),
                Target(gameUi, "HandleKeyDown", nameof(BeforeKey), typeof(Event)),
                Target(gameUi, "HandleKeyUp", nameof(BeforeKey), typeof(Event)),
                Target(gameUi, "DeliverReInputEvent", nameof(BeforeRewired), typeof(InputAction), assembly.GetType("Awaken.TG.MVC.UI.Events.UIAction", true)),
                Target(assembly.GetType("Awaken.TG.MVC.UI.Handlers.Focuses.Focus", true), "BeforeDelivery", nameof(BeforeGameSubmit), evt),
                Target(assembly.GetType("Awaken.TG.Main.UI.Components.ARButton", true), "Handle", nameof(BeforeGameSubmit), evt),
                Target(assembly.GetType("Awaken.TG.Main.Classes.VRuneSelection", true), "Handle", nameof(BeforeGameSubmit), evt),
                Target(assembly.GetType("Awaken.TG.Main.Heroes.Fights.VFightTargeting", true), "Handle", nameof(BeforeGameSubmit), evt),
                Target(assembly.GetType("Awaken.TG.Main.Heroes.CombatItems.VFightCombatItem", true), "Handle", nameof(BeforeGameSubmit), evt),
                Target(assembly.GetType("Awaken.TG.Main.Fights.Rewards.EndPanels.Stats.VFightEndVictorySummary", true), "Handle", nameof(BeforeGameSubmit), evt),
                Target(typeof(EventSystem), "Update", nameof(BeforeEventSystem)),
                Target(typeof(StandaloneInputModule), "Process", nameof(BeforeEventSystem)),
                Target(typeof(Button), "OnSubmit", nameof(BeforeToolkitSubmit), typeof(BaseEventData)),
                Target(typeof(Toggle), "OnSubmit", nameof(BeforeToolkitSubmit), typeof(BaseEventData)),
                Target(typeof(Selectable), "OnMove", nameof(BeforeToolkitMove), typeof(AxisEventData)),
                Target(typeof(StandaloneInputModule), "SendUpdateEventToSelectedObject", nameof(BeforeToolkitUpdate)),
                Target(typeof(TMP_InputField), "KeyPressed", nameof(BeforeTmpKey), typeof(Event)),
                Target(typeof(InputField), "KeyPressed", nameof(BeforeLegacyKey), typeof(Event)),
            };
            _harmony = new Harmony(HarmonyId);
            try
            {
                _owner = this;
                foreach (var target in targets) _harmony.Patch(target.Item1, prefix: new HarmonyMethod(typeof(TitleInputHost), target.Item2) { priority = Priority.First });
                _harmony.Patch(TitleUiBindings.RequireMethod(typeof(TMP_InputField), "KeyPressed", typeof(Event)),
                    postfix: new HarmonyMethod(typeof(TitleInputHost), nameof(AfterTextKey)));
                Trace("installed; fingerprint matched; scoped title input; no engine Input patches");
            }
            catch { Dispose(); throw; }
        }

        private static Tuple<MethodInfo, string> Target(Type type, string method, string hook, params Type[] args)
        { return Tuple.Create(TitleUiBindings.RequireMethod(type, method, args), hook); }
        private static void BeforeTitle(object __instance)
        {
            var owner = _owner;
            if (owner == null || owner._disposed || owner._failed) return;
            owner._title = new WeakReference(__instance);
            owner.Poll("title.OnGUI/Start");
        }
        private static void DestroyTitle(object __instance)
        {
            var owner = _owner;
            if (owner == null || owner._title == null || !ReferenceEquals(owner._title.Target, __instance)) return;
            owner._title = null;
            owner.ReleaseScene();
        }
        private static void BeforeGameGui() { _owner?.Poll("GameUI.PerformGUI"); }
        private static void BeforeScene(object __instance)
        {
            var owner = _owner;
            if (owner == null || owner._disposed || owner._failed) return;
            owner._scene = new WeakReference(__instance);
            owner.Poll("BaseScene.OnGUI");
        }
        private static void DestroyScene(object __instance)
        {
            var owner = _owner;
            if (owner == null || owner._scene == null || !ReferenceEquals(owner._scene.Target, __instance)) return;
            owner._scene = null; owner.ReleaseScene();
        }
        private static void BeforeEventSystem() { _owner?.Poll("Unity.EventSystem"); }

        private static void BeforeLoaderStart(object __instance)
        {
            if (_owner != null) _owner._loader = new WeakReference(__instance);
        }

        private static bool BeforeLoadingUpdate()
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("VLoadingScreenUI.Update");
            // This exact view polls KeyBindings.Accept directly, outside GameUI delivery.
            // Reuse live contributor checks, including independent gamepad input.
            var accept = ReInput.mapping.Actions.FirstOrDefault(action => action.name == "Accept");
            return accept == null || BeforeRewired(accept, null);
        }

        private object LiveWorld()
        { return LiveUiWorldSelection.Resolve(LoadingWorld, SceneWorld, TitleWorld); }

        private object LoadingWorld()
        {
            var loader = _loader == null ? null : _loader.Target as Behaviour;
            var loadingWorld = loader == null || !loader.isActiveAndEnabled ? null : _loaderWorld.GetValue(loader);
            return loadingWorld != null && !(bool)_discarded.GetValue(loadingWorld) && _bindings.HasLoadingScreen(loadingWorld) ? loadingWorld : null;
        }

        private object SceneWorld()
        {
            var scene = _scene == null ? null : _scene.Target as Behaviour;
            if (scene != null && scene.isActiveAndEnabled && (bool)_sceneReady.GetValue(scene))
            {
                var sceneWorld = _sceneWorld.GetValue(scene);
                if (sceneWorld != null && !(bool)_discarded.GetValue(sceneWorld)) return sceneWorld;
            }
            return null;
        }

        private object TitleWorld()
        {
            var title = _title == null ? null : _title.Target as Behaviour;
            if (title == null || !title.isActiveAndEnabled || !(bool)_ready.GetValue(title)) return null;
            var world = _world.GetValue(title);
            return world == null || (bool)_discarded.GetValue(world) ? null : world;
        }
        /// <summary>Also callable by the plugin's early Update. Each game path has its own earlier prefix.</summary>
        public void Poll(string source)
        {
            // A nested original UI handler reaches our prefix during this same poll.
            // Return before entering the finally that releases the outer borrowed World.
            if (_disposed || _failed || _lastFrame == Time.frameCount) return;
            try
            {
                var world = LiveWorld();
                if (world == null) { ReleaseScene(); return; }
                _lastFrame = Time.frameCount;
                _bindings.World = world;
                var held = Keys.Where(Input.GetKey).Select(key => new HeldKey((int)key, Chord(key, CurrentModifiers()))).ToArray();
                _gate.Poll(Time.frameCount, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency, Application.isFocused, held, _runtime,
                    () => _faults.Read("title.input-capture", _bindings.Capturing, true),
                    chord => _faults.Read("text.native-command", () => _bindings.NativeTextInput(chord), true));
                var snapshot = string.Join(",", held.Select(key => key.PhysicalKey + ":" + key.Chord.Modifiers)) +
                    "; raw=" + _runtime.Input.RawCapture + "; focus=" + _runtime.Navigator.HasFocus;
                if (snapshot != _lastSnapshot)
                {
                    _lastSnapshot = snapshot;
                    Trace("poll-before=" + source + "; " + snapshot);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { _bindings.World = null; }
        }
        private void ReleaseScene()
        {
            // Both title and gameplay scenes are supported now. A consumed held Enter must
            // remain owned until release while their controllers are being replaced.
            _gate.Blur();
            _runtime.Input.Release();
            _runtime.Screens.Clear();
            _runtime.Navigator.Attach(null);
            _bindings.HelpOpen = false;
            _bindings.ClearTextFeedback();
            _bindings.ClearAuxiliaryText();
            _bindings.ClearCombat();
            _lastSnapshot = null;
            _lastFrame = -1;
        }
        private void Fail(Exception error)
        {
            _failed = true;
            _gate.Release();
            _runtime.Input.Release();
            _log.Write("binding.title-input.disabled", error.GetType().Name + ": " + error.Message + "; ordinary input restored");
        }
        private bool Available => !_disposed && !_failed && Application.isFocused && _lastFrame == Time.frameCount;
        private bool Suppress(KeyChord chord) => Available && _gate.Suppress(chord, _runtime.Input.OwnAction);
        private bool ToolkitSubmit()
        {
            if (!Available || _runtime.Input.OwnAction) return false;
            return new[] { UiKey.Enter, UiKey.Space, UiKey.Backspace, UiKey.Escape }
                .Any(key => Suppress(new KeyChord(key, CurrentModifiers())));
        }

        private static bool BeforeKey(Event __0)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("GameUI.Key");
            // The inspected HandleKeyUp only removes a key from the game's held dictionary.
            // Let it clean up even when we acquired that key after its original KeyDown.
            if (__0.type == EventType.KeyUp) return true;
            if (!owner.Suppress(Chord(__0.keyCode, EventModifiers(__0)))) return true;
            // Only our exact event is consumed; held-key state is never globally cleared.
            bool named = Array.IndexOf(Keys, __0.keyCode) >= 0;
            __0.Use();
            owner.Trace("suppressed=GameUI.Key; named=" + named);
            return false;
        }
        private static bool BeforeRewired(InputAction __0, object __1)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("GameUI.Rewired");
            if (!owner.Available || owner._runtime.Input.OwnAction) return true;
            try
            {
                var player = (Player)owner._player.GetValue(null);
                int actionId = __0.id;
                // CreateAxis creates navigation from Horizontal/Vertical even when iterating
                // another axis action. Its Data.actionId is not necessarily its physical source.
                if (owner._navigation.IsInstanceOfType(__1))
                {
                    var direction = owner._direction.GetValue(__1);
                    bool horizontal = ReferenceEquals(direction, owner._left) || ReferenceEquals(direction, owner._right);
                    var action = ReInput.mapping.Actions.Single(a => a.name == (horizontal ? "Horizontal" : "Vertical"));
                    actionId = action.id;
                }
                var contributors = new List<KeyChord>();
                foreach (var map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
                {
                    if (!map.enabled) continue;
                    foreach (var binding in map.AllMaps)
                    {
                        if (!binding.enabled || binding.actionId != actionId || binding.keyCode == KeyCode.None) continue;
                        if (!Input.GetKey(binding.keyCode) && !Input.GetKeyUp(binding.keyCode)) continue;
                        if (!ModifierMatches(binding.modifierKey1) || !ModifierMatches(binding.modifierKey2) || !ModifierMatches(binding.modifierKey3)) continue;
                        contributors.Add(Chord(binding.keyCode, CurrentModifiers()));
                    }
                }
                // Never suppress an independent physical controller's simultaneous contribution.
                bool otherController = player.controllers.GetLastActiveController()?.type == ControllerType.Joystick;
                bool exclusive = owner._runtime.Input.Exclusive && !owner._runtime.Input.RawCapture;
                bool suppress = (exclusive || !otherController) && owner._gate.SuppressMappedAction(contributors, owner._runtime.Input.OwnAction);
                if (suppress) owner.Trace("suppressed=Rewired; action=" + __0.name);
                return !suppress;
            }
            catch (Exception error) { owner.Fail(error); return true; }
        }
        private static bool BeforeGameSubmit(object __0, ref object __result)
        {
            var owner = _owner;
            if (owner == null || !owner._submit.IsInstanceOfType(__0)) return true;
            owner.Poll("game.Submit");
            if (!owner.ToolkitSubmit()) return true;
            __result = owner._accept;
            owner.Trace("suppressed=game.Submit");
            return false;
        }
        private static bool BeforeToolkitSubmit(BaseEventData __0)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("Unity.Submit");
            if (!owner.ToolkitSubmit()) return true;
            __0?.Use(); owner.Trace("suppressed=Unity.Submit"); return false;
        }
        private static bool BeforeToolkitMove(AxisEventData __0)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("Unity.Move");
            var key = __0.moveDir == MoveDirection.Up ? UiKey.Up : __0.moveDir == MoveDirection.Down ? UiKey.Down :
                __0.moveDir == MoveDirection.Left ? UiKey.Left : __0.moveDir == MoveDirection.Right ? UiKey.Right : UiKey.None;
            if (!owner.Suppress(new KeyChord(key, CurrentModifiers()))) return true;
            __0.Use(); owner.Trace("suppressed=Unity.Move"); return false;
        }
        private static bool BeforeToolkitUpdate(ref bool __result)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("Unity.UpdateSelected");
            if (!owner.Suppress(new KeyChord(UiKey.None))) return true;
            __result = true; return false;
        }
        private static bool BeforeTmpKey(TMP_InputField __instance, Event __0, ref object __result)
        {
            return BeforeTextKey(__0, ref __result, true);
        }
        private static void AfterTextKey() { _owner?.ObserveText(); }
        private bool WithWorld(Func<bool> action)
        {
            if (_disposed || _failed || !Application.isFocused) return false;
            // Nested editor callbacks borrow the existing callback scope without clearing it.
            if (_bindings.World != null) return action();
            try { _bindings.World = LiveWorld(); return _bindings.World != null && action(); }
            catch (Exception error) { _faults.Report("text.native", error); return false; }
            finally { _bindings.World = null; }
        }
        public void ObserveText() { WithWorld(() => { _bindings.ObserveText(); _bindings.ObserveAuxiliaryText(); _bindings.ObserveCombat(); return true; }); }
        private static bool BeforeLegacyKey(Event __0, ref object __result)
        { return BeforeTextKey(__0, ref __result, false); }
        private static bool BeforeTextKey(Event evt, ref object result, bool tmp)
        {
            var owner = _owner;
            if (owner == null) return true;
            owner.Poll("Unity.TextKey");
            if (!owner.Suppress(Chord(evt.keyCode, EventModifiers(evt)))) return true;
            result = tmp ? owner._continueTmp : owner._continueLegacy;
            // The opening Enter cannot also submit/finish the native field. Fresh typing passes.
            owner.Trace("suppressed=Unity.TextKey; owned-opening-or-held-key");
            return false;
        }
        private static KeyChord Chord(KeyCode key, Modifiers modifiers)
        {
            var index = Array.IndexOf(Keys, key);
            // Unknown physical keys are only suppressed by explicit mod-modal exclusivity.
            return new KeyChord(index < 0 ? UiKey.None : Logical[index], modifiers);
        }
        private static Modifiers CurrentModifiers()
        {
            var modifiers = Modifiers.None;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) modifiers |= Modifiers.Shift;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) modifiers |= Modifiers.Control;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) modifiers |= Modifiers.Alt;
            return modifiers;
        }
        private static Modifiers EventModifiers(Event evt) => (evt.shift ? Modifiers.Shift : Modifiers.None) |
            (evt.control ? Modifiers.Control : Modifiers.None) | (evt.alt ? Modifiers.Alt : Modifiers.None);
        private static bool ModifierMatches(ModifierKey modifier)
        {
            var current = CurrentModifiers();
            return modifier == ModifierKey.None || (modifier == ModifierKey.Shift && (current & Modifiers.Shift) != 0) ||
                (modifier == ModifierKey.Control && (current & Modifiers.Control) != 0) || (modifier == ModifierKey.Alt && (current & Modifiers.Alt) != 0);
        }
        private void Trace(string message)
        {
            if (_traceCount++ < 600) _log.Write("input.host", "frame=" + Time.frameCount + "; " + message);
        }
        public void OnWindowFocusChanged(bool focused)
        { if (!focused) { _gate.Blur(); _runtime.Input.Release(); _bindings.ClearTextFeedback(); } }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bindings.World = null; _title = null; _scene = null; _loader = null; _gate.Release();
            if (ReferenceEquals(_owner, this)) _owner = null;
            try { _harmony.UnpatchSelf(); } finally { _runtime.Dispose(); }
        }
    }
}
