using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaintedGrailAccess.Runtime;
using UnityEngine;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>
    /// Passive PressStart reader for one locally inspected build. A postfix supplies the live
    /// title-scene owner only for the duration of a tick. No input or action method is patched.
    /// </summary>
    internal sealed class StartPromptReader : IDisposable
    {
        private const string HarmonyId = "org.taintedgrailaccess.start-prompt";
        private static StartPromptReader _owner;
        private readonly Harmony _harmony;
        private readonly AccessibilityRuntime _runtime;
        private readonly FaultBoundary _faults;
        private readonly IRuntimeLog _log;
        private readonly PropertyInfo _titleWorld;
        private readonly FieldInfo _titleReady;
        private readonly PropertyInfo _worldDiscarded;
        private readonly MethodInfo _findPrompt;
        private readonly MethodInfo _findPopup;
        private readonly PropertyInfo _fullyInitialized;
        private readonly PropertyInfo _discarded;
        private readonly PropertyInfo _discarding;
        private readonly PropertyInfo _mainView;
        private readonly FieldInfo _promptText;
        private readonly FieldInfo _promptTween;
        private readonly PropertyInfo _textValue;
        private readonly PropertyInfo _textAlpha;
        private readonly Type _viewType;
        private object _frameTitle;
        private int _lastFrame = -1;
        private bool _disposed;
        private bool? _lastVisible;

        /// <summary>Fails closed before installing a hook when the build or exact members differ.</summary>
        public StartPromptReader(ISpeechOutput speech, Localizer text, IRuntimeLog log)
        {
            if (_owner != null) throw new InvalidOperationException("A start reader is already installed.");
            _log = log;
            _faults = new FaultBoundary(log);
            var assembly = InspectedGameBuild.RequireAssembly();

            var title = assembly.GetType("Awaken.TG.Main.UI.TitleScreen.TitleScreen", true);
            var world = assembly.GetType("Awaken.TG.MVC.World", true);
            var model = assembly.GetType("Awaken.TG.MVC.Model", true);
            var prompt = assembly.GetType("Awaken.TG.Main.UI.TitleScreen.PressStartUI", true);
            var popup = assembly.GetType("Awaken.TG.Main.UI.Popup.PopupUI", true);
            _viewType = assembly.GetType("Awaken.TG.Main.UI.TitleScreen.VPressStartUI", true);
            _titleWorld = Property(title, "World", world);
            _titleReady = Field(title, "_initialized", typeof(bool));
            _worldDiscarded = Property(world, "Discarded", typeof(bool));
            var any = world.GetMethods(BindingFlags.Instance | BindingFlags.Public).Single(m =>
                m.Name == "Any" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 &&
                m.GetParameters().Length == 0 && m.ReturnType == m.GetGenericArguments()[0]);
            _findPrompt = any.MakeGenericMethod(prompt);
            _findPopup = any.MakeGenericMethod(popup);
            _fullyInitialized = Property(model, "IsFullyInitialized", typeof(bool));
            _discarded = Property(model, "WasDiscarded", typeof(bool));
            _discarding = Property(model, "IsBeingDiscarded", typeof(bool));
            _mainView = Property(model, "MainView", assembly.GetType("Awaken.TG.MVC.View", true));
            _promptText = _viewType.GetField("text", BindingFlags.Instance | BindingFlags.Public);
            if (_promptText == null || _promptText.FieldType.FullName != "TMPro.TextMeshProUGUI")
                throw new MissingFieldException(_viewType.FullName, "text: TMPro.TextMeshProUGUI");
            _promptTween = _viewType.GetField("_animTween", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_promptTween == null || _promptTween.FieldType.FullName != "DG.Tweening.Tween")
                throw new MissingFieldException(_viewType.FullName, "_animTween: DG.Tweening.Tween");
            _textValue = Property(_promptText.FieldType, "text", typeof(string));
            _textAlpha = Property(_promptText.FieldType, "alpha", typeof(float));
            var tick = title.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (tick == null || tick.ReturnType != typeof(void))
                throw new MissingMethodException(title.FullName, "void LateUpdate()");

            var screens = new ScreenManager(_faults);
            screens.Register(new PassiveTextScreen("game.press-start", text.Get("screen.pressStart"), Read));
            _runtime = new AccessibilityRuntime(screens, new Navigator(speech, text, _faults, log), new InputArbitration());
            _harmony = new Harmony(HarmonyId);
            try
            {
                _owner = this;
                _harmony.Patch(tick, postfix: new HarmonyMethod(typeof(StartPromptReader), nameof(AfterTitleFrame)));
                _log.Write("binding.press-start", "Fingerprint matched; passive LateUpdate observer installed; no input claims or game actions.");
            }
            catch
            {
                if (ReferenceEquals(_owner, this)) _owner = null;
                _harmony.UnpatchSelf();
                _runtime.Dispose();
                throw;
            }
        }

        private static void AfterTitleFrame(object __instance)
        {
            // This is an observation postfix, not an input poller. The game finishes its tick first.
            _owner?.Tick(__instance);
        }

        private void Tick(object title)
        {
            if (_disposed || _lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            _frameTitle = title;
            try
            {
                _faults.Run("game.press-start.tick", () =>
                {
                    bool visible = Read() != null;
                    if (_lastVisible != visible)
                    {
                        _lastVisible = visible;
                        _log.Write("screen.press-start", visible ? "visible" : "not-visible");
                    }
                    // The empty input stream deliberately preserves the game's "any button" handling.
                    _runtime.Update(Time.frameCount, Array.Empty<KeyChord>());
                });
            }
            finally { _frameTitle = null; }
        }

        private VisibleText Read()
        {
            if (_frameTitle == null || !Application.isFocused || !(bool)_titleReady.GetValue(_frameTitle)) return null;
            var title = _frameTitle as Behaviour;
            if (title == null || !title.isActiveAndEnabled) return null;
            var world = _titleWorld.GetValue(_frameTitle);
            if (world == null || (bool)_worldDiscarded.GetValue(world) || _findPopup.Invoke(world, null) != null) return null;
            var model = _findPrompt.Invoke(world, null);
            if (model == null || !(bool)_fullyInitialized.GetValue(model) || (bool)_discarded.GetValue(model) ||
                (bool)_discarding.GetValue(model)) return null;
            var view = _mainView.GetValue(model) as Behaviour;
            if (view == null || !_viewType.IsInstanceOfType(view) || !view.isActiveAndEnabled ||
                _promptTween.GetValue(view) == null) return null;
            var label = _promptText.GetValue(view) as Behaviour;
            if (label == null || !label.isActiveAndEnabled || (float)_textAlpha.GetValue(label) <= 0f) return null;
            // Check only the verified text's ancestor chain, never scan arbitrary scene objects.
            for (var parent = label.transform; parent != null; parent = parent.parent)
            {
                var canvas = parent.GetComponent<Canvas>();
                if (canvas != null && !canvas.isActiveAndEnabled) return null;
                foreach (var group in parent.GetComponents<CanvasGroup>())
                    if (group.alpha <= 0f) return null;
            }
            var value = ((string)_textValue.GetValue(label))?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : new VisibleText(model, value);
        }

        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo Property(Type type, string name, Type expected)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != expected || property.GetIndexParameters().Length != 0 ||
                property.GetGetMethod() == null) throw new MissingMemberException(type.FullName, name);
            return property;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_owner, this)) _owner = null;
            _frameTitle = null;
            try { _harmony.UnpatchSelf(); }
            finally { _runtime.Dispose(); }
        }
    }
}
