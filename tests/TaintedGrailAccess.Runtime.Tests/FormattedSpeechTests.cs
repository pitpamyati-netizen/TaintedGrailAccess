using System;
using System.Collections.Generic;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class FormattedSpeechTests
    {
        private static string Plain(string value) => FormattedSpeechOutput.PlainText(value,
            name => name == "keyTab" ? "Tab" : null, name => name == null ? "Icon" : "Icon: " + name);
        private static void Equal(string expected, string actual) { if (actual != expected) throw new Exception("Expected " + expected + "; got " + actual); }
        public static void VisibleFormatting()
        {
            Equal("Урон 10\nСледующая строка & текст", Plain("<color=#FFFFFF>Урон <b>10</b></color><br>Следующая строка &amp; <link=\"url\">текст</link>"));
            Equal(string.Empty, Plain(null));
            Equal("Порог < 3; результат > 1", Plain("Порог < 3; результат > 1"));
            Equal("<example>", Plain("<example>"));
        }
        public static void IconsKeepMeaning()
        {
            Equal("Press  Tab ", Plain("Press <sprite name=\"keyTab\">"));
            Equal(" Icon: unknown ", Plain("<sprite name='unknown'>"));
            Equal(" Icon ", Plain("<sprite=0>"));
            Equal(" Tab ", Plain("<SPRITE name=keyTab>"));
        }
        private sealed class Output : IPrivateSpeechOutput
        {
            public string Ordinary, Private;
            public bool Interrupted, Stopped;
            public void Speak(string text, bool interrupt) { Ordinary = text; Interrupted = interrupt; }
            public void SpeakPrivate(string text, bool interrupt) { Private = text; Interrupted = interrupt; }
            public void Stop() { Stopped = true; }
        }
        public static void PrivateContentIsLiteral()
        {
            var output = new Output(); var speech = new FormattedSpeechOutput(output, Plain);
            speech.Speak("<b>game</b>", false); Equal("game", output.Ordinary);
            if (output.Interrupted) throw new Exception("Queue provenance lost.");
            speech.SpeakPrivate("<b>my literal text</b>", true); Equal("<b>my literal text</b>", output.Private);
            Equal("game", output.Ordinary);
            if (!output.Interrupted) throw new Exception("Private interrupt lost.");
            speech.Stop(); if (!output.Stopped) throw new Exception("Stop not forwarded.");
        }
        private sealed class OrdinaryOutput : ISpeechOutput
        {
            public void Speak(string text, bool interrupt) { throw new Exception("Private content reached non-private backend."); }
            public void Stop() { }
        }
        public static void PrivateContentNeverFallsBack()
        {
            new FormattedSpeechOutput(new OrdinaryOutput(), Plain).SpeakPrivate("secret", true);
        }
    }
}
