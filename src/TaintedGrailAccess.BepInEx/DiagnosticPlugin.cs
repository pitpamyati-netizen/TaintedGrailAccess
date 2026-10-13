using System;
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
    /// Diagnostic bootstrap, passive start reader and opt-in fingerprinted title input.
    /// Speech success is not complete game accessibility.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [BepInPlugin("org.taintedgrailaccess.diagnostics", "Tainted Grail Access Diagnostics", "0.3.4")]
    public sealed class DiagnosticPlugin : BaseUnityPlugin
    {
        private WindowsSpeechOutput _speech;
        private DiagnosticLog _log;
        private StartPromptReader _startPrompt;
        private GameInputAudit _inputAudit;
        private TitleInputHost _titleInput;
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
                var locale = (language ?? "ru").Split('-', '_')[0];
                if (locale != "en") locale = "ru";
                var text = Localizer.Load(Path.Combine(Path.GetDirectoryName(typeof(DiagnosticPlugin).Assembly.Location),
                    "locales", locale + ".properties"));
                _startupText = text.Get("startup.diagnostic");
                var options = new SpeechOptions
                {
                    Backend = Config.Bind("Speech", "Backend", "auto", "auto, nvda, tolk, sapi or transcript.").Value,
                    NvdaDllPath = Config.Bind("Speech", "NvdaDllPath", "", "Absolute path to a locally obtained official Windows x64 NVDA Controller Client DLL.").Value,
                    TolkDllPath = Config.Bind("Speech", "TolkDllPath", "", "Optional absolute local x64 Tolk.dll path. JAWS needs its Tolk backend and live testing.").Value,
                    AllowSapiFallback = Config.Bind("Speech", "AllowSapiFallback", true, "Use an installed Windows SAPI voice when the selected screen reader bridge is unavailable.").Value
                };
                _log.Write("host.status", "BOOTSTRAP; input mode will be selected after configuration and fingerprint checks.");
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
                var readPrompt = Config.Bind("Accessibility", "ReadStartPrompt", true,
                    "Read only the visible Press Any Button prompt on the inspected GOG 1.61 build. No keyboard capture or menu actions.");
                if (readPrompt.Value)
                {
                    try
                    {
                        _startPrompt = new StartPromptReader(_speech, text, _log);
                        _startupText = text.Get("startup.promptPreview");
                    }
                    catch (Exception exception)
                    {
                        _log.Write("binding.press-start.disabled", exception.GetType().Name + ": " + exception.Message);
                    }
                }
                var auditInput = Config.Bind("Diagnostics", "ObserveTitleInput", false,
                    "Opt-in bounded observation of title input on inspected GOG 1.61. No key suppression, text capture or actions. Resets after this launch.");
                if (auditInput.Value)
                {
                    // Reset also after an unsupported-build attempt; never leave a recorder
                    // active across later launches without an explicit diagnostic request.
                    auditInput.Value = false;
                    Config.Save();
                    try
                    {
                        _inputAudit = new GameInputAudit(_log);
                        _startupText += " " + text.Get("diagnostic.inputAudit");
                    }
                    catch (Exception exception)
                    {
                        _log.Write("binding.input-audit.disabled", exception.GetType().Name + ": " + exception.Message);
                    }
                }
                var titleInput = Config.Bind("Accessibility", "EnableTitleInput", false,
                    "Enable fingerprinted menus, settings, first path and card combat; unsupported screens keep ordinary input.");
                if (titleInput.Value)
                {
                    try
                    {
                        _titleInput = new TitleInputHost(_speech, text, _log);
                        _startupText = text.Get("startup.titleInput");
                    }
                    catch (Exception exception)
                    { _log.Write("binding.title-input.disabled", exception.GetType().Name + ": " + exception.Message); }
                }
                _log.Write("host.status", _titleInput != null ? "TITLE_INPUT_ENABLED; original UI handlers, scoped keyboard ownership." :
                    "PASSIVE_DIAGNOSTICS; no input suppression or game actions installed.");
            }
            catch (Exception exception)
            {
                Logger.LogError("[TaintedGrailAccess] Diagnostic startup failed: " + exception);
                _startupPending = false;
            }
        }

        private void Update()
        {
            _titleInput?.Poll("plugin.Update");
            // Delay the diagnostic announcement until the game actually has focus.
            // This is a tick proof of life, not a game-state integration.
            if (!_startupPending || !Application.isFocused || _speech == null) return;
            _startupPending = false;
            _speech.Speak(_startupText, false);
        }

        private void OnApplicationFocus(bool focused)
        {
            _inputAudit?.OnWindowFocusChanged(focused);
            _titleInput?.OnWindowFocusChanged(focused);
            if (!focused) _speech?.Stop();
        }

        private void LateUpdate() { _titleInput?.ObserveText(); }

        private void OnDestroy()
        {
            _startupPending = false;
            try { _titleInput?.Dispose(); }
            catch (Exception exception) { Logger.LogWarning("Title input cleanup failed: " + exception.Message); }
            _titleInput = null;
            try { _inputAudit?.Dispose(); }
            catch (Exception exception) { Logger.LogWarning("Input observer cleanup failed: " + exception.Message); }
            _inputAudit = null;
            try { _startPrompt?.Dispose(); }
            catch (Exception exception) { Logger.LogWarning("Start reader cleanup failed: " + exception.Message); }
            _startPrompt = null;
            _speech?.Dispose();
            _speech = null;
            _log?.Dispose();
            _log = null;
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
