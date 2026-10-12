using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    public interface ISpeechOutput
    {
        void Speak(string text, bool interrupt);
        void Stop();
    }

    public interface IRuntimeLog
    {
        void Write(string category, string message);
    }

    public interface ISoundOutput { void Play(object sound); }

    public sealed class NullLog : IRuntimeLog
    {
        public void Write(string category, string message) { }
    }

    public sealed class NullSound : ISoundOutput
    {
        public void Play(object sound) { }
    }

    // A dictionary is injected. No game strings are translated by this service.
    public sealed class Localizer
    {
        private readonly IDictionary<string, string> _strings;
        public Localizer(IDictionary<string, string> strings)
        {
            _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        }
        public string Get(string key, params object[] args)
        {
            string value;
            if (!_strings.TryGetValue(key, out value)) value = "[" + key + "]";
            return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
        }
        public static Localizer Load(string path)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                var delimiter = line.IndexOf('=');
                if (delimiter < 1) throw new FormatException("Invalid locale entry.");
                strings.Add(line.Substring(0, delimiter).Trim(), line.Substring(delimiter + 1).Replace("\\n", "\n"));
            }
            return new Localizer(strings);
        }
    }

    public sealed class FaultBoundary
    {
        private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
        private readonly IRuntimeLog _log;
        public FaultBoundary(IRuntimeLog log) { _log = log ?? new NullLog(); }
        public void Report(string screenId, Exception error)
        {
            // Deliberately excludes exception.Message: it may change every frame.
            var key = screenId + "\0" + error.GetType().FullName;
            if (!_logged.Add(key)) return;
            try { _log.Write("fault", screenId + ": " + error.GetType().Name + ": " + error.Message); }
            catch { /* Logging must not take down the frame loop. */ }
        }
        public void Run(string screenId, Action action)
        {
            try { action(); } catch (Exception ex) { Report(screenId, ex); }
        }
        public T Read<T>(string screenId, Func<T> action, T fallback)
        {
            try { return action(); } catch (Exception ex) { Report(screenId, ex); return fallback; }
        }
    }

    public enum UiKey { None, Up, Right, Down, Left, Tab, Home, End, Enter, Escape, Backspace, Space, F1 }
    [Flags] public enum Modifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }

    public struct KeyChord : IEquatable<KeyChord>
    {
        public UiKey Key { get; }
        public Modifiers Modifiers { get; }
        public string Text { get; }
        public bool IsRepeat { get; }
        public KeyChord(UiKey key, Modifiers modifiers = Modifiers.None, string text = null, bool isRepeat = false)
        { Key = key; Modifiers = modifiers; Text = text; IsRepeat = isRepeat; }
        public bool Shift { get { return (Modifiers & Modifiers.Shift) != 0; } }
        public bool Control { get { return (Modifiers & Modifiers.Control) != 0; } }
        public bool Alt { get { return (Modifiers & Modifiers.Alt) != 0; } }
        public bool Equals(KeyChord other) { return Key == other.Key && Modifiers == other.Modifiers && Text == other.Text; }
        public override bool Equals(object obj) { return obj is KeyChord && Equals((KeyChord)obj); }
        public override int GetHashCode() { return ((int)Key * 397) ^ (int)Modifiers ^ (Text == null ? 0 : Text.GetHashCode()); }
    }

    public interface IAccessibleScreen
    {
        string Id { get; }
        string ParentId { get; }
        int Layer { get; }
        string ScreenName { get; }
        bool StartUnfocused { get; }
        bool Exclusive { get; }
        bool CapturesRawInput { get; }
        bool KeepStateOnPop { get; }
        object InitialFocusStop { get; }
        string UnfocusedAnnouncement { get; }
        bool IsActive();
        void Build(GraphBuilder builder);
        bool OnBack();
        bool OnContextualInput(KeyChord chord);
        void OnPush();
        void OnFocus();
        void OnUnfocus();
        void OnPop();
        void OnUpdate();
    }

    public abstract class AccessibleScreen : IAccessibleScreen
    {
        public abstract string Id { get; }
        public virtual string ParentId { get { return null; } }
        public virtual int Layer { get { return 0; } }
        public abstract string ScreenName { get; }
        public virtual bool StartUnfocused { get { return false; } }
        public virtual bool Exclusive { get { return false; } }
        public virtual bool CapturesRawInput { get { return false; } }
        public virtual bool KeepStateOnPop { get { return false; } }
        public virtual object InitialFocusStop { get { return null; } }
        public virtual string UnfocusedAnnouncement { get { return ScreenName; } }
        public abstract bool IsActive();
        public abstract void Build(GraphBuilder builder);
        public virtual bool OnBack() { return false; }
        public virtual bool OnContextualInput(KeyChord chord) { return false; }
        public virtual void OnPush() { }
        public virtual void OnFocus() { }
        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }
        public virtual void OnUpdate() { }
    }
}
