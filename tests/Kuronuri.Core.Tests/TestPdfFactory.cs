using System.Collections.Generic;
using System.IO;
using System.Text;
using Kuronuri.Core.Export;

namespace Kuronuri.Core.Tests
{
    /// <summary>Generates synthetic test PDFs without embedding fonts (standard Helvetica, ASCII only).</summary>
    public static class TestPdfFactory
    {
        public sealed class PageSpec
        {
            public double[] MediaBox = { 0, 0, 595, 842 };
            public double[]? CropBox;
            public int Rotate;
            public bool Unbalanced;     // leaves a modified CTM at the end of the content stream
            public bool NoMarkers;
            public int TextLines;
        }
        public sealed class Marker { public string Name = ""; public double UX, UY; public int R, G, B; }
        public sealed class Result
        {
            public byte[] Bytes = new byte[0];
            public List<Marker[]> Markers = new List<Marker[]>();
            public List<double[]> TextBox = new List<double[]>();   // x, y, w, h in user space
            public List<double[]> BlueBox = new List<double[]>();
        }

        public const string SecretText = "SECRET-TEXT-12345";

        public static Result Build(IList<PageSpec> specs, bool withJunk)
        {
            string F(double v) => MiniPdfWriter.F(v);
            var res = new Result();
            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pages = w.Reserve(), font = w.Reserve();
            w.WriteObject(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var kids = new StringBuilder();
            int firstPage = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                double[] box = s.CropBox ?? s.MediaBox;
                double x0 = box[0], y0 = box[1], x1 = box[2], y1 = box[3];
                var c = new StringBuilder();
                if (!s.NoMarkers)
                {
                    c.Append("1 0 0 rg ").Append(F(x0 + 50)).Append(' ').Append(F(y0 + 60)).Append(" 40 40 re f\n");
                    c.Append("0 0.6 0 rg ").Append(F(x1 - 130)).Append(' ').Append(F(y1 - 190)).Append(" 40 40 re f\n");
                    c.Append("0 0 1 rg ").Append(F(x0 + 200)).Append(' ').Append(F(y0 + 300)).Append(" 60 20 re f\n");
                }
                c.Append("0 g BT /F1 18 Tf ").Append(F(x0 + 100)).Append(' ').Append(F(y0 + 500)).Append(" Td (").Append(SecretText).Append(") Tj ET\n");
                if (s.TextLines > 0)
                {
                    c.Append("BT /F1 9 Tf 11 TL ").Append(F(x0 + 40)).Append(' ').Append(F(y1 - 40)).Append(" Td\n");
                    for (int k = 0; k < s.TextLines; k++)
                        c.Append("(Line ").Append(k).Append(": The quick brown fox jumps over the lazy dog 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ) Tj T*\n");
                    c.Append("ET\n");
                }
                if (s.Unbalanced) c.Append("0.5 0 0 0.5 200 200 cm\n");

                res.Markers.Add(s.NoMarkers ? new Marker[0] : new[]
                {
                    new Marker { Name = "red", UX = x0 + 70, UY = y0 + 80, R = 255, G = 0, B = 0 },
                    new Marker { Name = "green", UX = x1 - 110, UY = y1 - 170, R = 0, G = 153, B = 0 },
                    new Marker { Name = "blue", UX = x0 + 230, UY = y0 + 310, R = 0, G = 0, B = 255 },
                });
                res.TextBox.Add(new[] { x0 + 96, y0 + 494, 200.0, 26.0 });
                res.BlueBox.Add(new[] { x0 + 200, y0 + 300, 60.0, 20.0 });

                int cont = w.Reserve(), pg = w.Reserve();
                if (i == 0) firstPage = pg;
                w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes(c.ToString()));
                string annots = "";
                if (withJunk && i == 0)
                {
                    int an = w.Reserve();
                    w.WriteObject(an, "<< /Type /Annot /Subtype /Text /Rect [" + F(x0 + 400) + " " + F(y0 + 700) + " " + F(x0 + 420) + " " + F(y0 + 720) + "] /Contents (SECRET-ANNOT) >>");
                    annots = " /Annots [" + an + " 0 R]";
                }
                w.WriteObject(pg, "<< /Type /Page /Parent " + pages + " 0 R /MediaBox [" + F(s.MediaBox[0]) + " " + F(s.MediaBox[1]) + " " + F(s.MediaBox[2]) + " " + F(s.MediaBox[3]) + "]" +
                    (s.CropBox == null ? "" : " /CropBox [" + F(s.CropBox[0]) + " " + F(s.CropBox[1]) + " " + F(s.CropBox[2]) + " " + F(s.CropBox[3]) + "]") +
                    " /Rotate " + s.Rotate + " /Resources << /Font << /F1 " + font + " 0 R >> >> /Contents " + cont + " 0 R" + annots + " >>");
                kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
            }
            w.WriteObject(pages, "<< /Type /Pages /Count " + specs.Count + " /Kids [" + kids + "] >>");

            string extraCatalog = "", extraTrailer = "";
            if (withJunk)
            {
                int info = w.Reserve(), outlines = w.Reserve(), item = w.Reserve(), ef = w.Reserve(), fs = w.Reserve(), xmp = w.Reserve();
                w.WriteObject(info, "<< /Title (SECRET-META-TITLE) /Author (SECRET-AUTHOR) >>");
                w.WriteObject(outlines, "<< /Type /Outlines /First " + item + " 0 R /Last " + item + " 0 R /Count 1 >>");
                w.WriteObject(item, "<< /Title (SECRET-BOOKMARK) /Parent " + outlines + " 0 R /Dest [" + firstPage + " 0 R /Fit] >>");
                w.WriteStream(ef, "/Type /EmbeddedFile", MiniPdfWriter.Latin1.GetBytes("SECRET-ATTACH-PAYLOAD"));
                w.WriteObject(fs, "<< /Type /Filespec /F (secret.txt) /UF (secret.txt) /EF << /F " + ef + " 0 R >> >>");
                w.WriteStream(xmp, "/Type /Metadata /Subtype /XML", MiniPdfWriter.Latin1.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">SECRET-XMP</x:xmpmeta>"));
                extraCatalog = " /Outlines " + outlines + " 0 R /Names << /EmbeddedFiles << /Names [(secret.txt) " + fs + " 0 R] >> >> /Metadata " + xmp + " 0 R";
                extraTrailer = " /Info " + info + " 0 R";
            }
            w.WriteObject(catalog, "<< /Type /Catalog /Pages " + pages + " 0 R" + extraCatalog + " >>");
            w.Finish(catalog, extraTrailer);
            res.Bytes = ms.ToArray();
            return res;
        }

        public static List<PageSpec> StandardSpecs() => new List<PageSpec>
        {
            new PageSpec(),                                                                             // A4 portrait
            new PageSpec { Rotate = 90 },
            new PageSpec { MediaBox = new double[] { 0, 0, 1191, 842 }, Rotate = 180 },                 // A3 landscape
            new PageSpec { MediaBox = new double[] { 100, 50, 695, 892 }, CropBox = new double[] { 120, 80, 675, 862 }, Rotate = 270 },
            new PageSpec { Unbalanced = true },
        };
    }
}
