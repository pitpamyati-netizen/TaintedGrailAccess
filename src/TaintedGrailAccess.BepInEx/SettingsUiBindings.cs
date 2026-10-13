using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaintedGrailAccess.Graph;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaintedGrailAccess.BepInExHost
{
    // Only members inspected in GOG 1.61. Every view and callback is borrowed for this build.
    internal sealed partial class TitleUiBindings
    {
        private MethodInfo _moveSlider;

        private void InitializeSettings(Assembly assembly)
        {
            foreach (var pair in new[] { new[] { "slider", "VSlider" }, new[] { "enum", "VEnumArrows" },
                new[] { "dependent", "VDependentOption" }, new[] { "link", "VLinkOption" } })
                AddType(assembly, pair[0], "Awaken.TG.Main.Settings.Options.Views." + pair[1]);
            AddType(assembly, "prefOption", "Awaken.TG.Main.Settings.Options.PrefOption");
            Prop("prefOption", "ID", typeof(string));
            Prop("prefOption", "TooltipConstructor", typeof(Func<string>));
            Field("slider", "displayText", typeof(TextMeshProUGUI)); Field("slider", "slider", typeof(Slider));
            Field("slider", "selectable"); Field("slider", "_option");
            _moveSlider = RequireMethod(_types["slider"], "MoveSlider", typeof(float));
            Field("enum", "settingNameText", typeof(TextMeshProUGUI)); Field("enum", "optionNameText", typeof(TextMeshProUGUI));
            Field("enum", "leftButton", _types["button"]); Field("enum", "rightButton", _types["button"]); Field("enum", "_option");
            Field("dependent", "_baseOptionView"); Field("dependent", "_otherOptionsViews"); Field("dependent", "_option");
            AddType(assembly, "dependentOption", "Awaken.TG.Main.Settings.Options.DependentOption");
            Prop("dependentOption", "ShowDependent", typeof(bool));
            Field("link", "displayName", typeof(TextMeshProUGUI)); Field("link", "button", _types["button"]); Field("link", "_option");
        }

        private ControlId SettingId(object option)
        {
            var id = (string)ReadProp("prefOption", "ID", option);
            return string.IsNullOrEmpty(id) ? ControlId.ForObject(option) : ControlId.Referenced(option, "setting." + id);
        }

        private Action SettingTooltip(object option)
        {
            return () =>
            {
                // The game supplies already localized tooltip text; never reconstruct its meaning.
                var provider = (Func<string>)ReadProp("prefOption", "TooltipConstructor", option);
                _speech.Speak(provider == null ? _text.Get("action.noTooltip") : provider(), true);
            };
        }

        private void BuildSetting(GraphBuilder builder, Component setting, HashSet<object> visited)
        {
            if (!Visible(setting) || !visited.Add(setting)) return;
            if (_types["dependent"].IsInstanceOfType(setting))
            {
                BuildSetting(builder, ReadField("dependent", "_baseOptionView", setting) as Component, visited);
                var option = ReadField("dependent", "_option", setting);
                if (option != null && (bool)ReadProp("dependentOption", "ShowDependent", option))
                    foreach (var child in (IEnumerable)ReadField("dependent", "_otherOptionsViews", setting))
                        BuildSetting(builder, child as Component, visited);
                return;
            }
            if (_types["slider"].IsInstanceOfType(setting))
            {
                var label = (TMP_Text)ReadField("slider", "displayText", setting);
                var slider = (Slider)ReadField("slider", "slider", setting);
                var selectable = (Selectable)ReadField("slider", "selectable", setting);
                var option = ReadField("slider", "_option", setting);
                if (!Visible(label) || !Visible(slider) || option == null) return;
                Func<string> value = () => _text.Get("slider.value", slider.value.ToString("0.###", CultureInfo.CurrentCulture),
                    slider.minValue.ToString("0.###", CultureInfo.CurrentCulture), slider.maxValue.ToString("0.###", CultureInfo.CurrentCulture));
                builder.Add(SettingId(option), new NodeVtable(new NodeAnnouncement(() => label.text, "label"),
                    new NodeAnnouncement(() => _text.Get("role.slider"), "role"), new NodeAnnouncement(value, "value", true),
                    new NodeAnnouncement(() => selectable.IsInteractable() ? null : _text.Get("state.disabled"), "enabled", true))
                {
                    OnAdjust = (sign, large) =>
                    {
                        if (CanAct("settings") && Actionable(selectable) && selectable.IsInteractable())
                            // MoveSlider is the game's navigation handler, including its own step, clamp and listeners.
                            Invoke("settings.slider", () => _moveSlider.Invoke(setting, new object[] { sign * (large ? 5f : 1f) }));
                    }, StateText = value, OnTooltip = SettingTooltip(option)
                });
                return;
            }
            if (_types["enum"].IsInstanceOfType(setting))
            {
                var label = (TMP_Text)ReadField("enum", "settingNameText", setting);
                var valueText = (TMP_Text)ReadField("enum", "optionNameText", setting);
                var option = ReadField("enum", "_option", setting);
                if (!Visible(label) || !Visible(valueText) || option == null) return;
                Func<string> value = () => Visible(valueText) ? valueText.text : null;
                builder.Add(SettingId(option), new NodeVtable(new NodeAnnouncement(() => label.text, "label"),
                    new NodeAnnouncement(() => _text.Get("role.list"), "role"), new NodeAnnouncement(value, "value", true))
                {
                    OnAdjust = (sign, large) => ActivateButton("settings", (Component)ReadField("enum", sign < 0 ? "leftButton" : "rightButton", setting), "settings.list"),
                    StateText = value, OnTooltip = SettingTooltip(option)
                });
                return;
            }
            if (_types["toggle"].IsInstanceOfType(setting))
            {
                var toggle = (Toggle)ReadField("toggle", "toggle", setting);
                var label = (TMP_Text)ReadField("toggle", "displayName", setting);
                var option = ReadField("toggle", "_option", setting);
                if (!Visible(label) || !Visible(toggle) || option == null) return;
                Func<string> state = () => _text.Get(toggle.isOn ? "state.on" : "state.off");
                builder.Add(SettingId(option), new NodeVtable(new NodeAnnouncement(() => label.text, "label"),
                    new NodeAnnouncement(() => _text.Get("role.toggle"), "role"), new NodeAnnouncement(state, "value", true),
                    new NodeAnnouncement(() => toggle.IsInteractable() ? null : _text.Get("state.disabled"), "enabled", true))
                    { OnActivate = () => ActivateToggle("settings", toggle), StateText = state, OnTooltip = SettingTooltip(option) });
                return;
            }
            var kind = _types["keybinding"].IsInstanceOfType(setting) ? "keybinding" :
                _types["optionButton"].IsInstanceOfType(setting) ? "optionButton" : _types["link"].IsInstanceOfType(setting) ? "link" : null;
            if (kind == null) return;
            var button = (Component)ReadField(kind, "button", setting);
            var display = (TMP_Text)ReadField(kind, "displayName", setting);
            var pref = kind == "keybinding" ? ReadProp(kind, "Option", setting) : ReadField(kind, "_option", setting);
            if (!Visible(button) || !Visible(display) || pref == null) return;
            var binding = kind == "keybinding" ? (TMP_Text)ReadField(kind, "binding", setting) : null;
            builder.Add(SettingId(pref), new NodeVtable(new NodeAnnouncement(() => display.text, "label"),
                new NodeAnnouncement(() => _text.Get("role.button"), "role"),
                new NodeAnnouncement(() => Visible(binding) ? binding.text : null, "value", true),
                new NodeAnnouncement(() => ((Selectable)button).IsInteractable() ? null : _text.Get("state.disabled"), "enabled", true))
                { OnActivate = () => ActivateButton("settings", button, "settings." + kind), OnTooltip = SettingTooltip(pref) });
        }
    }
}
