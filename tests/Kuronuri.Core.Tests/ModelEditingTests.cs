using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Model;
using Xunit;

namespace Kuronuri.Core.Tests
{
    public class ProjectJsonTests
    {
        [Fact]
        public void RoundTripIsExact()
        {
            var tricky = new RedactionProject(new OriginalInfo("ab", 1234567890123, "名前 \"引用\" \\ /.pdf"),
                new[] { new PageInfo(595.275573730469, 841.889770507813, 90) },
                new[]
                {
                    new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(0.1 + 0.2, 1e-7, 123.456789012345678, 1.0 / 3) }, new Reason("2号", "法人等情報", "自由記述\n改行")),
                    new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new StrokeShape(new[] { new Point2(1.5, 2.25), new Point2(Math.PI, Math.E) }, 14.173228346456693) }, null),
                }, RedactionStyle.WhiteWithBorder);
            byte[] j1 = ProjectJson.Serialize(tricky, "1.0.0");
            var back = ProjectJson.Deserialize(j1);
            var r0 = (RectShape)back.Items[0].Shapes[0]; var s1 = (StrokeShape)back.Items[1].Shapes[0];
            Assert.Equal(0.1 + 0.2, r0.X);
            Assert.Equal(1e-7, r0.Y);
            Assert.Equal(1.0 / 3, r0.H);
            Assert.Equal(Math.PI, s1.Points[1].X);
            Assert.Equal(14.173228346456693, s1.WidthPt);
            Assert.Equal(595.275573730469, back.Pages[0].Width);
            Assert.Equal("自由記述\n改行", back.Items[0].Reason!.FreeText);
            Assert.Equal(tricky.Original.FileName, back.Original.FileName);
            Assert.Null(back.Items[1].Reason);
            Assert.Equal(RedactionStyle.WhiteWithBorder, back.Style);
            Assert.Equal(tricky.Items[0].Id, back.Items[0].Id);
            Assert.Equal(j1, ProjectJson.Serialize(back, "1.0.0"));
            string text = Encoding.UTF8.GetString(j1);
            Assert.StartsWith("{\"app\":\"kuronuri\",\"schema\":1,", text);
            Assert.Contains("法人等情報", text);
        }

        [Theory]
        [InlineData("{\"app\":\"other\",\"schema\":1,\"appVersion\":\"\",\"original\":{\"sha256\":\"\",\"length\":0,\"fileName\":\"\"},\"pages\":[],\"style\":\"black\",\"items\":[]}")]
        [InlineData("{\"app\":\"kuronuri\",\"schema\":2,\"appVersion\":\"\",\"original\":{\"sha256\":\"\",\"length\":0,\"fileName\":\"\"},\"pages\":[],\"style\":\"black\",\"items\":[]}")]
        [InlineData("{\"app\":\"kuronuri\",\"schema\":1,\"appVersion\":\"\",\"original\":{\"sha256\":\"\",\"length\":0,\"fileName\":\"\"},\"pages\":[],\"style\":\"black\",\"items\":[{\"id\":\"73c1cf7a-ce1c-4888-bde7-35499e9fc79a\",\"page\":0,\"reason\":null,\"shapes\":[{\"t\":\"rect\",\"x\":1,\"y\":2,\"w\":3,\"h\":4}]}]}")]
        [InlineData("{\"app\":\"kuronuri\",\"schema\":1,\"appVersion\":\"\",\"original\":{\"sha256\":\"\",\"length\":0,\"fileName\":\"\"},\"pages\":[{\"w\":1,\"h\":1,\"rot\":0}],\"style\":\"black\",\"items\":[{\"id\":\"x\",\"page\":0,\"reason\":null,\"shapes\":[{\"t\":\"circle\"}]}]}")]
        public void RejectsForeignOrInvalidData(string json)
        {
            Assert.ThrowsAny<Exception>(() => ProjectJson.Deserialize(Encoding.UTF8.GetBytes(json)));
        }

        [Fact]
        public void AcceptsTheDocumentedSample()
        {
            string json = "{\"app\":\"kuronuri\",\"schema\":1,\"appVersion\":\"1.0.0\",\"original\":{\"sha256\":\"x\",\"length\":123456,\"fileName\":\"a.pdf\"}," +
                "\"pages\":[{\"w\":595,\"h\":842,\"rot\":0}],\"style\":\"black\",\"items\":[" +
                "{\"id\":\"73c1cf7a-ce1c-4888-bde7-35499e9fc79a\",\"page\":0,\"reason\":{\"code\":\"1号\",\"label\":\"個人情報\",\"text\":\"\"},\"shapes\":[{\"t\":\"rect\",\"x\":100,\"y\":200,\"w\":150,\"h\":20}]}," +
                "{\"id\":\"5d6b35f7-ee1b-4070-844b-e49bf2c2c99c\",\"page\":0,\"reason\":null,\"shapes\":[{\"t\":\"stroke\",\"sw\":14.173228346456693,\"pts\":[10,20,15.5,22]}]}]}";
            var p = ProjectJson.Deserialize(Encoding.UTF8.GetBytes(json));
            Assert.Equal(2, p.Items.Count);
            Assert.Equal(new RectShape(100, 200, 150, 20), p.Items[0].Shapes[0]);
            Assert.Equal(2, ((StrokeShape)p.Items[1].Shapes[0]).Points.Count);
        }
    }

    public class ShapeTests
    {
        [Fact]
        public void BoundsIncludeHalfStrokeWidth()
        {
            var s = new StrokeShape(new[] { new Point2(10, 20), new Point2(30, 5) }, 4);
            Assert.Equal((8.0, 3.0, 32.0, 22.0), ShapeMath.Bounds(s));
        }

        [Fact]
        public void DisplayOrderIsPageTopLeft()
        {
            var a = Item(1, new RectShape(10, 10, 5, 5));
            var b = Item(0, new RectShape(50, 100, 5, 5));
            var c = Item(0, new RectShape(10, 100, 5, 5));
            var d = Item(0, new RectShape(90, 20, 5, 5));
            Assert.Equal(new[] { d, c, b, a }, ShapeMath.InDisplayOrder(new[] { a, b, c, d }));
        }

        [Fact]
        public void SimplifyKeepsEndpointsAndRemovesCollinearPoints()
        {
            var raw = Enumerable.Range(0, 101).Select(i => new Point2(i, 0.1 * Math.Sin(i))).ToList();
            var s = ShapeOps.Simplify(raw, 0.25);
            Assert.Equal(2, s.Count);
            Assert.Equal(raw[0], s[0]); Assert.Equal(raw[100], s[1]);

            var corner = new List<Point2> { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0), new Point2(10, 5), new Point2(10, 10) };
            Assert.Equal(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) }, ShapeOps.Simplify(corner, 0.25));
        }

        [Fact]
        public void SimplifiedStrokeStillCoversTheRawPath()
        {
            var rnd = new Random(7);
            var raw = new List<Point2>();
            double x = 100, y = 100;
            for (int i = 0; i < 400; i++) { x += rnd.NextDouble() * 2 - 0.6; y += rnd.NextDouble() * 2 - 1; raw.Add(new Point2(x, y)); }
            double width = ShapeOps.MmToPt(4);
            var s = ShapeOps.FinishStroke(raw, width);
            Assert.True(s.Points.Count < raw.Count, "kept " + s.Points.Count);
            Assert.Equal(width + 0.5, s.WidthPt, 9);
            // Every point of the raw stroke's disc boundary must be inside the simplified stroke.
            foreach (var p in raw)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8;
                    var q = new Point2(p.X + Math.Cos(a) * width / 2, p.Y + Math.Sin(a) * width / 2);
                    Assert.True(ShapeOps.Hit(s, q, 1e-6));
                }
        }

        [Fact]
        public void SinglePointStrokeIsKeptAsDot()
        {
            var s = ShapeOps.FinishStroke(new[] { new Point2(5, 5), new Point2(5, 5) }, 10);
            Assert.Single(s.Points);
            Assert.True(ShapeOps.Hit(s, new Point2(9, 5), 0));
            Assert.False(ShapeOps.Hit(s, new Point2(11, 5), 0));
        }

        [Fact]
        public void MmToPt() => Assert.Equal(28.346456692913385, ShapeOps.MmToPt(10), 12);

        internal static RedactionItem Item(int page, params Shape[] shapes) => new RedactionItem(Guid.NewGuid(), page, shapes, null);
    }

    public class UndoTests
    {
        private static RedactionItem R(int page, double x, double y, Reason? reason = null) =>
            new RedactionItem(Guid.NewGuid(), page, new Shape[] { new RectShape(x, y, 50, 10) }, reason);

        [Fact]
        public void AddRemoveReplaceUndoRedo()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var a = R(0, 10, 10); var b = R(0, 10, 50);
            u.Execute(new AddItemCommand(a));
            u.Execute(new AddItemCommand(b));
            var moved = ShapeOps.Translate(a, 5, 5);
            u.Execute(new ReplaceItemCommand(a, moved));
            u.Execute(new RemoveItemCommand(b));
            Assert.Equal(new[] { moved }, list.Items);

            var undone = u.Undo();
            Assert.Equal(new[] { b }, undone!.ItemsBefore);
            Assert.Equal(new[] { moved, b }, list.Items);   // re-inserted at its original index
            u.Undo();
            Assert.Equal(new[] { a, b }, list.Items);
            u.Undo(); u.Undo();
            Assert.Empty(list.Items);
            Assert.Null(u.Undo());
            u.Redo(); u.Redo(); u.Redo(); u.Redo();
            Assert.Equal(new[] { moved }, list.Items);
            Assert.Null(u.Redo());
        }

        [Fact]
        public void NewCommandClearsRedo()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            u.Execute(new AddItemCommand(R(0, 1, 1)));
            u.Undo();
            Assert.True(u.CanRedo);
            u.Execute(new AddItemCommand(R(0, 2, 2)));
            Assert.False(u.CanRedo);
        }

        [Fact]
        public void ModifiedFollowsSavedPosition()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            Assert.False(u.IsModified);
            u.Execute(new AddItemCommand(R(0, 1, 1)));
            Assert.True(u.IsModified);
            u.MarkSaved();
            Assert.False(u.IsModified);
            u.Execute(new AddItemCommand(R(0, 2, 2)));
            Assert.True(u.IsModified);
            u.Undo();
            Assert.False(u.IsModified);
            u.Undo();
            Assert.True(u.IsModified);
            u.Redo();
            Assert.False(u.IsModified);
            u.Undo();
            u.Execute(new AddItemCommand(R(0, 3, 3)));   // a different branch is never "saved"
            Assert.True(u.IsModified);
            u.Undo();
            Assert.True(u.IsModified);
        }

        [Fact]
        public void HistoryIsCappedAndDropsTheOldest()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            for (int i = 0; i < UndoStack.MaxHistory + 20; i++) u.Execute(new AddItemCommand(R(0, i, 0)));
            Assert.Equal(UndoStack.MaxHistory, u.UndoCount);
            while (u.CanUndo) u.Undo();
            Assert.Equal(20, list.Count);
            Assert.True(u.IsModified);   // the initial (saved) state is no longer reachable
        }

        [Fact]
        public void ClearAsModifiedMarksUnsaved()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            u.ClearAsModified();
            Assert.True(u.IsModified);
            u.Clear();
            Assert.False(u.IsModified);
        }

        [Fact]
        public void CompositeRollsBackWhenAPartFails()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var a = R(0, 1, 1);
            u.Execute(new AddItemCommand(a));
            var bad = new CompositeCommand(new EditCommand[] { new RemoveItemCommand(a), new RemoveItemCommand(a) });
            Assert.Throws<InvalidOperationException>(() => u.Execute(bad));
            Assert.Equal(new[] { a }, list.Items);
            Assert.Equal(1, u.UndoCount);
        }

        [Fact]
        public void ReasonChangeAndMoveAreSingleCommands()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var a = R(0, 1, 1); var b = R(0, 1, 30);
            u.Execute(new AddItemCommand(a)); u.Execute(new AddItemCommand(b));
            var reason = new Reason("1号", "個人情報", "");
            u.Execute(EditOps.SetReason(list.Items.ToList(), reason)!);
            Assert.All(list.Items, i => Assert.Equal(reason, i.Reason));
            u.Execute(EditOps.MoveMany(list.Items.ToList(), 3, 4)!);
            Assert.Equal(4, ((RectShape)list.Items[0].Shapes[0]).X);
            u.Undo();
            Assert.Equal(1, ((RectShape)list.Items[0].Shapes[0]).X);
            u.Undo();
            Assert.All(list.Items, i => Assert.Null(i.Reason));
            Assert.Null(EditOps.SetReason(list.Items.ToList(), null));
            Assert.Null(EditOps.Move(a, 0, 0));
        }

        [Fact]
        public void EraserRemovesShapeThenItem()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var item = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(0, 0, 10, 10), new RectShape(20, 0, 10, 10) }, null);
            u.Execute(new AddItemCommand(item));
            u.Execute(EditOps.EraseShape(list.Items[0], 0));
            Assert.Single(list.Items[0].Shapes);
            Assert.Equal(item.Id, list.Items[0].Id);
            u.Execute(EditOps.EraseShape(list.Items[0], 0));
            Assert.Empty(list.Items);
            u.Undo(); u.Undo();
            Assert.Equal(2, list.Items[0].Shapes.Count);
        }
    }

    public class GroupingTests
    {
        private static StrokeShape S(double x0, double y0, double x1, double y1, double w = 10) =>
            new StrokeShape(new[] { new Point2(x0, y0), new Point2(x1, y1) }, w);

        [Fact]
        public void NearbyStrokesJoinTheEditingItem()
        {
            var list = new ItemList(); var u = new UndoStack(list); var g = new FreehandGrouper();
            var reason = new Reason("1号", "個人情報", "");
            u.Execute(g.AddStroke(list, 0, S(100, 100, 200, 100), reason));
            u.Execute(g.AddStroke(list, 0, S(100, 115, 200, 115), reason));   // 15pt apart, half widths 5+5, gap 5 <= 6
            Assert.Single(list.Items);
            Assert.Equal(2, list.Items[0].Shapes.Count);
            Assert.Equal(reason, list.Items[0].Reason);

            u.Execute(g.AddStroke(list, 0, S(100, 300, 200, 300), reason));   // far away -> new item
            Assert.Equal(2, list.Count);
            Assert.Equal(list.Items[1].Id, g.EditingId);

            u.Execute(g.AddStroke(list, 0, S(100, 125, 200, 125), reason));   // near the first item but it is no longer editing
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void GapJustOverSixPointsStartsANewItem()
        {
            var list = new ItemList(); var u = new UndoStack(list); var g = new FreehandGrouper();
            u.Execute(g.AddStroke(list, 0, S(0, 0, 100, 0, 2), null));   // bounds bottom = 1
            u.Execute(g.AddStroke(list, 0, S(0, 8.01, 100, 8.01, 2), null));   // top = 7.01, gap 6.01
            Assert.Equal(2, list.Count);
            u.Execute(g.AddStroke(list, 0, S(0, 14.9, 100, 14.9, 2), null));   // top 13.9 vs bottom 9.01: gap 4.89
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void CloseAndPageChangeStartNewItems()
        {
            var list = new ItemList(); var u = new UndoStack(list); var g = new FreehandGrouper();
            u.Execute(g.AddStroke(list, 0, S(0, 0, 100, 0), null));
            g.Close();
            Assert.Null(g.EditingId);
            u.Execute(g.AddStroke(list, 0, S(0, 1, 100, 1), null));
            Assert.Equal(2, list.Count);
            u.Execute(g.AddStroke(list, 1, S(0, 1, 100, 1), null));   // same place, other page
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void UndoOfJoinedStrokeRestoresPreviousShapes()
        {
            var list = new ItemList(); var u = new UndoStack(list); var g = new FreehandGrouper();
            u.Execute(g.AddStroke(list, 0, S(0, 0, 100, 0), null));
            u.Execute(g.AddStroke(list, 0, S(0, 5, 100, 5), null));
            u.Undo();
            Assert.Single(list.Items[0].Shapes);
            u.Undo();
            Assert.Empty(list.Items);
        }

        [Fact]
        public void MergeKeepsFirstIdAndFirstReason()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var reason = new Reason("2号", "法人等情報", "");
            var top = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(10, 10, 5, 5) }, null);
            var mid = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(10, 50, 5, 5), new RectShape(30, 50, 5, 5) }, reason);
            var low = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(10, 90, 5, 5) }, new Reason("1号", "個人情報", ""));
            foreach (var i in new[] { low, mid, top }) u.Execute(new AddItemCommand(i));
            var cmd = EditOps.Merge(new[] { low, top, mid });
            u.Execute(cmd);
            Assert.Single(list.Items);
            var m = list.Items[0];
            Assert.Equal(top.Id, m.Id);
            Assert.Equal(reason, m.Reason);
            Assert.Equal(4, m.Shapes.Count);
            Assert.Equal(new RectShape(10, 10, 5, 5), m.Shapes[0]);
            Assert.Equal(new[] { m.Id }, cmd.ItemsAfter.Select(i => i.Id));
            u.Undo();
            Assert.Equal(new[] { low.Id, mid.Id, top.Id }, list.Items.Select(i => i.Id));
            Assert.Equal(3, cmd.ItemsBefore.Count());
        }

        [Fact]
        public void MergeAcrossPagesIsRefused()
        {
            var a = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(1, 1, 1, 1) }, null);
            var b = new RedactionItem(Guid.NewGuid(), 1, new Shape[] { new RectShape(1, 1, 1, 1) }, null);
            Assert.False(EditOps.CanMerge(new[] { a, b }));
            Assert.False(EditOps.CanMerge(new[] { a }));
            Assert.Throws<InvalidOperationException>(() => EditOps.Merge(new[] { a, b }));
        }

        [Fact]
        public void SplitMakesOneItemPerShape()
        {
            var list = new ItemList(); var u = new UndoStack(list);
            var reason = new Reason("1号", "個人情報", "x");
            var other = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(0, 500, 5, 5) }, null);
            var item = new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(0, 0, 5, 5), new RectShape(0, 20, 5, 5), new RectShape(0, 40, 5, 5) }, reason);
            u.Execute(new AddItemCommand(item));
            u.Execute(new AddItemCommand(other));
            Assert.False(EditOps.CanSplit(other));
            var cmd = EditOps.Split(list, item);
            u.Execute(cmd);
            Assert.Equal(4, list.Count);
            Assert.Equal(item.Id, list.Items[0].Id);
            Assert.Equal(other.Id, list.Items[3].Id);
            Assert.All(list.Items.Take(3), i => { Assert.Single(i.Shapes); Assert.Equal(reason, i.Reason); });
            Assert.Equal(3, cmd.ItemsAfter.Count());
            u.Undo();
            Assert.Equal(2, list.Count);
            Assert.Equal(3, list.Items[0].Shapes.Count);
        }
    }
}
