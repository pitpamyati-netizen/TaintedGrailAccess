using System;
using System.Globalization;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>
    /// Observes the real editor after its own handlers. Stores only a disposable text snapshot,
    /// never a native field or callback; it does not implement or modify editing.
    /// </summary>
    public sealed class TextEditFeedback
    {
        private readonly IPrivateSpeechOutput _speech;
        private readonly Localizer _text;
        private string _id;
        private string _value;
        private int _anchor;
        private int _caret;

        public TextEditFeedback(IPrivateSpeechOutput speech, Localizer text)
        { _speech = speech ?? throw new ArgumentNullException(nameof(speech)); _text = text ?? throw new ArgumentNullException(nameof(text)); }

        /// <summary>Clears editable content when editing ends, the window blurs or a scene closes.</summary>
        public void Reset() { _id = null; _value = null; _anchor = _caret = 0; }

        /// <summary>Reads current content or selection by explicit request, without logging the text.</summary>
        public void Read(string value, int anchor, int caret, bool protectedContent = false)
        {
            if (protectedContent) { _speech.SpeakPrivate(_text.Get("text.protected"), true); return; }
            value = value ?? "";
            anchor = Clamp(anchor, value.Length); caret = Clamp(caret, value.Length);
            var selected = anchor != caret ? value.Substring(Math.Min(anchor, caret), Math.Abs(anchor - caret)) : value;
            _speech.SpeakPrivate(selected.Length == 0 ? _text.Get("text.empty") : selected, true);
        }

        /// <summary>Echoes insertion, deletion, selection and caret movement after confirmed native edits.</summary>
        public void Observe(string id, string value, int anchor, int caret, bool protectedContent = false)
        {
            if (id == null) { Reset(); return; }
            value = value ?? ""; anchor = Clamp(anchor, value.Length); caret = Clamp(caret, value.Length);
            if (protectedContent) { Reset(); return; }
            if (_id != id) { _id = id; _value = value; _anchor = anchor; _caret = caret; return; }
            string message = null;
            if (value != _value)
            {
                int start = 0;
                while (start < value.Length && start < _value.Length && value[start] == _value[start]) start++;
                // Keep surrogate pairs intact in replaced/deleted characters.
                if (start > 0 && start < value.Length && char.IsLowSurrogate(value[start])) start--;
                int oldEnd = _value.Length, newEnd = value.Length;
                while (oldEnd > start && newEnd > start && _value[oldEnd - 1] == value[newEnd - 1]) { oldEnd--; newEnd--; }
                if (newEnd < value.Length && newEnd > start && char.IsHighSurrogate(value[newEnd - 1])) { newEnd++; oldEnd++; }
                var inserted = value.Substring(start, newEnd - start);
                var removed = _value.Substring(start, oldEnd - start);
                message = inserted.Length > 0 ? Readable(inserted) : _text.Get("text.deleted", Readable(removed));
            }
            else if (anchor != _anchor || caret != _caret)
            {
                if (anchor != caret) message = _text.Get("text.selection", value.Substring(Math.Min(anchor, caret), Math.Abs(anchor - caret)));
                else if (caret == value.Length) message = _text.Get("text.end");
                else message = Readable(StringInfo.GetNextTextElement(value, caret));
            }
            _value = value; _anchor = anchor; _caret = caret;
            if (!string.IsNullOrEmpty(message)) _speech.SpeakPrivate(message, true);
        }

        private string Readable(string text)
        { return text == " " ? _text.Get("text.space") : text == "\n" || text == "\r\n" ? _text.Get("text.newline") : text; }
        private static int Clamp(int value, int length) => Math.Max(0, Math.Min(value, length));
    }
}
