using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Kuronuri.Core.Pdfium
{
    /// <summary>Loads pdfium.dll by absolute path from the application folder, then initializes it (once per process).</summary>
    public static class PdfiumLibrary
    {
        private static readonly object Gate = new object();
        private static bool _loaded;

        public static string DllPath { get; private set; } = "";

        public static void Load()
        {
            lock (Gate)
            {
                if (_loaded) return;
                string dir = Path.GetDirectoryName(typeof(PdfiumLibrary).Assembly.Location)!;
                DllPath = Path.Combine(dir, "pdfium.dll");
                IntPtr h = PdfiumNative.LoadLibraryEx(DllPath, IntPtr.Zero, PdfiumNative.LOAD_WITH_ALTERED_SEARCH_PATH);
                // No path in the message (paths may identify the user / document location).
                if (h == IntPtr.Zero) throw new InvalidOperationException("pdfium.dll load failed: win32 error " + Marshal.GetLastWin32Error());
                PdfiumNative.FPDF_InitLibrary();
                _loaded = true;
            }
        }
    }
}
