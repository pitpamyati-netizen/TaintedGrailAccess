using System;
using System.Collections.Generic;
using System.Threading;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Windows
{
    /// <summary>
    /// Single-owner-thread native speech adapter. The runtime controls priorities,
    /// coalescing and announce-once; this class preserves the interrupt/queue request.
    /// A transcript entry means "requested", never proof that sound was audible.
    /// </summary>
    public sealed class WindowsSpeechOutput : IPrivateSpeechOutput, IDisposable
    {
        private readonly IRuntimeLog _log;
        private readonly int _ownerThread;
        private readonly Queue<Func<ISpeechBackend>> _candidates = new Queue<Func<ISpeechBackend>>();
        private readonly HashSet<string> _reported = new HashSet<string>();
        private readonly object _logGate = new object();
        private ISpeechBackend _backend;
        private bool _disposed;
        public bool IsAvailable => !_disposed && _backend != null;
        public string ActiveBackend => _backend?.Name ?? "transcript-only";
        public string LastError { get; private set; } = "";

        public WindowsSpeechOutput(SpeechOptions options, IRuntimeLog log)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _ownerThread = Thread.CurrentThread.ManagedThreadId;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Report("platform", "Native speech is unavailable on this OS. Requests remain in the transcript.");
                return;
            }
            var mode = (options.Backend ?? "auto").Trim().ToLowerInvariant();
            if (mode != "auto" && mode != "nvda" && mode != "tolk" && mode != "sapi" && mode != "transcript")
            {
                Report("backend", "Unknown speech backend: " + mode);
                return;
            }
            // Snapshot configuration; subsequent external edits cannot redirect a pending fallback.
            var nvdaPath = options.NvdaDllPath;
            var tolkPath = options.TolkDllPath;
            if ((mode == "auto" || mode == "nvda") && !string.IsNullOrWhiteSpace(nvdaPath))
                _candidates.Enqueue(() => new NvdaBackend(nvdaPath));
            if ((mode == "auto" || mode == "tolk") && !string.IsNullOrWhiteSpace(tolkPath))
                _candidates.Enqueue(() => new TolkBackend(tolkPath));
            if (mode == "sapi" || (mode != "transcript" && options.AllowSapiFallback))
                _candidates.Enqueue(() => new SapiBackend());
            ActivateNext();
            if (!IsAvailable) Report("unavailable", "No speech backend is active. Check local DLL paths, screen reader and speech configuration.");
        }

        public void Speak(string text, bool interrupt)
        { SpeakCore(text, interrupt, false); }

        /// <summary>Delivers user-entered text without putting its characters in diagnostics.</summary>
        public void SpeakPrivate(string text, bool interrupt)
        { SpeakCore(text, interrupt, true); }

        private void SpeakCore(string text, bool interrupt, bool privateContent)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            SafeLog(interrupt ? "speech.request.interrupt" : "speech.request.queue", privateContent ? "[private editable content]" : text);
            if (!OnOwnerThread()) return;
            while (_backend != null)
            {
                try
                {
                    _backend.Speak(text, interrupt);
                    SafeLog("speech.accepted", _backend.Name);
                    return;
                }
                catch (Exception exception)
                {
                    Report("output:" + _backend.Name + ":" + exception.GetType().FullName,
                        privateContent ? "Private text output failed: " + exception.GetType().Name : exception.Message);
                    ReleaseBackend();
                    ActivateNext();
                }
            }
            Report("unavailable", privateContent ? "Speech is unavailable; editable content is excluded from diagnostics."
                : "Speech is unavailable; the requested text was written to the transcript.");
        }

        public void Stop()
        {
            if (!OnOwnerThread() || _backend == null) return;
            try { _backend.Stop(); }
            catch (Exception exception) { Report("stop:" + exception.GetType().FullName, exception.Message); }
        }

        private void ActivateNext()
        {
            while (_backend == null && _candidates.Count > 0)
            {
                try
                {
                    _backend = _candidates.Dequeue()();
                    SafeLog("speech.backend", _backend.Name);
                }
                catch (Exception exception) { Report("initialize:" + exception.GetType().FullName, exception.Message); }
            }
        }

        private bool OnOwnerThread()
        {
            if (_disposed) { Report("disposed", "Speech output has been disposed."); return false; }
            if (_ownerThread == Thread.CurrentThread.ManagedThreadId) return true;
            Report("thread", "Speech must be called on its constructing thread. Marshal requests to the host tick.");
            return false;
        }
        private void ReleaseBackend()
        {
            var old = _backend;
            _backend = null;
            try { old?.Dispose(); }
            catch (Exception exception) { Report("dispose:" + exception.GetType().FullName, exception.Message); }
        }
        private void Report(string key, string message)
        {
            lock (_logGate)
            {
                LastError = message;
                if (_reported.Add(key)) SafeLog("speech.error", message);
            }
        }
        private void SafeLog(string category, string message)
        {
            lock (_logGate)
            {
                try { _log.Write(category, message); }
                catch { /* A diagnostic sink must not crash the game's tick or speech cleanup. */ }
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (!OnOwnerThread()) return;
            Stop();
            ReleaseBackend();
            _candidates.Clear();
            _disposed = true;
        }
    }
}
