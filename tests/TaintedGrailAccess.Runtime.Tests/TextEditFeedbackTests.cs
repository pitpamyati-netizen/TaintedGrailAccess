using System;
using System.Collections.Generic;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class TextEditFeedbackTests
    {
        private sealed class Output : IPrivateSpeechOutput
        {
            public readonly List<string> Private = new List<string>();
            public void Speak(string text, bool interrupt) { throw new Exception("Editable content reached ordinary speech."); }
            public void SpeakPrivate(string text, bool interrupt) { if (!interrupt) throw new Exception("Edit feedback must interrupt."); Private.Add(text); }
            public void Stop() { }
        }
        private static TextEditFeedback New(Output output) => new TextEditFeedback(output, new Localizer(new Dictionary<string, string>
        {
            ["text.deleted"] = "Deleted: {0}", ["text.selection"] = "Selected: {0}", ["text.end"] = "End",
            ["text.empty"] = "Empty", ["text.space"] = "Space", ["text.newline"] = "Newline", ["text.protected"] = "Protected"
        }));
        private static void Equal(string expected, string actual) { if (expected != actual) throw new Exception("Expected " + expected + "; got " + actual); }
        private static string Last(Output output) => output.Private.Count == 0 ? null : output.Private[output.Private.Count - 1];

        public static void ActualEdits()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("field", "", 0, 0);
            feedback.Observe("field", "я", 1, 1); Equal("я", Last(output));
            feedback.Observe("field", "я ", 2, 2); Equal("Space", Last(output));
            feedback.Observe("field", "я", 1, 1); Equal("Deleted: Space", Last(output));
            feedback.Observe("field", "", 0, 0); Equal("Deleted: я", Last(output));
            if (output.Private.Count != 4) throw new Exception("One edit must speak once.");
        }
        public static void PasteAndReplacement()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("field", "old text", 3, 8);
            feedback.Observe("field", "old paste", 9, 9); Equal("paste", Last(output));
            feedback.Observe("field", "old paste", 9, 9);
            if (output.Private.Count != 1) throw new Exception("Unchanged/native duplicate observations spoke twice.");
        }
        public static void SelectionAndCaret()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("field", "ёж", 2, 2);
            feedback.Observe("field", "ёж", 0, 0); Equal("ё", Last(output));
            feedback.Observe("field", "ёж", 0, 2); Equal("Selected: ёж", Last(output));
            feedback.Observe("field", "ёж", 2, 2); Equal("End", Last(output));
            feedback.Read("ёж", 0, 1); Equal("ё", Last(output));
            feedback.Read("ёж", 2, 2); Equal("ёж", Last(output));
        }
        public static void ResetAndFieldChange()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("title", "private title", 0, 0);
            feedback.Observe("description", "private description", 0, 0);
            feedback.Reset(); feedback.Observe("description", "new text after blur", 0, 0);
            if (output.Private.Count != 0) throw new Exception("Switching/reopening a field must establish a silent baseline.");
            feedback.Read("", 0, 0); Equal("Empty", Last(output));
        }
        public static void Passwords()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("password", "secret", 6, 6, true);
            feedback.Read("secret", 0, 6, true); Equal("Protected", Last(output));
            if (output.Private.Count != 1) throw new Exception("Password echo must be silent.");
        }
        public static void UnicodeAndInvalidSelection()
        {
            var output = new Output(); var feedback = New(output);
            feedback.Observe("field", "", 0, 0);
            feedback.Observe("field", "\ud83d\ude00", 2, 2); Equal("\ud83d\ude00", Last(output));
            feedback.Observe("field", "\ud83d\ude03", 2, 2); Equal("\ud83d\ude03", Last(output));
            feedback.Observe("field", "", 0, 0); Equal("Deleted: \ud83d\ude03", Last(output));
            feedback.Read("safe", -100, 100); Equal("safe", Last(output));
            feedback.Read(null, -1, 1); Equal("Empty", Last(output));
        }
    }
}
