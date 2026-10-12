using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TaintedGrailAccess.Windows
{
    internal interface ISpeechBackend : IDisposable
    {
        string Name { get; }
        void Speak(string text, bool interrupt);
        void Stop();
    }

    internal sealed class NvdaBackend : ISpeechBackend
    {
        private readonly NativeModule _module;
        private readonly StatusCall _test;
        private readonly StatusCall _cancel;
        private readonly TextCall _speak;
        public string Name => "NVDA";

        // NVDA Controller Client: error_status_t is a Windows unsigned 32-bit value,
        // __stdcall; wchar_t* is UTF-16. This uses the stable version 1 entrypoints.
        // Source: nvaccess/nvda extras/controllerClient/readme.md and examples/example_python.py.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint StatusCall();
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate uint TextCall([MarshalAs(UnmanagedType.LPWStr)] string text);

        public NvdaBackend(string path)
        {
            _module = new NativeModule(path);
            try
            {
                _test = _module.GetExport<StatusCall>("nvdaController_testIfRunning");
                _cancel = _module.GetExport<StatusCall>("nvdaController_cancelSpeech");
                _speak = _module.GetExport<TextCall>("nvdaController_speakText");
                Check(_test(), "testIfRunning");
            }
            catch { _module.Dispose(); throw; }
        }

        public void Speak(string text, bool interrupt)
        {
            if (interrupt) Check(_cancel(), "cancelSpeech");
            // No length-based timers: NVDA's own queue handles passive output.
            Check(_speak(text), "speakText");
        }
        public void Stop() => Check(_cancel(), "cancelSpeech");
        public void Dispose() => _module.Dispose();
        private static void Check(uint result, string operation)
        {
            if (result != 0) throw new Win32Exception(unchecked((int)result), "NVDA " + operation + " failed.");
        }
    }

    internal sealed class TolkBackend : ISpeechBackend
    {
        private readonly NativeModule _module;
        private readonly VoidCall _unload;
        private readonly SpeakCall _speak;
        private readonly BoolCall _silence;
        private bool _loaded;
        public string Name { get; }

        // Tolk's C interface uses __cdecl and C++ bool (one byte), not Win32 BOOL.
        // Keep every call on the constructing thread: Tolk is not thread-safe.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool BoolCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr StringCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SpeakCall([MarshalAs(UnmanagedType.LPWStr)] string text,
            [MarshalAs(UnmanagedType.I1)] bool interrupt);

        public TolkBackend(string path)
        {
            _module = new NativeModule(path);
            try
            {
                var load = _module.GetExport<VoidCall>("Tolk_Load");
                _unload = _module.GetExport<VoidCall>("Tolk_Unload");
                _speak = _module.GetExport<SpeakCall>("Tolk_Speak");
                _silence = _module.GetExport<BoolCall>("Tolk_Silence");
                var detect = _module.GetExport<StringCall>("Tolk_DetectScreenReader");
                var hasSpeech = _module.GetExport<BoolCall>("Tolk_HasSpeech");
                load();
                _loaded = true;
                var detected = Marshal.PtrToStringUni(detect());
                if (string.IsNullOrEmpty(detected) || !hasSpeech())
                    throw new InvalidOperationException("Tolk did not find an active speech-capable screen reader.");
                Name = "Tolk: " + detected;
            }
            catch { Dispose(); throw; }
        }
        public void Speak(string text, bool interrupt)
        {
            if (!_speak(text, interrupt)) throw new InvalidOperationException("Tolk refused speech output.");
        }
        public void Stop()
        {
            if (!_silence()) throw new InvalidOperationException("Tolk could not stop speech.");
        }
        public void Dispose()
        {
            try
            {
                if (_loaded) { _loaded = false; _unload(); }
            }
            finally { _module.Dispose(); }
        }
    }

    internal sealed class SapiBackend : ISpeechBackend
    {
        private object _voice;
        public string Name => "Windows SAPI";

        public SapiBackend()
        {
            // Late binding avoids shipping interop assemblies and remains BCL-only.
            // GetTypeFromProgID is located at runtime for netstandard2.0/Mono portability.
            var method = typeof(Type).GetMethod("GetTypeFromProgID", new[] { typeof(string) });
            if (method == null) throw new PlatformNotSupportedException("This runtime has no COM activation API.");
            var voiceType = method.Invoke(null, new object[] { "SAPI.SpVoice" }) as Type;
            if (voiceType == null) throw new InvalidOperationException("Windows SAPI voice is not registered.");
            _voice = Activator.CreateInstance(voiceType);
            if (_voice == null) throw new InvalidOperationException("Windows SAPI voice activation failed.");
        }

        public void Speak(string text, bool interrupt)
        {
            // SpeechVoiceSpeakFlags: Async=1, PurgeBeforeSpeak=2, IsNotXML=16.
            // Literal mode prevents game text from being interpreted as SSML/XML.
            InvokeSpeak(text, 1 | 16 | (interrupt ? 2 : 0));
        }
        public void Stop() => InvokeSpeak("", 1 | 2 | 16);
        private void InvokeSpeak(string text, int flags)
        {
            if (_voice == null) throw new ObjectDisposedException(nameof(SapiBackend));
            _voice.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice,
                new object[] { text, flags });
        }
        public void Dispose()
        {
            if (_voice == null) return;
            var voice = _voice;
            _voice = null;
            if (Marshal.IsComObject(voice)) Marshal.ReleaseComObject(voice);
        }
    }
}
