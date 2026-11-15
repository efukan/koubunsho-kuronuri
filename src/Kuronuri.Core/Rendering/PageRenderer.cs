using System;
using System.Runtime.InteropServices;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Rendering
{
    /// <summary>Unmanaged, reusable pixel buffer. Zero-cleared before it is freed.</summary>
    public sealed unsafe class NativeBuffer : IDisposable
    {
        public IntPtr Ptr { get; private set; }
        public long Capacity { get; private set; }

        public void Ensure(long bytes)
        {
            if (bytes <= Capacity) return;
            Release();
            Ptr = Marshal.AllocHGlobal(new IntPtr(bytes));
            Capacity = bytes;
        }

        public void Clear()
        {
            if (Ptr == IntPtr.Zero) return;
            ulong* q = (ulong*)Ptr; long n = Capacity / 8;
            for (long i = 0; i < n; i++) q[i] = 0;
            byte* b = (byte*)Ptr; for (long i = n * 8; i < Capacity; i++) b[i] = 0;
        }

        private void Release()
        {
            if (Ptr == IntPtr.Zero) return;
            Clear(); Marshal.FreeHGlobal(Ptr); Ptr = IntPtr.Zero; Capacity = 0;
        }

        public void Dispose() => Release();
    }

    public static class PageRenderer
    {
        /// <summary>The same flags are used for screen, final output and printing.</summary>
        public const int RenderFlags = PdfiumNative.FPDF_ANNOT;

        /// <summary>
        /// Largest side the in-box JPEG encoder accepts (the format limit is 65,535). Pages with an extreme aspect ratio can
        /// stay under the pixel budget and still exceed it, so every encoded rendering also limits the longer side.
        /// </summary>
        public const int MaxEncodableSide = 65_000;

        /// <param name="maxSide">Upper bound for either side (use <see cref="MaxEncodableSide"/> for anything that is encoded).</param>
        public static void PixelSize(double wd, double hd, double dpi, long maxPixels, out int wpx, out int hpx, out double effectiveDpi,
            int maxSide = int.MaxValue)
        {
            double scale = dpi / 72.0;
            wpx = Math.Max(1, (int)Math.Ceiling(wd * scale - 1e-6));
            hpx = Math.Max(1, (int)Math.Ceiling(hd * scale - 1e-6));
            if ((long)wpx * hpx > maxPixels)
            {
                scale *= Math.Sqrt((double)maxPixels / ((double)wpx * hpx));
                wpx = Math.Max(1, (int)Math.Floor(wd * scale));
                hpx = Math.Max(1, (int)Math.Floor(hd * scale));
            }
            if (wpx > maxSide || hpx > maxSide)
            {
                scale *= (double)maxSide / Math.Max(wpx, hpx);
                wpx = Math.Max(1, Math.Min(maxSide, (int)Math.Floor(wd * scale)));
                hpx = Math.Max(1, Math.Min(maxSide, (int)Math.Floor(hd * scale)));
            }
            effectiveDpi = scale * 72.0;
        }

        /// <summary>Renders the whole page into a BGRx buffer of wpx*hpx*4 bytes (white background).</summary>
        public static void RenderFull(IntPtr page, IntPtr buffer, int wpx, int hpx) =>
            RenderRegion(page, buffer, wpx, hpx, 0, 0, wpx, hpx);

        /// <summary>
        /// Renders only the region [rx, rx+rw) x [ry, ry+rh) of a page that is conceptually fullW x fullH pixels,
        /// into a BGRx buffer of rw*rh*4 bytes. Pixel (0,0) of the buffer is pixel (rx, ry) of the full rendering.
        /// </summary>
        public static void RenderRegion(IntPtr page, IntPtr buffer, int fullW, int fullH, int rx, int ry, int rw, int rh)
        {
            IntPtr bmp = PdfiumNative.FPDFBitmap_CreateEx(rw, rh, PdfiumNative.FPDFBitmap_BGRx, buffer, rw * 4);
            if (bmp == IntPtr.Zero) throw new InvalidOperationException("FPDFBitmap_CreateEx failed");
            try
            {
                PdfiumNative.FPDFBitmap_FillRect(bmp, 0, 0, rw, rh, 0xFFFFFFFF);
                PdfiumNative.FPDF_RenderPageBitmap(bmp, page, -rx, -ry, fullW, fullH, 0, RenderFlags);
            }
            finally { PdfiumNative.FPDFBitmap_Destroy(bmp); }
        }
    }

    /// <summary>A rendered page with pixel access in display page coordinates (pt).</summary>
    public sealed unsafe class Raster : IDisposable
    {
        private readonly NativeBuffer _buf = new NativeBuffer();
        public int W, H;
        public double Sx, Sy;
        public IntPtr Ptr => _buf.Ptr;

        public static Raster Render(IntPtr page, double dpi)
        {
            var r = new Raster();
            double wd = PdfiumNative.FPDF_GetPageWidthF(page), hd = PdfiumNative.FPDF_GetPageHeightF(page);
            PageRenderer.PixelSize(wd, hd, dpi, long.MaxValue, out r.W, out r.H, out _);
            r.Sx = r.W / wd; r.Sy = r.H / hd;
            r._buf.Ensure((long)r.W * r.H * 4);
            PageRenderer.RenderFull(page, r._buf.Ptr, r.W, r.H);
            return r;
        }

        public uint Pixel(int x, int y) => ((uint*)_buf.Ptr)[(long)y * W + x];

        public void Rgb(double xd, double yd, out int r, out int g, out int b)
        {
            int x = Math.Min(W - 1, Math.Max(0, (int)(xd * Sx))), y = Math.Min(H - 1, Math.Max(0, (int)(yd * Sy)));
            uint v = ((uint*)_buf.Ptr)[(long)y * W + x];
            b = (int)(v & 0xFF); g = (int)((v >> 8) & 0xFF); r = (int)((v >> 16) & 0xFF);
        }

        public void Dispose() => _buf.Dispose();
    }
}
