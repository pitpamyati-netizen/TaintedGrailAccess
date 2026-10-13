using System;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TaintedGrailAccess.BepInExHost
{
    internal sealed partial class TitleUiBindings
    {
        private TextEditFeedback _editorFeedback;

        private void InitializeText()
        {
            if (_speech is IPrivateSpeechOutput output) _editorFeedback = new TextEditFeedback(output, _text);
        }

        private TMP_InputField KnownField(Component owner, out string id)
        {
            id = null;
            var bug = View("bug");
            if (bug != null)
                foreach (var name in new[] { "title", "description" })
                {
                    var field = ReadField("bugView", name, bug) as TMP_InputField;
                    if (ReferenceEquals(field, owner) && Visible(field)) { id = "bug." + name; return field; }
                }
            var gender = View("gender");
            if (gender != null)
            {
                var field = ReadField("genderView", "heroNameField", gender) as TMP_InputField;
                if (ReferenceEquals(field, owner) && Visible(field)) { id = "creator.name"; return field; }
            }
            return null;
        }

        /// <summary>Observation runs after the native editor; no text is passed to runtime diagnostics.</summary>
        public void ObserveText()
        {
            if (_editorFeedback == null) return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<TMP_InputField>();
            if (field == null || !Application.isFocused || !field.isFocused || KnownField(field, out var id) == null)
            { _editorFeedback.Reset(); return; }
            _editorFeedback.Observe(id, field.text, field.selectionStringAnchorPosition, field.selectionStringFocusPosition,
                field.inputType == TMP_InputField.InputType.Password);
        }

        /// <summary>Ctrl+F1 reads real content or selection. All editing keys remain native.</summary>
        public bool NativeTextInput(KeyChord chord)
        {
            if (_editorFeedback == null || chord.Modifiers != Modifiers.Control ||
                (chord.Key != UiKey.F1 && chord.Key != UiKey.Enter) || !Application.isFocused) return false;
            var selected = EventSystem.current?.currentSelectedGameObject;
            var owner = selected == null ? null : selected.GetComponent<TMP_InputField>();
            var field = KnownField(owner, out var id);
            if (field == null || !field.isFocused || HelpOpen || RawView() != null) return false;
            if (chord.IsRepeat) return true;
            if (chord.Key == UiKey.F1)
                _editorFeedback.Read(field.text, field.selectionStringAnchorPosition, field.selectionStringFocusPosition,
                    field.inputType == TMP_InputField.InputType.Password);
            else
            {
                // This is the editor's original finish path, also used by EventSystem selection.
                // It fires the genuine end-edit/validation listeners without submitting the form.
                Invoke("text.finish", () => EventSystem.current.SetSelectedGameObject(null));
                _editorFeedback.Reset();
                _speech.Speak(_text.Get("text.finished"), true);
            }
            return true;
        }

        public void ClearTextFeedback() => _editorFeedback?.Reset();

        private void AddTextField(GraphBuilder builder, string kind, TMP_InputField field, ControlId id, string labelKey)
        {
            if (!Visible(field)) return;
            builder.Add(id, new NodeVtable(new NodeAnnouncement(() => _text.Get(labelKey), "label"),
                new NodeAnnouncement(() => _text.Get("role.textField"), "role"),
                new NodeAnnouncement(() => field.IsInteractable() && !field.readOnly ? null : _text.Get("state.disabled"), "enabled"))
            {
                OnActivate = () =>
                {
                    if (!CanAct(kind) || !Actionable(field) || !field.IsInteractable() || field.readOnly) return;
                    var events = EventSystem.current;
                    if (events == null || !events.isActiveAndEnabled) return;
                    Invoke(kind + ".textEntry", () =>
                    {
                        events.SetSelectedGameObject(field.gameObject);
                        field.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
                    });
                    _pendingTextFrame = Time.frameCount;
                },
                OnTooltip = () => _editorFeedback?.Read(field.text, field.selectionStringAnchorPosition,
                    field.selectionStringFocusPosition, field.inputType == TMP_InputField.InputType.Password),
                StateText = () => field.isFocused || _pendingTextFrame == Time.frameCount ? _text.Get("text.editing") : null
            });
        }
    }
}
