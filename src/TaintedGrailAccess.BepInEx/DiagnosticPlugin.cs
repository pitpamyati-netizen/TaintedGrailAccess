using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using global::BepInEx;
using global::BepInEx.Logging;
using TaintedGrailAccess.Runtime;
using TaintedGrailAccess.Windows;
using UnityEngine;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>
    /// Diagnostic bootstrap only. Deliberately registers no Harmony patches, input hooks,
    /// generic UI scanner or invented game adapter. Speech success is not game accessibility.
    /// </summary>
    [BepInPlugin("org.taintedgrailaccess.diagnostics", "Tainted Grail Access Diagnostics", "0.1.0")]
    public sealed class DiagnosticPlugin : BaseUnityPlugin
    {
        private WindowsSpeechOutput _speech;
        private DiagnosticLog _log;
        private bool _startupPending;
        private string _startupText;
        private bool _booted;

        private void Awake()
        {
            if (_booted) return;
            _booted = true;
            try
            {
                var directory = Path.Combine(Path.GetDirectoryName(typeof(DiagnosticPlugin).Assembly.Location), "diagnostics");
                _log = new DiagnosticLog(Logger, directory);
                var language = Config.Bind("General", "Language", "ru", "Diagnostic announcements: ru or en.").Value;
                _startupText = HostStrings.Get(language, "startup");
                var options = new SpeechOptions
                {
                    Backend = Config.Bind("Speech", "Backend", "auto", "auto, nvda, tolk, sapi or transcript.").Value,
                    NvdaDllPath = Config.Bind("Speech", "NvdaDllPath", "", "Absolute path to a locally obtained official Windows x64 NVDA Controller Client DLL.").Value,
                    TolkDllPath = Config.Bind("Speech", "TolkDllPath", "", "Optional absolute local x64 Tolk.dll path. JAWS needs its Tolk backend and live testing.").Value,
                    AllowSapiFallback = Config.Bind("Speech", "AllowSapiFallback", true, "Use an installed Windows SAPI voice when the selected screen reader bridge is unavailable.").Value
                };
                _log.Write("host.status", "DIAGNOSTIC_ONLY; no game adapter, navigation or input patches are installed.");
                _log.Write("host.game", "Product=" + Application.productName + "; Version=" + Application.version +
                    "; Unity=" + Application.unityVersion + "; ProcessBits=" + (IntPtr.Size * 8).ToString(CultureInfo.InvariantCulture));
                _log.Write("host.loader", typeof(BaseUnityPlugin).Assembly.GetName().FullName);
                _log.Write("host.assemblies", string.Join(", ", AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name).OrderBy(name => name, StringComparer.Ordinal).Take(300)));
                _speech = new WindowsSpeechOutput(options, _log);
                _log.Write("host.speech", "Backend=" + _speech.ActiveBackend + "; Available=" + _speech.IsAvailable);
                _startupPending = true;

                var export = Config.Bind("Diagnostics", "ExportAssemblyInventory", false,
                    "One-shot signature inventory of TG.Main and Assembly-CSharp. No field values or live scene state. Resets after use.");
                if (export.Value)
                {
                    try
                    {
                        var path = Path.Combine(directory, "assembly-signatures.txt");
                        SignatureInventory.Write(path, _log);
                    }
                    finally { export.Value = false; Config.Save(); }
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("[TaintedGrailAccess] Diagnostic startup failed: " + exception);
                _startupPending = false;
            }
        }

        private void Update()
        {
            // Delay the diagnostic announcement until the game actually has focus.
            // This is a tick proof of life, not a game-state integration.
            if (!_startupPending || !Application.isFocused || _speech == null) return;
            _startupPending = false;
            _speech.Speak(_startupText, false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _speech?.Stop();
        }

        private void OnDestroy()
        {
            _startupPending = false;
            _speech?.Dispose();
            _speech = null;
            _log?.Dispose();
            _log = null;
        }
    }

    internal static class HostStrings
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Strings =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ru"] = new Dictionary<string, string>
                {
                    ["startup"] = "Tainted Grail Access. Диагностическая версия проекта. Проверка речи запущена. Доступность игровых экранов ещё не подключена."
                },
                ["en"] = new Dictionary<string, string>
                {
                    ["startup"] = "Tainted Grail Access. Diagnostic project build. Speech check started. Game screen accessibility is not connected yet."
                }
            };

        public static string Get(string language, string key)
        {
            var code = (language ?? "ru").Split('-', '_')[0];
            if (!Strings.TryGetValue(code, out var table)) table = Strings["ru"];
            return table[key];
        }
    }

    internal sealed class DiagnosticLog : IRuntimeLog, IDisposable
    {
        private readonly ManualLogSource _logger;
        private StreamWriter _writer;
        private const long MaxPreviousLogBytes = 2 * 1024 * 1024;

        public DiagnosticLog(ManualLogSource logger, string directory)
        {
            _logger = logger;
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "diagnostic.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxPreviousLogBytes)
                {
                    var previous = path + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                _writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception exception) { _logger.LogWarning("File transcript unavailable: " + exception.Message); }
        }

        public void Write(string category, string message)
        {
            var line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " [" + category + "] " +
                (message ?? "").Replace("\r", " ").Replace("\n", " ");
            _logger.LogInfo(line);
            if (_writer == null) return;
            try { _writer.WriteLine(line); }
            catch (Exception exception)
            {
                try { _writer.Dispose(); } catch { }
                _writer = null;
                _logger.LogWarning("Transcript write failed: " + exception.Message);
            }
        }
        public void Dispose() { _writer?.Dispose(); _writer = null; }
    }
}
