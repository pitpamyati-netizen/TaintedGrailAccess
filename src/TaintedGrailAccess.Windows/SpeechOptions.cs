namespace TaintedGrailAccess.Windows
{
    /// <summary>Only caller-selected, fully qualified local DLL paths are used.</summary>
    public sealed class SpeechOptions
    {
        /// <summary>auto, nvda, tolk, sapi or transcript. Tolk is optional and unbundled.</summary>
        public string Backend { get; set; } = "auto";
        public string NvdaDllPath { get; set; } = "";
        public string TolkDllPath { get; set; } = "";
        public bool AllowSapiFallback { get; set; } = true;
    }
}
