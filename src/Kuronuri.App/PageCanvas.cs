using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Model;
using Kuronuri.Core.Printing;
using Kuronuri.Core.Rendering;

namespace Kuronuri.App
{
    public enum Tool { Select, Rect, Freehand, Eraser }

    /// <summary>What a page canvas needs from the window.</summary>
    public interface IEditorHost
    {
        DocumentSession? Session { get; }
        ViewState View { get; }
        Tool Tool { get; }
        double BrushPt { get; }
        Reason? CurrentReason { get; }
        Guid? HighlightId { get; }
        void Execute(EditCommand cmd);
        void HandleWheel(PageCanvas source, MouseWheelEventArgs e);
    }

    /// <summary>
    /// One page pane. Draws the page bitmap (shared with the other pane through the cache) at device resolution and, when
    /// <see cref="Editable"/>, the redaction overlays and the tool interactions. Everything is drawn from the shared
    /// <see cref="ViewState"/>; there is no per-pane scroll state.
    /// </summary>
    public sealed class PageCanvas : FrameworkElement
    {
        private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x5c, 0x5f, 0x66)));
        private static readonly Brush SelBrush = Frozen(new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)));
        private static readonly Brush PreviewFill = Frozen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)));
        private static readonly Brush BadgeBack = Frozen(new SolidColorBrush(Color.FromArgb(220, 0, 84, 166)));
        private static readonly Brush BadgeWarn = Frozen(new SolidColorBrush(Color.FromArgb(230, 214, 98, 0)));
        private static readonly Brush HighlightBrush = Frozen(new SolidColorBrush(Color.FromRgb(255, 200, 0)));
        private static readonly Typeface BadgeFace = new Typeface(new FontFamily("Yu Gothic UI, Meiryo, MS Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private const double HandleDip = 8;
        private const long BasePlaceholderPixels = 4_000_000;

        public PageCanvas()
        {
            Focusable = false;
            ClipToBounds = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            _regionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(160), DispatcherPriority.Background, (s, e) => { _regionTimer!.Stop(); InvalidateVisual(); }, Dispatcher);
            _regionTimer.Stop();
        }

        public IEditorHost? Host { get; set; }
        public bool Editable { get; set; }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        // ------------------------------------------------------------------ geometry

        private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        private (double Ox, double Oy, double K) Origin()
        {
            var v = Host!.View;
            double k = v.DipPerPt;
            double ox = v.PageW < ActualWidth ? (ActualWidth - v.PageW) / 2 : -v.ScrollX;
            double oy = v.PageH < ActualHeight ? (ActualHeight - v.PageH) / 2 : -v.ScrollY;
            double s = DpiScale;
            return (Math.Round(ox * s) / s, Math.Round(oy * s) / s, k);
        }

        public Point2 ToPage(Point p)
        {
            var (ox, oy, k) = Origin();
            return new Point2((p.X - ox) / k, (p.Y - oy) / k);
        }

        private Point2 ToPageClamped(Point p)
        {
            var q = ToPage(p);
            var v = Host!.View;
            return new Point2(ShapeOps.Clamp(q.X, 0, v.PageWidthPt), ShapeOps.Clamp(q.Y, 0, v.PageHeightPt));
        }

        // ------------------------------------------------------------------ bitmaps

        private CancellationTokenSource? _cts;
        private RenderKey? _pending;
        private readonly DispatcherTimer _regionTimer;
        private RenderKey? _lastRegion;

        private void Request(PageRenderService svc, RenderKey key)
        {
            if (_pending == key) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _pending = key;
            var token = _cts.Token;
            svc.RenderAsync(key, token).ContinueWith(t =>
            {
                if (_pending == key) _pending = null;
                if (t.Status == TaskStatus.RanToCompletion) InvalidateVisual();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void DrawPage(DrawingContext dc, Rect pageRect)
        {
            var s = Host!.Session!;
            var svc = s.Renderer;
            dc.DrawRectangle(Brushes.White, null, pageRect);
            if (svc == null) return;
            var v = Host.View;
            double dps = v.DipPerPt * DpiScale;   // device px per pt
            Kuronuri.Core.Rendering.PageRenderer.PixelSize(v.PageWidthPt, v.PageHeightPt, 72 * dps, long.MaxValue, out int wpx, out int hpx, out _);
            int page = v.PageIndex;

            if ((long)wpx * hpx <= PageRenderService.FullRenderLimitPixels)
            {
                var key = RenderKey.Full(page, wpx, hpx);
                var bmp = svc.Pages.Get(key);
                if (bmp != null) { dc.DrawImage(bmp, pageRect); return; }
                var any = svc.Pages.GetAnyFull(page);
                if (any != null) dc.DrawImage(any, pageRect);
                Request(svc, key);
                return;
            }

            // Huge: a reduced full bitmap as the base, plus the visible region at full resolution once scrolling stops.
            double f = Math.Sqrt((double)BasePlaceholderPixels / ((double)wpx * hpx));
            var baseKey = RenderKey.Full(page, Math.Max(1, (int)(wpx * f)), Math.Max(1, (int)(hpx * f)));
            var baseBmp = svc.Pages.Get(baseKey) ?? svc.Pages.GetAnyFull(page);
            if (baseBmp != null) dc.DrawImage(baseBmp, pageRect);

            double scale = DpiScale;
            var vis = Rect.Intersect(new Rect(0, 0, ActualWidth, ActualHeight), pageRect);
            if (vis.IsEmpty) return;
            const int grid = 256;
            int x0 = (int)Math.Floor((vis.X - pageRect.X) * scale) - grid, y0 = (int)Math.Floor((vis.Y - pageRect.Y) * scale) - grid;
            int x1 = (int)Math.Ceiling((vis.Right - pageRect.X) * scale) + grid, y1 = (int)Math.Ceiling((vis.Bottom - pageRect.Y) * scale) + grid;
            x0 = Math.Max(0, x0 / grid * grid); y0 = Math.Max(0, y0 / grid * grid);
            x1 = Math.Min(wpx, (x1 + grid - 1) / grid * grid); y1 = Math.Min(hpx, (y1 + grid - 1) / grid * grid);
            var regionKey = new RenderKey(page, wpx, hpx, x0, y0, x1 - x0, y1 - y0);
            var reg = svc.Pages.Get(regionKey);
            if (reg != null)
            {
                dc.DrawImage(reg, new Rect(pageRect.X + x0 / scale, pageRect.Y + y0 / scale, (x1 - x0) / scale, (y1 - y0) / scale));
                return;
            }
            if (svc.Pages.Get(baseKey) == null) { Request(svc, baseKey); return; }
            if (_lastRegion != regionKey) { _lastRegion = regionKey; _regionTimer.Stop(); _regionTimer.Start(); return; }   // wait until scrolling stops
            if (!_regionTimer.IsEnabled) Request(svc, regionKey);
        }

        // ------------------------------------------------------------------ drawing

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var s = Host?.Session;
            if (s == null) return;
            var v = Host!.View;
            var (ox, oy, k) = Origin();
            var pageRect = new Rect(ox, oy, v.PageW, v.PageH);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), 3), new Rect(ox + 2, oy + 2, v.PageW, v.PageH));
            DrawPage(dc, pageRect);
            if (!Editable) return;

            int page = v.PageIndex;
            var onPage = s.Items.Items.Where(i => i.PageIndex == page).ToList();
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, ox, oy));

            // Overlays as they will be burned (screen geometry is never larger than the burn).
            dc.PushOpacity(v.Peek ? 0.25 : 1.0);
            var shapes = new List<Shape>();
            foreach (var item in onPage)
            {
                bool moving = _drag == DragKind.Move && s.IsSelected(item.Id);
                RedactionItem shown = moving ? ShapeOps.Translate(item, _moveDx, _moveDy) : item;
                if (_drag == DragKind.Resize && _resizeItem != null && item.Id == _resizeItem.Id) shown = item with { Shapes = new Shape[] { _resizeRect! } };
                shapes.AddRange(shown.Shapes);
            }
            DrawShapes(dc, shapes, s.Style, Brushes.Black, k);
            if (s.Style == RedactionStyle.WhiteWithBorder)
            {
                double b = Math.Max(0.75, 1.5 / k);
                DrawShapes(dc, shapes.Select(sh => Shrink(sh, b)).Where(sh => sh != null).Select(sh => sh!), s.Style, Brushes.White, k);
            }
            dc.Pop();

            // Selection, highlight, labels.
            var ordered = ShapeMath.InDisplayOrder(s.Items.Items);
            var numbers = new Dictionary<Guid, int>();
            for (int n = 0; n < ordered.Count; n++) numbers[ordered[n].Id] = n + 1;
            foreach (var item in onPage)
            {
                var shown = _drag == DragKind.Move && s.IsSelected(item.Id) ? ShapeOps.Translate(item, _moveDx, _moveDy) : item;
                var bb = ShapeOps.Box(shown);
                if (s.IsSelected(item.Id))
                {
                    var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 2 / k) { DashStyle = DashStyles.Dash };
                    dc.DrawRectangle(SelBrush, pen, new Rect(bb.L, bb.T, Math.Max(0, bb.W), Math.Max(0, bb.H)).Inflated(2 / k));
                }
                if (Host.HighlightId == item.Id)
                    dc.DrawRectangle(null, new Pen(HighlightBrush, 4 / k), new Rect(bb.L, bb.T, Math.Max(0, bb.W), Math.Max(0, bb.H)).Inflated(4 / k));
                DrawBadge(dc, numbers[item.Id].ToString(CultureInfo.InvariantCulture) + (item.Reason?.Code is { Length: > 0 } c ? " " + c : ""),
                    bb.L, bb.T, k, item.Reason == null);
            }
            var single = SingleRectSelection(s);
            if (single != null && _drag != DragKind.Move)
            {
                var r = _drag == DragKind.Resize ? _resizeRect! : (RectShape)single.Shapes[0];
                foreach (var h in Handles(r)) dc.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, 1 / k), HandleRect(h.P, k));
            }

            // Tool previews.
            if (_drag == DragKind.Rect && _rectStart != null && _rectEnd != null)
            {
                var r = ShapeOps.RectFromCorners(_rectStart.Value, _rectEnd.Value, v.PageWidthPt, v.PageHeightPt);
                dc.DrawRectangle(PreviewFill, new Pen(Brushes.Black, 1 / k) { DashStyle = DashStyles.Dash }, new Rect(r.X, r.Y, r.W, r.H));
            }
            if (_drag == DragKind.Stroke && _stroke.Count > 0)
            {
                var pen = new Pen(PreviewFill, Host.BrushPt) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(null, pen, ReviewPaginator.StrokeGeometry(new StrokeShape(_stroke, Host.BrushPt)));
            }
            dc.Pop();
        }

        private static void DrawShapes(DrawingContext dc, IEnumerable<Shape> shapes, RedactionStyle style, Brush brush, double k)
        {
            foreach (var sh in shapes)
            {
                if (sh is RectShape r) dc.DrawRectangle(brush, null, new Rect(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H)));
                else
                {
                    var st = (StrokeShape)sh;
                    var pen = new Pen(brush, st.WidthPt) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, pen, ReviewPaginator.StrokeGeometry(st));
                }
            }
        }

        private static Shape? Shrink(Shape s, double b)
        {
            if (s is RectShape r) return r.W > 2 * b && r.H > 2 * b ? new RectShape(r.X + b, r.Y + b, r.W - 2 * b, r.H - 2 * b) : null;
            var st = (StrokeShape)s;
            return st.WidthPt > 2 * b ? st with { WidthPt = st.WidthPt - 2 * b } : null;
        }

        private void DrawBadge(DrawingContext dc, string text, double x, double y, double k, bool warn)
        {
            var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, BadgeFace, 11 / k, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double pad = 2 / k;
            var rect = new Rect(x, y - ft.Height - 2 * pad, ft.Width + 2 * pad, ft.Height + 2 * pad);
            if (rect.Y < 0) rect.Y = y;
            dc.DrawRectangle(warn ? BadgeWarn : BadgeBack, null, rect);
            dc.DrawText(ft, new Point(rect.X + pad, rect.Y + pad));
        }

        // ------------------------------------------------------------------ handles

        private enum H { NW, N, NE, E, SE, S, SW, W }

        private static IEnumerable<(H H, Point P)> Handles(RectShape r)
        {
            double l = r.X, t = r.Y, rr = r.X + r.W, b = r.Y + r.H, cx = (l + rr) / 2, cy = (t + b) / 2;
            yield return (H.NW, new Point(l, t)); yield return (H.N, new Point(cx, t)); yield return (H.NE, new Point(rr, t));
            yield return (H.E, new Point(rr, cy)); yield return (H.SE, new Point(rr, b)); yield return (H.S, new Point(cx, b));
            yield return (H.SW, new Point(l, b)); yield return (H.W, new Point(l, cy));
        }

        private static Rect HandleRect(Point p, double k) { double s = HandleDip / k; return new Rect(p.X - s / 2, p.Y - s / 2, s, s); }

        private static RedactionItem? SingleRectSelection(DocumentSession s)
        {
            if (s.SelectedIds.Count != 1) return null;
            var item = s.Items.Find(s.SelectedIds.First());
            return item != null && item.Shapes.Count == 1 && item.Shapes[0] is RectShape ? item : null;
        }

        // ------------------------------------------------------------------ input

        private enum DragKind { None, Pan, Move, Resize, Rect, Stroke }
        private DragKind _drag;
        private Point _panLast;
        private Point2 _moveStart;
        private double _moveDx, _moveDy;
        private RedactionItem? _resizeItem;
        private RectShape? _resizeRect;
        private H _resizeHandle;
        private Point2? _rectStart, _rectEnd;
        private readonly List<Point2> _stroke = new List<Point2>();

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Host?.HandleWheel(this, e);
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            var s = Host?.Session;
            if (s == null || _drag != DragKind.None) return;
            Point pos = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Middle)
            {
                _drag = DragKind.Pan; _panLast = pos; CaptureMouse(); e.Handled = true; Cursor = Cursors.SizeAll;
                return;
            }
            if (e.ChangedButton != MouseButton.Left || !Editable) return;
            e.Handled = true;
            var v = Host!.View;
            var p = ToPage(pos);
            double tol = 3 / v.DipPerPt;
            switch (Host.Tool)
            {
                case Tool.Select:
                {
                    var single = SingleRectSelection(s);
                    if (single != null && single.PageIndex == v.PageIndex)
                    {
                        var r = (RectShape)single.Shapes[0];
                        foreach (var h in Handles(r))
                            if (HandleRect(h.P, v.DipPerPt).Inflated(2 / v.DipPerPt).Contains(new Point(p.X, p.Y)))
                            {
                                _drag = DragKind.Resize; _resizeItem = single; _resizeRect = r; _resizeHandle = h.H; CaptureMouse();
                                return;
                            }
                    }
                    var hit = HitItem(s, v.PageIndex, p, tol);
                    bool add = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
                    if (hit == null) { if (!add) s.Select(new Guid[0]); InvalidateVisual(); return; }
                    if (add)
                    {
                        var ids = s.SelectedIds.ToList();
                        if (!ids.Remove(hit.Id)) ids.Add(hit.Id);
                        s.Select(ids);
                        InvalidateVisual();
                        return;
                    }
                    if (!s.IsSelected(hit.Id)) s.Select(new[] { hit.Id });
                    _drag = DragKind.Move; _moveStart = p; _moveDx = _moveDy = 0; CaptureMouse();
                    break;
                }
                case Tool.Rect:
                    _drag = DragKind.Rect; _rectStart = _rectEnd = ToPageClamped(pos); CaptureMouse();
                    break;
                case Tool.Freehand:
                    _drag = DragKind.Stroke; _stroke.Clear(); _stroke.Add(ToPageClamped(pos)); CaptureMouse();
                    break;
                case Tool.Eraser:
                {
                    foreach (var item in s.Items.Items.Where(i => i.PageIndex == v.PageIndex).Reverse())
                    {
                        int idx = ShapeOps.HitShape(item, p, tol);
                        if (idx >= 0) { Host.Execute(EditOps.EraseShape(item, idx)); break; }
                    }
                    break;
                }
            }
            InvalidateVisual();
        }

        private static RedactionItem? HitItem(DocumentSession s, int page, Point2 p, double tol)
        {
            // Last drawn wins: walk backwards; selected items get priority so a selection can be dragged even under others.
            var onPage = s.Items.Items.Where(i => i.PageIndex == page).ToList();
            foreach (var item in onPage.AsEnumerable().Reverse()) if (s.IsSelected(item.Id) && ShapeOps.HitShape(item, p, tol) >= 0) return item;
            foreach (var item in onPage.AsEnumerable().Reverse()) if (ShapeOps.HitShape(item, p, tol) >= 0) return item;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var s = Host?.Session;
            if (s == null) return;
            Point pos = e.GetPosition(this);
            var v = Host!.View;
            switch (_drag)
            {
                case DragKind.Pan:
                    Host.View.ScrollBy(_panLast.X - pos.X, _panLast.Y - pos.Y);
                    _panLast = pos;
                    return;
                case DragKind.Move:
                {
                    var p = ToPage(pos);
                    double dx = p.X - _moveStart.X, dy = p.Y - _moveStart.Y;
                    var sel = s.SelectedItems.Where(i => i.PageIndex == v.PageIndex).ToList();
                    if (sel.Count > 0)
                    {
                        var union = new RedactionItem(Guid.Empty, v.PageIndex, sel.SelectMany(i => i.Shapes).ToList(), null);
                        (dx, dy) = ShapeOps.ClampDelta(union, dx, dy, v.PageWidthPt, v.PageHeightPt);
                    }
                    _moveDx = dx; _moveDy = dy;
                    break;
                }
                case DragKind.Resize:
                    _resizeRect = Resize((RectShape)_resizeItem!.Shapes[0], _resizeHandle, ToPageClamped(pos));
                    break;
                case DragKind.Rect:
                    _rectEnd = ToPageClamped(pos);
                    break;
                case DragKind.Stroke:
                {
                    var p = ToPageClamped(pos);
                    var last = _stroke[_stroke.Count - 1];
                    if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) >= 0.05) _stroke.Add(p);
                    break;
                }
                default:
                    UpdateCursor(s, pos);
                    return;
            }
            InvalidateVisual();
        }

        private void UpdateCursor(DocumentSession s, Point pos)
        {
            if (!Editable) { Cursor = null; return; }
            var tool = Host!.Tool;
            if (tool == Tool.Rect || tool == Tool.Freehand) { Cursor = Cursors.Cross; return; }
            if (tool == Tool.Eraser) { Cursor = Cursors.Hand; return; }
            var single = SingleRectSelection(s);
            var p = ToPage(pos);
            var v = Host.View;
            if (single != null && single.PageIndex == v.PageIndex)
                foreach (var h in Handles((RectShape)single.Shapes[0]))
                    if (HandleRect(h.P, v.DipPerPt).Inflated(2 / v.DipPerPt).Contains(new Point(p.X, p.Y)))
                    {
                        Cursor = h.H == H.N || h.H == H.S ? Cursors.SizeNS : h.H == H.E || h.H == H.W ? Cursors.SizeWE :
                            h.H == H.NW || h.H == H.SE ? Cursors.SizeNWSE : Cursors.SizeNESW;
                        return;
                    }
            Cursor = HitItem(s, v.PageIndex, p, 3 / v.DipPerPt) != null ? Cursors.SizeAll : null;
        }

        private static RectShape Resize(RectShape r, H h, Point2 p)
        {
            double l = r.X, t = r.Y, rr = r.X + r.W, b = r.Y + r.H;
            if (h == H.NW || h == H.W || h == H.SW) l = p.X;
            if (h == H.NE || h == H.E || h == H.SE) rr = p.X;
            if (h == H.NW || h == H.N || h == H.NE) t = p.Y;
            if (h == H.SW || h == H.S || h == H.SE) b = p.Y;
            return new RectShape(Math.Min(l, rr), Math.Min(t, b), Math.Abs(rr - l), Math.Abs(b - t));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            var s = Host?.Session;
            var kind = _drag;
            if (kind == DragKind.None) return;
            if (kind == DragKind.Pan && e.ChangedButton != MouseButton.Middle) return;
            if (kind != DragKind.Pan && e.ChangedButton != MouseButton.Left) return;
            _drag = DragKind.None;
            ReleaseMouseCapture();
            Cursor = null;
            if (s == null) return;
            var v = Host!.View;
            switch (kind)
            {
                case DragKind.Move:
                {
                    var cmd = EditOps.MoveMany(s.SelectedItems.Where(i => i.PageIndex == v.PageIndex).ToList(), _moveDx, _moveDy);
                    _moveDx = _moveDy = 0;
                    if (cmd != null) Host.Execute(cmd);
                    break;
                }
                case DragKind.Resize:
                {
                    var r = _resizeRect!;
                    if (r.W >= 3 && r.H >= 3)
                    {
                        var cmd = EditOps.ResizeRect(_resizeItem!, r);
                        if (cmd != null) Host.Execute(cmd);
                    }
                    _resizeItem = null; _resizeRect = null;
                    break;
                }
                case DragKind.Rect:
                {
                    var r = ShapeOps.RectFromCorners(_rectStart!.Value, _rectEnd!.Value, v.PageWidthPt, v.PageHeightPt);
                    _rectStart = _rectEnd = null;
                    if (r.W >= 3 && r.H >= 3)
                    {
                        var cmd = EditOps.AddRect(v.PageIndex, r, Host.CurrentReason);
                        Host.Execute(cmd);
                        s.Select(new[] { cmd.Item.Id });
                    }
                    break;
                }
                case DragKind.Stroke:
                {
                    var stroke = ShapeOps.FinishStroke(_stroke, Host.BrushPt);
                    _stroke.Clear();
                    Host.Execute(s.Grouper.AddStroke(s.Items, v.PageIndex, stroke, Host.CurrentReason));
                    break;
                }
            }
            InvalidateVisual();
        }

        /// <summary>Abandons a drag in progress (Esc, focus loss).</summary>
        public void CancelDrag()
        {
            if (_drag == DragKind.None) return;
            _drag = DragKind.None;
            _stroke.Clear(); _rectStart = _rectEnd = null; _resizeItem = null; _resizeRect = null; _moveDx = _moveDy = 0;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        public bool IsDragging => _drag != DragKind.None;

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_drag != DragKind.None && Mouse.LeftButton == MouseButtonState.Released && Mouse.MiddleButton == MouseButtonState.Released) CancelDrag();
        }
    }

    internal static class RectExt
    {
        public static Rect Inflated(this Rect r, double d) { r.Inflate(d, d); return r; }
    }
}
