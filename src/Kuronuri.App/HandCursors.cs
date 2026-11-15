using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace Kuronuri.App
{
    /// <summary>
    /// Open / closed hand cursors for the hand tool (ARCHITECTURE §11.2). Windows has no grab cursor, so the hand is drawn
    /// here and turned into a cursor with CreateIconIndirect. WPF's Cursor(Stream) is not used: on .NET Framework it
    /// writes the stream to a temporary file. Falls back to a stock cursor when anything fails.
    /// </summary>
    internal static class HandCursors
    {
        private static readonly Dictionary<(bool Closed, int Size), Cursor> Cache = new Dictionary<(bool, int), Cursor>();

        public static Cursor Open(double dpiScale) => Get(false, dpiScale);
        public static Cursor Closed(double dpiScale) => Get(true, dpiScale);

        private static Cursor Get(bool closed, double dpiScale)
        {
            int size = Math.Max(32, (int)Math.Round(32 * dpiScale));
            if (Cache.TryGetValue((closed, size), out var c)) return c;
            try { c = Create(closed, size); }
            catch (Exception ex) when (ex is ExternalException || ex is InvalidOperationException || ex is ArgumentException) { c = closed ? Cursors.SizeAll : Cursors.Hand; }
            Cache[(closed, size)] = c;
            return c;
        }

        /// <summary>The hand outline in a 32×32 box (union of palm, fingers and thumb).</summary>
        private static System.Windows.Media.Geometry Shape(bool closed)
        {
            System.Windows.Media.Geometry g = new RectangleGeometry(new Rect(9.5, 13, 15, 13.5), 4, 4);   // palm
            double[] tops = closed ? new[] { 10.5, 9.5, 10, 11.5 } : new[] { 5, 3.5, 4.5, 7.5 };
            for (int i = 0; i < 4; i++)
                g = System.Windows.Media.Geometry.Combine(g, new RectangleGeometry(new Rect(9.5 + i * 3.75, tops[i], 3.75, 17 - tops[i]), 1.85, 1.85),
                    GeometryCombineMode.Union, null);
            var thumb = new RectangleGeometry(new Rect(-2, -1.9, closed ? 6 : 9, 3.8), 1.9, 1.9);
            thumb.Transform = new TransformGroup { Children = { new RotateTransform(closed ? 205 : 220), new TranslateTransform(11.5, 19.5) } };
            return System.Windows.Media.Geometry.Combine(g, thumb, GeometryCombineMode.Union, null);
        }

        private static Cursor Create(bool closed, int size)
        {
            double k = size / 32.0;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(k, k));
                var geo = Shape(closed);
                dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.4) { LineJoin = PenLineJoin.Round }, geo);
                // Lines between the fingers.
                var line = new Pen(Brushes.Black, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                for (int i = 1; i < 4; i++) dc.DrawLine(line, new Point(9.5 + i * 3.75, closed ? 13 : 11), new Point(9.5 + i * 3.75, 17.5));
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var bgra = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);   // icons take straight (not premultiplied) alpha
            var pixels = new byte[size * size * 4];
            bgra.CopyPixels(pixels, size * 4, 0);

            IntPtr color = CreateBitmap(size, size, 1, 32, pixels);
            IntPtr mask = CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
            try
            {
                if (color == IntPtr.Zero || mask == IntPtr.Zero) throw new InvalidOperationException("CreateBitmap failed");
                var info = new IconInfo { fIcon = false, xHotspot = (int)Math.Round(16 * k), yHotspot = (int)Math.Round(15 * k), hbmMask = mask, hbmColor = color };
                IntPtr h = CreateIconIndirect(ref info);
                if (h == IntPtr.Zero) throw new InvalidOperationException("CreateIconIndirect failed");
                return CursorInteropHelper.Create(new CursorHandle(h));
            }
            finally
            {
                if (color != IntPtr.Zero) DeleteObject(color);
                if (mask != IntPtr.Zero) DeleteObject(mask);
            }
        }

        private sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public CursorHandle(IntPtr h) : base(true) { SetHandle(h); }
            protected override bool ReleaseHandle() => DestroyIcon(handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32", SetLastError = true)] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
        [DllImport("user32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("gdi32")] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPel, byte[] bits);
        [DllImport("gdi32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    }
}
