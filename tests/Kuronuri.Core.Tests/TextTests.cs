using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Kuronuri.Core.Text;
using Xunit;

namespace Kuronuri.Core.Tests
{
    /// <summary>Builds PDFs whose pages contain the given ASCII lines (Helvetica 12pt, 20pt apart, from y = top - 72).</summary>
    public static class TextPdf
    {
        public static byte[] Build(params (string[] Lines, int Rotate)[] pages)
        {
            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pagesObj = w.Reserve(), font = w.Reserve();
            w.WriteObject(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var kids = new StringBuilder();
            foreach (var (lines, rot) in pages)
            {
                var c = new StringBuilder("BT /F1 12 Tf 20 TL 72 770 Td\n");
                foreach (var l in lines) c.Append('(').Append(l.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj T*\n");
                c.Append("ET");
                int cont = w.Reserve(), pg = w.Reserve();
                w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes(c.ToString()));
                w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Rotate " + rot + " /Resources << /Font << /F1 " + font + " 0 R >> >> /Contents " + cont + " 0 R >>");
                kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
            }
            w.WriteObject(pagesObj, "<< /Type /Pages /Count " + pages.Length + " /Kids [" + kids + "] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.Finish(catalog);
            return ms.ToArray();
        }
    }

    public class TextTests
    {
        private static StandardData D => StandardData.Instance;

        private static List<PageText> ReadAll(byte[] pdf) => Pdf.Run(() =>
        {
            using var doc = PdfDoc.FromBytes(pdf);
            return Enumerable.Range(0, doc.PageCount).Select(i => PageText.Read(doc, i)).ToList();
        });

        [Fact]
        public void FindsTheSecretOnEveryPageAndTheRectsCoverItInDisplaySpace()
        {
            var texts = ReadAll(D.Td.Bytes);
            for (int i = 0; i < 5; i++)
            {
                var hits = TextSearch.Find(texts[i], "secret-text-12345", new TextSearchOptions());
                Assert.Single(hits);
                var h = hits[0];
                Assert.Contains("SECRET-TEXT-12345", h.Preview);
                // The factory's text box (user space) converted to display space must lie inside the hit rectangles' union.
                var g = D.Geoms[i];
                double[] tb = D.Td.TextBox[i];
                CoordMap.UserToDisplay(g, tb[0] + 6, tb[1] + 8, out double ax, out double ay);   // inside the glyph run
                CoordMap.UserToDisplay(g, tb[0] + 150, tb[1] + 16, out double bx, out double by);
                foreach (var p in new[] { new Point2(ax, ay), new Point2(bx, by), new Point2((ax + bx) / 2, (ay + by) / 2) })
                    Assert.True(h.Rects.Any(r => ShapeOps.Hit(r, p, 0)), "page " + (i + 1) + " point " + p);
                Assert.Single(h.Rects);   // one line (rotated pages give a vertical run, still one rectangle)
            }
        }

        [Fact]
        public void MatchingIgnoresWidthCaseAndWhitespace()
        {
            var t = ReadAll(D.Td.Bytes)[0];
            Assert.Single(TextSearch.Find(t, "ＳＥＣＲＥＴ－ＴＥＸＴ", new TextSearchOptions()));
            Assert.Single(TextSearch.Find(t, "secret - text", new TextSearchOptions()));
            Assert.Empty(TextSearch.Find(t, "secret - text", new TextSearchOptions { IgnoreWhitespace = false }));
            Assert.Empty(TextSearch.Find(t, "not in the page", new TextSearchOptions()));
            Assert.Empty(TextSearch.Find(t, "   ", new TextSearchOptions()));
        }

        [Fact]
        public void PresetsFindTypicalPersonalData()
        {
            byte[] pdf = TextPdf.Build((new[]
            {
                "Contact: TEL 03-1234-5678 or 090(1234)5678",
                "Mail: Taro.Yamada@example.lg.jp  Zip 100-0001",
                "Number 1234 5678 9012 and 123456789012 and 12345678901234",
                "Not a phone: 1234-5678",
            }, 0));
            var t = ReadAll(pdf)[0];
            string Pat(string name) => TextSearch.Presets.First(p => p.Name == name).Pattern;
            var rx = new TextSearchOptions { Regex = true };
            Assert.Equal(2, TextSearch.Find(t, Pat("電話番号"), rx).Count);
            Assert.Single(TextSearch.Find(t, Pat("メールアドレス"), rx));
            Assert.Single(TextSearch.Find(t, Pat("郵便番号"), rx));
            var twelve = TextSearch.Find(t, Pat("12 桁の番号"), rx);
            Assert.Equal(2, twelve.Count);   // the 14-digit run is not a 12-digit number
            Assert.Throws<ArgumentException>(() => TextSearch.Find(t, "(unclosed", rx));
        }

        [Fact]
        public void RangeAcrossLinesGivesOneRectanglePerLine()
        {
            byte[] pdf = TextPdf.Build((new[] { "First line of text", "Second line here", "Third" }, 0), (new[] { "Rotated First line", "Rotated Second line" }, 90));
            var texts = ReadAll(pdf);
            var t = texts[0];
            var (text, map) = TextSearch.BuildIndex(t, false);
            int a = text.IndexOf("line of", StringComparison.Ordinal), b = text.IndexOf("second", StringComparison.Ordinal) + "second".Length - 1;
            var rects = TextRects.ForRange(t, map[a], map[b]);
            Assert.Equal(2, rects.Count);
            Assert.True(rects[0].Y < rects[1].Y);
            Assert.True(rects[1].Y - rects[0].Y > 15);   // 20pt line spacing

            // 90° page: lines are vertical columns on screen; each still becomes one rectangle.
            var r = texts[1];
            var (rt, rm) = TextSearch.BuildIndex(r, false);
            int fa = rt.IndexOf("first line", StringComparison.Ordinal), sa = rt.IndexOf("second line", StringComparison.Ordinal);
            // PDFium may order the lines of a rotated page differently; cover from the earlier to the later match.
            int ra = Math.Min(fa, sa), rb = Math.Max(fa + 9, sa + 10);
            var vr = TextRects.ForRange(r, rm[ra], rm[rb]);
            Assert.Equal(2, vr.Count);
            Assert.All(vr, x => Assert.True(x.H > x.W * 2, "vertical run expected"));
        }

        [Fact]
        public void HitCharFindsTheCharacterUnderThePoint()
        {
            var t = ReadAll(D.Td.Bytes)[1];   // rotated page
            var hit = TextSearch.Find(t, "secret", new TextSearchOptions())[0];
            var b = t.Boxes[hit.First];
            Assert.Equal(hit.First, t.HitChar(new Point2((b.L + b.R) / 2, (b.T + b.B) / 2), 0.5));
            Assert.Equal(-1, t.HitChar(new Point2(1, 1), 0.5));
        }

        [Fact]
        public void HitsBecomeItemsThatBurnTheTextAndAreThenReportedCovered()
        {
            var texts = ReadAll(D.Td.Bytes);
            var hits = texts.SelectMany(t => TextSearch.Find(t, "secret-text-12345", new TextSearchOptions())).ToList();
            Assert.Equal(5, hits.Count);
            Assert.All(hits, h => Assert.False(TextSearch.IsCovered(h, new RedactionItem[0])));
            var items = hits.Select(h => TextSearch.ToItem(h, new Reason("1号", "個人情報", ""))).ToList();
            Assert.All(hits, h => Assert.True(TextSearch.IsCovered(h, items)));

            var ms = new MemoryStream();
            Pdf.Run(() => { using var doc = PdfDoc.FromBytes(D.Td.Bytes); FinalExporter.Export(doc, items, RedactionStyle.Black, ms, new FinalExportOptions()); });
            byte[] fin = ms.ToArray();
            Assert.Empty(OutputVerifier.VerifyStructure(fin, 5));
            Assert.Empty(Pdf.Run(() => OutputVerifier.VerifyWithPdfium(fin, D.Pages, items, RedactionStyle.Black)));
            Assert.Equal(0, Util.SecretCount(fin));
            // Every glyph box of the searched text is black in the output.
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(fin);
                foreach (var h in hits)
                {
                    using var page = doc.LoadPage(h.Page);
                    using var r = Raster.Render(page.Handle, 100);
                    var t = texts[h.Page];
                    for (int i = h.First; i <= h.Last; i++)
                    {
                        var b = t.Boxes[i];
                        if (!PageText.HasBox(b)) continue;
                        Assert.True(Util.Near(r, (b.L + b.R) / 2, (b.T + b.B) / 2, 0, 0, 0, 48, out string got), "page " + (h.Page + 1) + " char " + i + got);
                    }
                }
            });
        }

        [Fact]
        public void PageWithoutTextGivesNoHits()
        {
            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pages = w.Reserve(), cont = w.Reserve(), pg = w.Reserve();
            w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes("0 0 1 rg 100 100 200 50 re f"));
            w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents " + cont + " 0 R >>");
            w.WriteObject(pages, "<< /Type /Pages /Count 1 /Kids [" + pg + " 0 R] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.Finish(catalog);
            var t = ReadAll(ms.ToArray())[0];
            Assert.Equal(0, t.Count);
            Assert.Empty(TextSearch.Find(t, "x", new TextSearchOptions()));
        }

        [Fact]
        public void SameLineRuleHandlesHorizontalAndVerticalRuns()
        {
            var a = new BoxD(0, 0, 10, 12); var right = new BoxD(10.5, 0, 20, 12); var below = new BoxD(0, 12.5, 10, 24);
            var nextLineStart = new BoxD(-200, 20, -190, 32);
            Assert.True(TextRects.SameLine(a, right));
            Assert.True(TextRects.SameLine(a, below));          // vertical writing: next character is below
            Assert.False(TextRects.SameLine(a, nextLineStart)); // line break back to the left margin
        }
    }
}
