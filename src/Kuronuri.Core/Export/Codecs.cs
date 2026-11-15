using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Rendering;

namespace Kuronuri.Core.Export
{
    public static unsafe class JpegCodec
    {
        /// <summary>True when the page has a meaningful amount of saturated pixels (e.g. a red seal).</summary>
        public static bool DetectColor(IntPtr bgrx, long pixelCount)
        {
            uint* p = (uint*)bgrx;
            long limit = Math.Max(50, pixelCount / 50000), colored = 0;
            for (long i = 0; i < pixelCount; i++)
            {
                uint v = p[i];
                int b = (int)(v & 0xFF), g = (int)((v >> 8) & 0xFF), r = (int)((v >> 16) & 0xFF);
                int mx = r > g ? (r > b ? r : b) : (g > b ? g : b);
                int mn = r < g ? (r < b ? r : b) : (g < b ? g : b);
                if (mx - mn > 40 && ++colored >= limit) return true;
            }
            return false;
        }

        /// <summary>Encodes with the in-box WIC encoder. Gray pages go through a Gray8 copy held in grayScratch.</summary>
        public static void Encode(IntPtr bgrx, NativeBuffer grayScratch, int w, int h, bool color, int quality, MemoryStream output)
        {
            BitmapSource src;
            if (color)
            {
                src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgrx, checked(w * h * 4), w * 4);
            }
            else
            {
                long n = (long)w * h;
                grayScratch.Ensure(n);
                uint* p = (uint*)bgrx; byte* q = (byte*)grayScratch.Ptr;
                for (long i = 0; i < n; i++)
                {
                    uint v = p[i];
                    q[i] = (byte)((((v >> 16) & 0xFF) * 77 + ((v >> 8) & 0xFF) * 150 + (v & 0xFF) * 29 + 128) >> 8);
                }
                src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, grayScratch.Ptr, checked(w * h), w);
            }
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(src));
            output.SetLength(0);
            enc.Save(output);
        }

        /// <summary>Number of components from the SOF marker (1 = gray, 3 = color), or 0 when not a JPEG.</summary>
        public static int Components(byte[] jpeg, int length)
        {
            if (length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return 0;
            int i = 2;
            while (i + 9 < length && jpeg[i] == 0xFF)
            {
                int marker = jpeg[i + 1];
                int seg = (jpeg[i + 2] << 8) | jpeg[i + 3];
                bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (sof) return jpeg[i + 9];
                i += 2 + seg;
            }
            return 0;
        }
    }

    /// <summary>
    /// Lossless alternative to JPEG (zlib over raw Gray8 / RGB24, no predictor). Clean born-digital pages compress
    /// better this way; scanned pages do not, so a cheap probe on sample bands decides whether the full pass is worth it.
    /// </summary>
    public sealed unsafe class LosslessCodec
    {
        private byte[] _raw = new byte[0];

        /// <returns>zlib data when it is smaller than the JPEG, otherwise null.</returns>
        public byte[]? TryEncode(IntPtr bgrx, IntPtr gray8, int w, int h, bool color, int jpegBytes)
        {
            int rowBytes = w * (color ? 3 : 1);
            long total = (long)rowBytes * h;
            if (total > int.MaxValue - 64) return null;
            if (_raw.Length < total) _raw = new byte[total];
            fixed (byte* dst = _raw)
            {
                if (color)
                {
                    uint* p = (uint*)bgrx; byte* d = dst;
                    for (long i = 0; i < (long)w * h; i++) { uint v = p[i]; *d++ = (byte)(v >> 16); *d++ = (byte)(v >> 8); *d++ = (byte)v; }
                }
                else Buffer.MemoryCopy((void*)gray8, dst, total, total);
            }

            const int bands = 8, bandRows = 64;
            if (h > bands * bandRows)
            {
                using (var ms = new MemoryStream())
                {
                    using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                        for (int k = 0; k < bands; k++)
                        {
                            int row = Math.Min(h - bandRows, Math.Max(0, h * (2 * k + 1) / (2 * bands) - bandRows / 2));
                            ds.Write(_raw, row * rowBytes, bandRows * rowBytes);
                        }
                    double estimate = ms.Length * (double)h / (bands * bandRows);
                    if (estimate >= 0.9 * jpegBytes) return null;
                }
            }
            byte[] z = MiniPdfWriter.ZlibCompress(_raw, 0, (int)total, System.IO.Compression.CompressionLevel.Optimal);
            return z.Length < jpegBytes ? z : null;
        }
    }
}
