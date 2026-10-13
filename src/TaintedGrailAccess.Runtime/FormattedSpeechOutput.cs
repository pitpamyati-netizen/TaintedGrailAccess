using System;
using System.Net;
using System.Text.RegularExpressions;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>Converts rendered game markup to speech without touching editable user content.</summary>
    public sealed class FormattedSpeechOutput : IPrivateSpeechOutput
    {
        private static readonly Regex Sprites = new Regex(@"<sprite\b[^>]*>", RegexOptions.IgnoreCase);
        private static readonly Regex SpriteName = new Regex("\\bname\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
        private static readonly Regex Breaks = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase);
        private static readonly Regex Formatting = new Regex(@"</?(?:align|alpha|b|color|cspace|font|font-weight|gradient|i|indent|line-height|line-indent|link|lowercase|margin|mark|mspace|nobr|noparse|page|pos|rotate|s|size|smallcaps|space|style|sub|sup|u|uppercase|voffset|width)(?=[\s=>])[^>]*>|<#[0-9a-f]{6}(?:[0-9a-f]{2})?>", RegexOptions.IgnoreCase);
        private readonly ISpeechOutput _output;
        private readonly Func<string, string> _format;

        public FormattedSpeechOutput(ISpeechOutput output, Func<string, string> format)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public void Speak(string text, bool interrupt) => _output.Speak(_format(text), interrupt);
        public void Stop() => _output.Stop();
        // A backend without the private port must never receive editable content through Speak.
        public void SpeakPrivate(string text, bool interrupt) => (_output as IPrivateSpeechOutput)?.SpeakPrivate(text, interrupt);

        public static string PlainText(string text, Func<string, string> spriteLabel, Func<string, string> unknownSprite)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string withIcons = Sprites.Replace(text, match =>
            {
                var name = SpriteName.Match(match.Value);
                string key = name.Success ? (name.Groups[1].Success ? name.Groups[1].Value : name.Groups[2].Success ? name.Groups[2].Value : name.Groups[3].Value) : null;
                string label = key == null ? null : spriteLabel?.Invoke(key);
                return " " + (string.IsNullOrWhiteSpace(label) ? unknownSprite(key) : label) + " ";
            });
            // Match formatting tags, rather than arbitrary angle-bracket spans: conditions
            // such as "value < 3; result > 1" are visible text and must remain intact.
            return WebUtility.HtmlDecode(Formatting.Replace(Breaks.Replace(withIcons, "\n"), string.Empty));
        }
    }
}
