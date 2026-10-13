using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.BepInExHost
{
    internal sealed partial class TitleUiBindings
    {
        private MethodInfo _getTextMapping;

        private void InitializeGameText(Assembly assembly)
        {
            const string mapping = "Awaken.TG.Main.Utility.InputToText.InputActionsToTextMapping";
            AddType(assembly, "textMapping", mapping);
            foreach (var pair in new[] { new[] { "keySprite", "KeyToTextBiding", "keyToTextBidings" },
                new[] { "mouseSprite", "ActionToTextBiding", "mouseBidings" }, new[] { "padSprite", "GamepadIndexToTextBiding", "gamepadToTextBidings" } })
            {
                AddType(assembly, pair[0], mapping + "+" + pair[1]);
                Field("textMapping", pair[2], _types[pair[0]].MakeArrayType());
                Field(pair[0], "spriteName", typeof(string)); Field(pair[0], "text", typeof(string));
            }
            Field("keySprite", "keyCode", typeof(UnityEngine.KeyCode));
            _getTextMapping = assembly.GetType("Awaken.TG.MVC.Services", true).GetMethods()
                .Single(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(_types["textMapping"]);
        }

        public string PlainGameText(string value) => FormattedSpeechOutput.PlainText(value, SpriteLabel,
            name => string.IsNullOrWhiteSpace(name) ? _text.Get("speech.icon") : _text.Get("speech.namedIcon", name));

        private string SpriteLabel(string name)
        {
            if (World == null) return null;
            var mapping = _getTextMapping.Invoke(ReadProp("world", "Services", World), null);
            if (mapping == null) return null;
            // Resolve only the named icon already present in rendered text. No action-map edits,
            // templates or cached game objects; ambiguous labels retain the actual icon name.
            var labels = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in new[] { new[] { "keySprite", "keyToTextBidings" }, new[] { "mouseSprite", "mouseBidings" }, new[] { "padSprite", "gamepadToTextBidings" } })
            {
                var sources = ReadField("textMapping", pair[1], mapping) as IEnumerable;
                if (sources == null) continue;
                foreach (var source in sources)
                {
                    if (source == null || !string.Equals(name, ReadField(pair[0], "spriteName", source) as string, StringComparison.Ordinal)) continue;
                    var label = ReadField(pair[0], "text", source) as string;
                    if (string.IsNullOrWhiteSpace(label) && pair[0] == "keySprite") label = ReadField(pair[0], "keyCode", source).ToString();
                    if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
                }
            }
            return labels.Count == 1 ? labels.Single() : null;
        }
    }
}
