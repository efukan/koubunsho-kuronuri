using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Kuronuri.Core.Pdfium
{
    /// <summary>
    /// Owns a PDFium document handle together with the memory / stream / delegate it depends on.
    /// Must only be used on the PDFium worker thread (see <see cref="PdfiumWorker"/>).
    /// </summary>
    public sealed class PdfDoc : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private GCHandle _pin;
        private byte[]? _clearOnDispose;
        private IntPtr _fileAccess;
        private PdfiumNative.GetBlockCallback? _callback;   // must stay referenced while the document is open
        private Stream? _stream;

        private PdfDoc() { }

        public int PageCount => PdfiumNative.FPDF_GetPageCount(Handle);

        public static PdfDoc CreateNew()
        {
            var d = new PdfDoc { Handle = PdfiumNative.FPDF_CreateNewDocument() };
            if (d.Handle == IntPtr.Zero) throw new InvalidOperationException("FPDF_CreateNewDocument failed");
            return d;
        }

        /// <summary>The byte array is pinned until Dispose. With <paramref name="clearOnDispose"/> it is also zero-cleared then.</summary>
        public static PdfDoc FromBytes(byte[] data, bool clearOnDispose = false)
        {
            var d = new PdfDoc();
            d._pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            if (clearOnDispose) d._clearOnDispose = data;
            d.Handle = PdfiumNative.FPDF_LoadMemDocument64(d._pin.AddrOfPinnedObject(), (UIntPtr)(ulong)data.LongLength, null);
            if (d.Handle == IntPtr.Zero) { uint e = PdfiumNative.FPDF_GetLastError(); d.Dispose(); throw new PdfLoadException(PdfLoadException.FromPdfiumError(e)); }
            return d;
        }

        /// <summary>Opens a file read-only (FileShare.Read) and reads it on demand. The original is never written.</summary>
        public static PdfDoc FromFile(string path)
        {
            FileStream fs;
            try { fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            { throw new PdfLoadException(PdfErrorKind.File); }
            return FromStream(fs);
        }

        /// <summary>Reads on demand through a callback. The stream is owned and closed by this object.</summary>
        public static PdfDoc FromStream(Stream stream)
        {
            if (stream.Length > uint.MaxValue) { stream.Dispose(); throw new PdfLoadException(PdfErrorKind.TooLarge); }
            var d = new PdfDoc { _stream = stream };
            byte[] chunk = new byte[64 * 1024];
            d._callback = (param, position, buffer, size) =>
            {
                try
                {
                    stream.Position = position;
                    uint done = 0;
                    while (done < size)
                    {
                        int n = stream.Read(chunk, 0, (int)Math.Min((uint)chunk.Length, size - done));
                        if (n <= 0) return 0;
                        Marshal.Copy(chunk, 0, IntPtr.Add(buffer, (int)done), n);
                        done += (uint)n;
                    }
                    return 1;
                }
                catch { return 0; }
            };
            var fa = new PdfiumNative.FPDF_FILEACCESS
            {
                FileLen = checked((uint)stream.Length),
                GetBlock = Marshal.GetFunctionPointerForDelegate(d._callback),
                Param = IntPtr.Zero,
            };
            d._fileAccess = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PdfiumNative.FPDF_FILEACCESS)));
            Marshal.StructureToPtr(fa, d._fileAccess, false);
            d.Handle = PdfiumNative.FPDF_LoadCustomDocument(d._fileAccess, null);
            if (d.Handle == IntPtr.Zero) { uint e = PdfiumNative.FPDF_GetLastError(); d.Dispose(); throw new PdfLoadException(PdfLoadException.FromPdfiumError(e)); }
            return d;
        }

        public PdfPage LoadPage(int index) => new PdfPage(this, index);

        public void SaveTo(Stream output, uint flags)
        {
            byte[] chunk = new byte[64 * 1024];
            Exception? error = null;
            PdfiumNative.WriteBlockCallback cb = (self, data, size) =>
            {
                try
                {
                    uint done = 0;
                    while (done < size)
                    {
                        int n = (int)Math.Min((uint)chunk.Length, size - done);
                        Marshal.Copy(IntPtr.Add(data, (int)done), chunk, 0, n);
                        output.Write(chunk, 0, n);
                        done += (uint)n;
                    }
                    return 1;
                }
                catch (Exception e) { error = e; return 0; }
            };
            var fw = new PdfiumNative.FPDF_FILEWRITE { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(cb) };
            bool ok = PdfiumNative.FPDF_SaveAsCopy(Handle, ref fw, flags);
            GC.KeepAlive(cb);
            Array.Clear(chunk, 0, chunk.Length);
            if (error != null) throw new IOException("write failed", error);
            if (!ok) throw new InvalidOperationException("FPDF_SaveAsCopy failed");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { PdfiumNative.FPDF_CloseDocument(Handle); Handle = IntPtr.Zero; }
            if (_pin.IsAllocated) _pin.Free();
            if (_clearOnDispose != null) { Array.Clear(_clearOnDispose, 0, _clearOnDispose.Length); _clearOnDispose = null; }
            if (_fileAccess != IntPtr.Zero) { Marshal.FreeHGlobal(_fileAccess); _fileAccess = IntPtr.Zero; }
            _stream?.Dispose(); _stream = null;
            _callback = null;
        }
    }

    /// <summary>A loaded page; closes itself on Dispose.</summary>
    public sealed class PdfPage : IDisposable
    {
        public IntPtr Handle { get; private set; }
        public int Index { get; }

        internal PdfPage(PdfDoc doc, int index)
        {
            Index = index;
            Handle = PdfiumNative.FPDF_LoadPage(doc.Handle, index);
            if (Handle == IntPtr.Zero) throw new PdfLoadException(PdfErrorKind.Page);
        }

        public double Width => PdfiumNative.FPDF_GetPageWidthF(Handle);
        public double Height => PdfiumNative.FPDF_GetPageHeightF(Handle);

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { PdfiumNative.FPDF_ClosePage(Handle); Handle = IntPtr.Zero; }
        }
    }
}
