using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Tests
{
    /// <summary>All PDFium calls in the tests go through one shared worker, exactly like the app.</summary>
    public static class Pdf
    {
        private static readonly Lazy<PdfiumWorker> Lazy = new Lazy<PdfiumWorker>(() => new PdfiumWorker());
        public static PdfiumWorker Worker => Lazy.Value;
        public static T Run<T>(Func<T> f) => Worker.Invoke(f);
        public static void Run(Action a) => Worker.Invoke(a);
    }

    /// <summary>The PoC "P1" data set: standard 5-page PDF with junk, one rectangle + one stroke per page.</summary>
    public sealed class StandardData
    {
        public TestPdfFactory.Result Td = new TestPdfFactory.Result();
        public List<PageGeometry> Geoms = new List<PageGeometry>();
        public List<PageInfo> Pages = new List<PageInfo>();
        public List<RedactionItem> Items = new List<RedactionItem>();
        public byte[] Json = new byte[0];

        private static readonly Lazy<StandardData> Shared = new Lazy<StandardData>(Create);
        public static StandardData Instance => Shared.Value;

        private static StandardData Create()
        {
            var d = new StandardData { Td = TestPdfFactory.Build(TestPdfFactory.StandardSpecs(), true) };
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(d.Td.Bytes);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    PageGeometry g;
                    using (var p = doc.LoadPage(i)) g = PageGeometry.FromPage(p.Handle);
                    d.Geoms.Add(g);
                    d.Pages.Add(new PageInfo(g.Wd, g.Hd, g.Rotation * 90));
                    double[] tb = d.Td.TextBox[i], bb = d.Td.BlueBox[i];
                    CoordMap.UserToDisplay(g, tb[0], tb[1], out double ax, out double ay);
                    CoordMap.UserToDisplay(g, tb[0] + tb[2], tb[1] + tb[3], out double bx, out double by);
                    var rect = new RectShape(Math.Min(ax, bx) - 4, Math.Min(ay, by) - 4, Math.Abs(bx - ax) + 8, Math.Abs(by - ay) + 8);
                    var pts = new List<Point2>();
                    foreach (double ux in new[] { bb[0] + 5, bb[0] + bb[2] / 2, bb[0] + bb[2] - 5 })
                    {
                        CoordMap.UserToDisplay(g, ux, bb[1] + bb[3] / 2, out double px, out double py);
                        pts.Add(new Point2(px, py));
                    }
                    d.Items.Add(new RedactionItem(Guid.NewGuid(), i, new Shape[] { rect }, new Reason("1号", "個人情報", "")));
                    d.Items.Add(new RedactionItem(Guid.NewGuid(), i, new Shape[] { new StrokeShape(pts, 30) }, null));
                }
            });
            var project = new RedactionProject(new OriginalInfo(Util.Sha(d.Td.Bytes), d.Td.Bytes.Length, "original.pdf"), d.Pages, d.Items, RedactionStyle.Black);
            d.Json = ProjectJson.Serialize(project, "0.0.0-test");
            return d;
        }
    }

    public static class Util
    {
        public static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

        public static int Count(byte[] hay, byte[] needle)
        {
            int c = 0;
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) c++;
            }
            return c;
        }

        public static int SecretCount(byte[] pdf) =>
            Count(pdf, Encoding.ASCII.GetBytes("SECRET")) + Count(pdf, Encoding.Unicode.GetBytes("SECRET")) + Count(pdf, Encoding.BigEndianUnicode.GetBytes("SECRET"));

        public static int CharCount(IntPtr page)
        {
            IntPtr tp = N.FPDFText_LoadPage(page);
            if (tp == IntPtr.Zero) return -1;
            int n = N.FPDFText_CountChars(tp);
            N.FPDFText_ClosePage(tp);
            return n;
        }

        public static string PageText(IntPtr page)
        {
            IntPtr tp = N.FPDFText_LoadPage(page);
            if (tp == IntPtr.Zero) return "";
            int n = N.FPDFText_CountChars(tp);
            var buf = new byte[(n + 1) * 2];
            int got = n > 0 ? N.FPDFText_GetText(tp, 0, n, buf) : 0;
            N.FPDFText_ClosePage(tp);
            return got > 0 ? Encoding.Unicode.GetString(buf, 0, (got - 1) * 2) : "";
        }

        public static bool Near(Raster r, double xd, double yd, int er, int eg, int eb, int tol, out string got)
        {
            r.Rgb(xd, yd, out int cr, out int cg, out int cb);
            got = "(" + cr + "," + cg + "," + cb + ")";
            return Math.Abs(cr - er) <= tol && Math.Abs(cg - eg) <= tol && Math.Abs(cb - eb) <= tol;
        }

        public static T WithPage<T>(PdfDoc doc, int index, Func<IntPtr, T> f)
        {
            using var p = doc.LoadPage(index);
            return f(p.Handle);
        }
    }
}
