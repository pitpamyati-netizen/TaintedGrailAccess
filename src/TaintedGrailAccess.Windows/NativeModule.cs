using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TaintedGrailAccess.Windows
{
    /// <summary>Owns one LoadLibrary reference; never changes process-wide DLL search paths.</summary>
    internal sealed class NativeModule : IDisposable
    {
        private IntPtr _handle;

        public NativeModule(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Native speech is available only on Windows.");
            if (IntPtr.Size != 8)
                throw new PlatformNotSupportedException("This diagnostic bridge requires an x64 game process.");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("A fully qualified local DLL path is required.", nameof(path));
            var fullPath = Path.GetFullPath(path).Replace('/', '\\');
            // Drive-relative paths, UNC shares and device paths are intentionally not accepted.
            if (fullPath.Length < 3 || fullPath[1] != ':' ||
                (fullPath[2] != '\\' && fullPath[2] != '/') || path.Length < 3 || path[1] != ':' ||
                (path[2] != '\\' && path[2] != '/'))
                throw new ArgumentException("Use an absolute local drive path such as C:\\Speech\\library.dll.", nameof(path));
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Speech DLL was not found.", fullPath);
            ValidateAmd64Pe(fullPath);
            const uint loadLibrarySearchDllLoadDir = 0x00000100;
            const uint loadLibrarySearchSystem32 = 0x00000800;
            _handle = LoadLibraryEx(fullPath, IntPtr.Zero, loadLibrarySearchDllLoadDir | loadLibrarySearchSystem32);
            if (_handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load selected speech DLL or its dependencies.");
        }

        public T GetExport<T>(string name) where T : class
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(NativeModule));
            var address = GetProcAddress(_handle, name);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException("Missing native export: " + name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static void ValidateAmd64Pe(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
                    throw new BadImageFormatException("Speech DLL has no valid MZ header.");
                stream.Position = 0x3c;
                var offset = reader.ReadInt32();
                if (offset < 64 || offset > stream.Length - 26)
                    throw new BadImageFormatException("Speech DLL has an invalid PE offset.");
                stream.Position = offset;
                if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
                    throw new BadImageFormatException("Select the Windows x64 (AMD64) speech DLL.");
                stream.Position = offset + 22;
                if ((reader.ReadUInt16() & 0x2000) == 0)
                    throw new BadImageFormatException("Selected PE file is not a DLL.");
            }
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            var handle = _handle;
            _handle = IntPtr.Zero;
            FreeLibrary(handle);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
