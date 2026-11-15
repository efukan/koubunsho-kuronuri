using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Model;
using Kuronuri.Core.Rendering;

namespace Kuronuri.App
{
    public static class Cmd
    {
        private static RoutedUICommand C(string text, string name, params InputGesture[] g) =>
            new RoutedUICommand(text, name, typeof(Cmd), new InputGestureCollection(g));

        public static readonly RoutedUICommand Open = C("開く", "Open", new KeyGesture(Key.O, ModifierKeys.Control));
        public static readonly RoutedUICommand SaveReview = C("確認用PDFに保存", "SaveReview", new KeyGesture(Key.S, ModifierKeys.Control));
        public static readonly RoutedUICommand SaveAs = C("名前を付けて確認用PDFに保存", "SaveAs", new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand Export = C("開示用PDFを作成", "Export", new KeyGesture(Key.E, ModifierKeys.Control));
        public static readonly RoutedUICommand PrintRedacted = C("黒塗りした状態で印刷", "PrintRedacted", new KeyGesture(Key.P, ModifierKeys.Control));
        public static readonly RoutedUICommand PrintReview = C("確認用として印刷", "PrintReview");
        public static readonly RoutedUICommand Undo = C("元に戻す", "Undo", new KeyGesture(Key.Z, ModifierKeys.Control));
        public static readonly RoutedUICommand Redo = C("やり直し", "Redo", new KeyGesture(Key.Y, ModifierKeys.Control));
        public static readonly RoutedUICommand Merge = C("結合", "Merge", new KeyGesture(Key.G, ModifierKeys.Control));
        public static readonly RoutedUICommand Split = C("分割", "Split", new KeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand Delete = C("削除", "Delete");
        public static readonly RoutedUICommand ZoomIn = C("拡大", "ZoomIn", new KeyGesture(Key.OemPlus, ModifierKeys.Control));
        public static readonly RoutedUICommand ZoomOut = C("縮小", "ZoomOut", new KeyGesture(Key.OemMinus, ModifierKeys.Control));
        public static readonly RoutedUICommand Zoom100 = C("100%", "Zoom100", new KeyGesture(Key.D1, ModifierKeys.Control));
        public static readonly RoutedUICommand FitWidth = C("幅に合わせる", "FitWidth", new KeyGesture(Key.D2, ModifierKeys.Control));
        public static readonly RoutedUICommand FitPage = C("全体", "FitPage", new KeyGesture(Key.D0, ModifierKeys.Control));
        public static readonly RoutedUICommand TogglePanes = C("2画面 / 1画面", "TogglePanes", new KeyGesture(Key.F6));
        public static readonly RoutedUICommand Settings = C("設定", "Settings");
        public static readonly RoutedUICommand NextUnset = C("次の未設定", "NextUnset", new KeyGesture(Key.F8));
        public static readonly RoutedUICommand PrevUnset = C("前の未設定", "PrevUnset", new KeyGesture(Key.F8, ModifierKeys.Shift));
        public static readonly RoutedUICommand Find = C("検索", "Find", new KeyGesture(Key.F, ModifierKeys.Control));
        public static readonly RoutedUICommand Help = C("使い方", "Help", new KeyGesture(Key.F1));
        public static readonly RoutedUICommand RotateRight = C("右に90°回転", "RotateRight", new KeyGesture(Key.R, ModifierKeys.Control));
        public static readonly RoutedUICommand RotateLeft = C("左に90°回転", "RotateLeft", new KeyGesture(Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand Rotate180 = C("180°回転", "Rotate180");
        public static readonly RoutedUICommand ToggleExclude = C("開示用PDFから除く／含める", "ToggleExclude");
        public static readonly RoutedUICommand MovePageUp = C("前へ移動", "MovePageUp");
        public static readonly RoutedUICommand MovePageDown = C("後ろへ移動", "MovePageDown");
    }

    /// <summary>One search result row.</summary>
    public sealed class SearchHitVM : INotifyPropertyChanged
    {
        private bool _selected;
        private bool _covered;

        public SearchHitVM(Kuronuri.Core.Text.TextHit hit, bool covered, int viewPage) { Hit = hit; _covered = covered; _selected = !covered; Page = viewPage; }

        public Kuronuri.Core.Text.TextHit Hit { get; }
        /// <summary>Page number as shown (view order, 1-based); updated when the page order changes.</summary>
        public int Page { get; private set; }

        public void SetViewPage(int page) { if (Page == page) return; Page = page; Raise(nameof(Page)); }
        public string Preview => Hit.Preview;

        public bool Selected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; Raise(nameof(Selected)); }
        }

        public bool Covered
        {
            get => _covered;
            set { if (_covered == value) return; _covered = value; Raise(nameof(Covered)); Raise(nameof(State)); }
        }

        public string State => _covered ? "済" : "";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>One row of the item list.</summary>
    public sealed class ItemRow
    {
        /// <param name="viewPage">Page number as shown (view order, 1-based).</param>
        public ItemRow(RedactionItem item, int number, int viewPage)
        {
            Item = item; Number = number; Page = viewPage;
        }

        public RedactionItem Item { get; }
        public Guid Id => Item.Id;
        public int Number { get; }
        public int Page { get; }
        public bool IsUnset => Item.Reason == null;
        public string ReasonText => Describe(Item.Reason);
        public string Kind => Item.Shapes.Count > 1 ? "複数(" + Item.Shapes.Count + ")" : Item.Shapes[0] is RectShape ? "矩形" : "手書き";
        public string KindGlyph => Item.Shapes.Count > 1 ? "" : Item.Shapes[0] is RectShape ? "" : "";

        public static string Describe(Reason? r)
        {
            if (r == null) return "（未設定）";
            string head = (r.Code + " " + r.Label).Trim();
            if (r.FreeText.Length == 0) return head;
            return head.Length == 0 ? r.FreeText : head + "：" + r.FreeText;
        }
    }

    /// <summary>A thumbnail; the bitmap is only requested when a container shows it (virtualized list).</summary>
    public sealed class ThumbVM : INotifyPropertyChanged
    {
        public const double WidthDip = 120;
        private readonly Func<PageRenderService?> _svc;
        private readonly double _scale;
        private bool _requested;

        /// <param name="index">View position.</param><param name="basePage">Original page shown there.</param><param name="rotation">User rotation (ARCHITECTURE §2.4).</param>
        public ThumbVM(int index, int basePage, int rotation, bool excluded, double baseW, double baseH, double dpiScale, Func<PageRenderService?> svc)
        {
            Index = index; BasePage = basePage; Rotation = rotation; Excluded = excluded; _baseW = baseW; _baseH = baseH; _svc = svc; _scale = dpiScale;
            var (wPt, hPt) = Kuronuri.Core.Geometry.CoordMap.ViewSize(rotation, baseW, baseH);
            HeightDip = Math.Round(WidthDip * hPt / wPt);
            int wpx = Math.Max(1, (int)Math.Round(WidthDip * dpiScale)), hpx = Math.Max(1, (int)Math.Round(HeightDip * dpiScale));
            Key = RenderKey.Full(basePage, wpx, hpx, rotation);
        }

        public int Index { get; }
        public string Label => (Index + 1).ToString(CultureInfo.InvariantCulture);
        public double HeightDip { get; }
        public RenderKey Key { get; }
        public int BasePage { get; }
        public int Rotation { get; }
        /// <summary>Not included in the disclosure PDF (ARCHITECTURE §4.4).</summary>
        public bool Excluded { get; }
        private readonly double _baseW, _baseH;

        // ---- redactions on this page (badge + black overlay)

        public int Count { get; private set; }
        public int UnsetCount { get; private set; }
        public bool HasItems => Count > 0;
        public bool HasUnset => UnsetCount > 0;
        public string BadgeToolTip => "箇所 " + Count + (UnsetCount > 0 ? "（理由未設定 " + UnsetCount + "）" : "");
        public ImageSource? Overlay { get; private set; }

        /// <summary>Updates the badge and the overlay; raises change notifications only when something changed.</summary>
        public void SetItems(System.Collections.Generic.IReadOnlyList<RedactionItem> onPage)
        {
            int count = onPage.Count, unset = 0;
            foreach (var i in onPage) if (i.Reason == null) unset++;
            ImageSource? overlay = count == 0 ? null : BuildOverlay(onPage.Select(i => Kuronuri.Core.Geometry.CoordMap.ToView(Rotation, _baseW, _baseH, i)).ToList(),
                Kuronuri.Core.Geometry.CoordMap.ViewSize(Rotation, _baseW, _baseH));
            bool changed = count != Count || unset != UnsetCount || count > 0 || Overlay != null;
            Count = count; UnsetCount = unset; Overlay = overlay;
            if (!changed) return;
            foreach (var n in new[] { nameof(Count), nameof(UnsetCount), nameof(HasItems), nameof(HasUnset), nameof(BadgeToolTip), nameof(Overlay) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private static ImageSource BuildOverlay(System.Collections.Generic.IReadOnlyList<RedactionItem> items, (double W, double H) size)
        {
            var g = new DrawingGroup();
            double w = size.W, h = size.H;
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
            foreach (var item in items)
                foreach (var s in item.Shapes)
                {
                    if (s is RectShape r) g.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H)))));
                    else
                    {
                        var st = (StrokeShape)s;
                        var pen = new Pen(Brushes.Black, st.WidthPt) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                        g.Children.Add(new GeometryDrawing(null, pen, Kuronuri.Core.Printing.ReviewPaginator.StrokeGeometry(st)));
                    }
                }
            g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h));
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImageSource? Image
        {
            get
            {
                var svc = _svc();
                if (svc == null) return null;
                var hit = svc.Thumbs.Get(Key);
                if (hit != null) { _requested = false; return hit; }
                if (!_requested)
                {
                    _requested = true;
                    var sc = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
                    svc.RenderAsync(Key, CancellationToken.None, thumbnail: true).ContinueWith(t =>
                    {
                        _requested = false;
                        if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
                    }, sc);
                }
                return null;
            }
        }
    }
}
