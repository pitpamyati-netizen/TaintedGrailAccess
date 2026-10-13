using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TaintedGrailAccess.Runtime;
using TaintedGrailAccess.Windows;

namespace TaintedGrailAccess.Windows.Tests
{
    // These tests deliberately never load a native DLL or instantiate SAPI.
    // They verify safe failure/thread/transcript behavior, not audible speech.
    internal static class Program
    {
        private static int _checks;
        public static int Main()
        {
            try
            {
                TranscriptModeIsHonest();
                FailedLoggingDoesNotEscape();
                WrongThreadIsRejected();
                MissingConfiguredDllIsSafe();
                PrivateContentIsNeverTranscribed();
                Console.WriteLine("PASS Windows safe-failure suite: " + _checks + " assertions. Native speech and the game were not tested.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL Windows safe-failure suite: " + exception.Message);
                return 1;
            }
        }

        private static void TranscriptModeIsHonest()
        {
            var log = new CollectingLog();
            var output = new WindowsSpeechOutput(new SpeechOptions { Backend = "transcript", AllowSapiFallback = false }, log);
            Check(!output.IsAvailable, "A transcript-only output must not claim that audible speech is available.");
            Check(output.ActiveBackend == "transcript-only", "The inactive backend must be clearly identified.");
            output.Speak("Карта: удар", true);
            output.Speak("Получено 5 урона", false);
            output.Speak(" \r\n ", true);
            Check(log.Count("speech.request.interrupt") == 1, "Exactly one user-request transcript entry.");
            Check(log.Count("speech.request.queue") == 1, "Exactly one passive-request transcript entry.");
            Check(log.Count("speech.accepted") == 0, "Logging a request is not a successful native call.");
            output.Stop();
            output.Dispose();
            output.Dispose();
            Check(!output.IsAvailable, "Repeated disposal keeps the bridge unavailable.");
            output.Speak("После закрытия", true);
            Check(log.Count("speech.accepted") == 0, "A disposed output never reports native acceptance.");
        }

        private static void FailedLoggingDoesNotEscape()
        {
            using (var output = new WindowsSpeechOutput(new SpeechOptions { Backend = "transcript", AllowSapiFallback = false }, new ThrowingLog()))
            {
                output.Speak("Проверка при отказе журнала", true);
                output.Stop();
                Check(!output.IsAvailable, "An unavailable logger does not make a native backend available.");
            }
        }

        private static void WrongThreadIsRejected()
        {
            var log = new CollectingLog();
            using (var output = new WindowsSpeechOutput(new SpeechOptions { Backend = "transcript", AllowSapiFallback = false }, log))
            {
                Exception backgroundError = null;
                var worker = new Thread(() =>
                {
                    try { output.Speak("Другой поток", false); output.Stop(); }
                    catch (Exception exception) { backgroundError = exception; }
                });
                worker.Start();
                Check(worker.Join(2000), "The off-thread request must not block waiting for an absent UI dispatcher.");
                Check(backgroundError == null, "The thread contract failure must be contained.");
                Check(output.LastError.IndexOf("constructing thread", StringComparison.Ordinal) >= 0,
                    "Off-thread use records an actionable reason.");
                Check(log.Count("speech.accepted") == 0, "Off-thread requests never reach native speech.");
            }
        }

        private static void MissingConfiguredDllIsSafe()
        {
            var log = new CollectingLog();
            // This intentionally invalid path must be rejected before native loading.
            var options = new SpeechOptions { Backend = "nvda", NvdaDllPath = "relative.dll", AllowSapiFallback = false };
            using (var output = new WindowsSpeechOutput(options, log))
            {
                output.Speak("Нет библиотеки речи", false);
                Check(!output.IsAvailable, "A rejected DLL path leaves audible output unavailable.");
                Check(log.Count("speech.request.queue") == 1, "Failed initialization preserves the requested text.");
                Check(log.Count("speech.accepted") == 0, "Failed initialization must not be reported as success.");
            }
        }

        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void PrivateContentIsNeverTranscribed()
        {
            var log = new CollectingLog();
            using (var output = new WindowsSpeechOutput(new SpeechOptions { Backend = "transcript", AllowSapiFallback = false }, log))
            {
                output.SpeakPrivate("private-user-content", true);
                output.SpeakPrivate("private-selection", false);
                Check(log.Count("speech.request.interrupt") == 1 && log.Count("speech.request.queue") == 1, "Private request provenance is preserved.");
                Check(log.Messages.All(message => !message.Contains("private-user-content") && !message.Contains("private-selection")), "User content must never enter the transcript.");
                Check(log.Count("speech.accepted") == 0, "A private transcript marker does not prove native speech.");
            }
        }

        private sealed class CollectingLog : IRuntimeLog
        {
            private readonly List<string> _categories = new List<string>();
            public readonly List<string> Messages = new List<string>();
            public void Write(string category, string message) { lock (_categories) { _categories.Add(category); Messages.Add(message); } }
            public int Count(string category) { lock (_categories) return _categories.Count(value => value == category); }
        }
        private sealed class ThrowingLog : IRuntimeLog
        {
            public void Write(string category, string message) { throw new InvalidOperationException("Test logger failure"); }
        }
    }
}
