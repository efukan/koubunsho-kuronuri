using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Printing;
using Kuronuri.Core.Rendering;
using Xunit;

namespace Kuronuri.Core.Tests
{
    /// <summary>Page order, user rotation and exclusion (ARCHITECTURE §2.4, §4.4).</summary>
    public class LayoutTests
    {
        private static StandardData D => StandardData.Instance;

        /// <summary>Reversed order, every user rotation, page 3 (base index 2) excluded.</summary>
        private static PageLayout Mixed() => new PageLayout(new[] { 4, 3, 2, 1, 0 }, new[] { 1, 0, 2, 3, 1 }, new[] { false, false, true, false, false });

        [Fact]
        public void ToViewAndFromViewAreInverseAndMatchTheMatrix()
        {
            double wd = 595, hd = 842;
            var p = new Point2(100, 30);
            for (int e = 0; e < 4; e++)
            {
                var v = CoordMap.ToView(e, wd, hd, p);
                var (vw, vh) = CoordMap.ViewSize(e, wd, hd);
                Assert.InRange(v.X, 0, vw); Assert.InRange(v.Y, 0, vh);
                Assert.Equal(p, CoordMap.FromView(e, wd, hd, v));
                double[] m = CoordMap.ToViewMatrix(e, wd, hd);
                Assert.Equal(v.X, m[0] * p.X + m[2] * p.Y + m[4], 9);
                Assert.Equal(v.Y, m[1] * p.X + m[3] * p.Y + m[5], 9);
            }
            // 90° clockwise: the top-left corner goes to the top-right.
            Assert.Equal(new Point2(842, 0), CoordMap.ToView(1, wd, hd, new Point2(0, 0)));
            var r = (RectShape)CoordMap.ToView(1, wd, hd, new RectShape(10, 20, 100, 30));
            Assert.Equal(new RectShape(842 - 50, 10, 30, 100), r);
        }

        [Fact]
        public void RotatedRenderingPutsContentWhereToViewSays()
        {
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                for (int i = 0; i < 5; i++)
                    for (int e = 1; e < 4; e++)
                    {
                        using var page = doc.LoadPage(i);
                        using var r = Raster.Render(page.Handle, 72, e);
                        var g = D.Geoms[i];
                        foreach (var mk in D.Td.Markers[i])
                        {
                            CoordMap.UserToDisplay(g, mk.UX, mk.UY, out double xd, out double yd);
                            var v = CoordMap.ToView(e, g.Wd, g.Hd, new Point2(xd, yd));
                            Assert.True(Util.Near(r, v.X, v.Y, mk.R, mk.G, mk.B, 16, out string got), "page " + (i + 1) + " rot " + e + " " + mk.Name + got);
                        }
                    }
            });
        }

        [Fact]
        public void LayoutOperationsAndValidation()
        {
            var l = PageLayout.Identity(4);
            Assert.True(l.IsIdentity && l.IsValidFor(4) && !l.IsValidFor(5));
            var m = l.Move(0, 3).Rotate(2, 1).Rotate(2, -2).SetExcluded(1, true);
            Assert.Equal(new[] { 1, 2, 3, 0 }, m.Order);
            Assert.Equal(3, m.RotationOf(2));
            Assert.True(m.IsExcluded(1));
            Assert.Equal(3, m.ViewOf(0));
            Assert.Equal(3, m.IncludedCount);
            Assert.False(m.IsIdentity);
            Assert.True(m.SameAs(new PageLayout(new[] { 1, 2, 3, 0 }, new[] { 0, 0, 3, 0 }, new[] { false, true, false, false })));
            Assert.False(new PageLayout(new[] { 0, 0, 1, 2 }, new int[4], new bool[4]).IsValidFor(4));
            Assert.False(new PageLayout(new[] { 0, 1, 2, 3 }, new[] { 0, 4, 0, 0 }, new bool[4]).IsValidFor(4));
        }

        [Fact]
        public void LayoutRoundTripsThroughJsonAndSchemaOneStillReads()
        {
            var p = new RedactionProject(new OriginalInfo("ab", 10, "x.pdf"), D.Pages, D.Items, RedactionStyle.Black, Mixed());
            var back = ProjectJson.Deserialize(ProjectJson.Serialize(p, "1"));
            Assert.True(back.Layout!.SameAs(Mixed()));
            Assert.Contains("\"layout\":{\"order\":[4,3,2,1,0]", Encoding.UTF8.GetString(ProjectJson.Serialize(p, "1")));

            string v1 = "{\"app\":\"kuronuri\",\"schema\":1,\"appVersion\":\"1.0.0\",\"original\":{\"sha256\":\"x\",\"length\":1,\"fileName\":\"a.pdf\"}," +
                "\"pages\":[{\"w\":595,\"h\":842,\"rot\":0}],\"style\":\"black\",\"items\":[]}";
            var old = ProjectJson.Deserialize(Encoding.UTF8.GetBytes(v1));
            Assert.Null(old.Layout);
            Assert.True(old.EffectiveLayout.IsIdentity);

            string bad = v1.Replace("\"schema\":1", "\"schema\":2").Replace("\"items\":[]", "\"items\":[],\"layout\":{\"order\":[1],\"rot\":[0],\"excluded\":[false]}");
            Assert.ThrowsAny<Exception>(() => ProjectJson.Deserialize(Encoding.UTF8.GetBytes(bad)));
        }

        [Fact]
        public void DisplayOrderFollowsThePageOrderAndTheViewPosition()
        {
            var pages = new[] { new PageInfo(100, 200, 0), new PageInfo(100, 200, 0) };
            var a = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(10, 10, 5, 5) }, null);    // top-left of page 0
            var b = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(80, 150, 5, 5) }, null);   // bottom-right of page 0
            var c = new RedactionItem(Guid.NewGuid(), 1, new Shape[] { new RectShape(10, 10, 5, 5) }, null);
            var swapped = new PageLayout(new[] { 1, 0 }, new[] { 2, 0 }, new bool[2]);   // page 1 first, page 0 upside down (rotations are per original page)
            Assert.Equal(new[] { c, b, a }, CoordMap.InDisplayOrder(new[] { a, b, c }, swapped, pages));
            Assert.Equal(ShapeMath.InDisplayOrder(new[] { a, b, c }), CoordMap.InDisplayOrder(new[] { a, b, c }, PageLayout.Identity(2), pages));
        }

        [Fact]
        public void SetLayoutIsUndoable()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            list.ResetLayout(PageLayout.Identity(3));
            u.Clear();
            var before = list.Layout!;
            u.Execute(new SetLayoutCommand(before, before.Move(2, 0)));
            Assert.Equal(new[] { 2, 0, 1 }, list.Layout!.Order);
            Assert.True(u.IsModified);
            u.Undo();
            Assert.True(list.Layout!.SameAs(before));
            Assert.False(u.IsModified);
        }

        [Fact]
        public void DisclosurePdfFollowsOrderRotationAndExclusion()
        {
            var layout = Mixed();
            var plan = OutputPlan.Build(D.Pages, D.Items, layout, includeExcluded: false);
            Assert.Equal(new[] { 4, 3, 1, 0 }, plan.Pages.Select(p => p.BasePage));
            string dir = TestDir.Make("layout-final");
            string dest = Path.Combine(dir, "out.pdf");
            var res = Pdf.Run(() => { using var doc = PdfDoc.FromBytes(D.Td.Bytes); return FinalOutput.Write(doc, plan, RedactionStyle.Black, dest, new string[0], new FinalExportOptions()); });
            Assert.Equal(4, res.Pages.Count);
            byte[] fin = File.ReadAllBytes(dest);
            Assert.Equal(0, Util.SecretCount(fin));
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(fin);
                Assert.Equal(4, doc.PageCount);
                for (int o = 0; o < 4; o++)
                {
                    var op = plan.Pages[o];
                    int b = op.BasePage, e = op.Rotation;
                    var g = D.Geoms[b];
                    using var page = doc.LoadPage(o);
                    Assert.Equal(op.Width, page.Width, 1);
                    Assert.Equal(op.Height, page.Height, 1);
                    using var r = Raster.Render(page.Handle, 100);
                    foreach (var mk in D.Td.Markers[b])
                    {
                        CoordMap.UserToDisplay(g, mk.UX, mk.UY, out double xd, out double yd);
                        var v = CoordMap.ToView(e, g.Wd, g.Hd, new Point2(xd, yd));
                        if (mk.Name == "blue") Assert.True(Util.Near(r, v.X, v.Y, 0, 0, 0, 48, out string g1), "out " + o + " blue burned" + g1);
                        else Assert.True(Util.Near(r, v.X, v.Y, mk.R, mk.G, mk.B, 40, out string g2), "out " + o + " " + mk.Name + g2);
                    }
                }
            });
        }

        [Fact]
        public void AllPagesExcludedIsRefused()
        {
            var layout = new PageLayout(new[] { 0, 1, 2, 3, 4 }, new int[5], Enumerable.Repeat(true, 5).ToArray());
            var plan = OutputPlan.Build(D.Pages, D.Items, layout, includeExcluded: false);
            Assert.Empty(plan.Pages);
            Assert.Throws<InvalidOperationException>(() => Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                return FinalExporter.Export(doc, plan, RedactionStyle.Black, new MemoryStream(), new FinalExportOptions());
            }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReviewPdfKeepsTheLayoutAndShowsOverlaysInPlace(bool raster)
        {
            var layout = Mixed();
            var project = new RedactionProject(new OriginalInfo(Util.Sha(D.Td.Bytes), D.Td.Bytes.Length, "o.pdf"), D.Pages, D.Items, RedactionStyle.Black, layout);
            string dir = TestDir.Make("layout-review-" + raster);
            string dest = Path.Combine(dir, "r.pdf");
            var saved = Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, project, "1", dest, new string[0], forceRaster: raster));
            Assert.Equal(raster ? ReviewSaveMethod.Raster : ReviewSaveMethod.Vector, saved.Method);
            var plan = OutputPlan.Build(D.Pages, D.Items, layout, includeExcluded: true);
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromFile(dest);
                var loaded = ReviewFile.TryLoad(doc);
                Assert.NotNull(loaded);
                Assert.True(loaded!.Project.Layout!.SameAs(layout));
                Assert.Equal(D.Td.Bytes, loaded.Original);
                Assert.Equal(5, doc.PageCount);
                for (int o = 0; o < 5; o++)
                {
                    var op = plan.Pages[o];
                    using var page = doc.LoadPage(o);
                    Assert.Equal(op.Width, page.Width, 1);
                    Assert.Equal(op.Height, page.Height, 1);
                    using var r = Raster.Render(page.Handle, 100);
                    // The rectangle over the secret text (item 2*b of the standard data) in view coordinates is tinted red.
                    var rect = (RectShape)plan.Items.First(i => i.PageIndex == o && i.Shapes[0] is RectShape).Shapes[0];
                    r.Rgb(rect.X + rect.W / 2, rect.Y + 1.5, out int cr, out int cg, out int cb);
                    Assert.True(cr >= 200 && cr - cg >= 20, "out " + o + " overlay (" + cr + "," + cg + "," + cb + ")");
                }
            });
        }

        [Fact]
        public void PrintingFollowsTheLayout()
        {
            var layout = Mixed();
            var doc = Pdf.Run(() => PdfDoc.FromBytes(D.Td.Bytes));
            try
            {
                Exception? err = null;
                int redactedPages = 0, reviewPages = 0;
                var t = new Thread(() =>
                {
                    try
                    {
                        var paper = new Size(794, 1123);
                        var red = new RedactedPaginator(Pdf.Worker, doc, OutputPlan.Build(D.Pages, D.Items, layout, false), RedactionStyle.Black, 72, paper);
                        redactedPages = red.PageCount;
                        for (int i = 0; i < red.PageCount; i++) red.GetPage(i);
                        red.Release();
                        var rev = new ReviewPaginator(Pdf.Worker, doc, OutputPlan.Build(D.Pages, D.Items, layout, true), 72, paper, "W");
                        reviewPages = rev.PageCount;
                        for (int i = 0; i < rev.PageCount; i++) rev.GetPage(i);
                        rev.Release();
                    }
                    catch (Exception e) { err = e; }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start(); t.Join();
                if (err != null) throw new Exception("printing failed", err);
                Assert.Equal(4, redactedPages);
                Assert.Equal(5, reviewPages);
            }
            finally { Pdf.Run(() => doc.Dispose()); }
        }
    }
}
