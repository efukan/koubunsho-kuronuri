using System;
using System.ComponentModel;
using System.Globalization;
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
    /// <summary>Ribbon icon from a Segoe MDL2 Assets glyph (in-box font on Windows 10/11), drawn as a vector.</summary>
    [MarkupExtensionReturnType(typeof(ImageSource))]
    public sealed class GlyphExtension : MarkupExtension
    {
        public GlyphExtension(string code) { Code = code; }
        public string Code { get; set; }
        public Color Color { get; set; } = Color.FromRgb(0x33, 0x33, 0x33);

        public override object ProvideValue(IServiceProvider serviceProvider) => Make(Code, Color);

        public static ImageSource Make(string code, Color color)
        {
            const double em = 32;
            string ch = char.ConvertFromUtf32(int.Parse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var ft = new FormattedText(ch, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                em * 0.8, Brushes.Black, 1.0);
            var geo = ft.BuildGeometry(new Point(0, 0));
            var b = geo.Bounds;
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, em, em))));
            if (!b.IsEmpty)
            {
                geo.Transform = new TranslateTransform((em - b.Width) / 2 - b.X, (em - b.Height) / 2 - b.Y);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                g.Children.Add(new GeometryDrawing(brush, null, geo));
            }
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }
    }

    public static class Cmd
    {
        private static RoutedUICommand C(string text, string name, params InputGesture[] g) =>
            new RoutedUICommand(text, name, typeof(Cmd), new InputGestureCollection(g));

        public static readonly RoutedUICommand Open = C("開く", "Open", new KeyGesture(Key.O, ModifierKeys.Control));
        public static readonly RoutedUICommand SaveReview = C("確認用として保存", "SaveReview", new KeyGesture(Key.S, ModifierKeys.Control));
        public static readonly RoutedUICommand SaveAs = C("名前を付けて保存", "SaveAs", new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand Export = C("確定出力", "Export", new KeyGesture(Key.E, ModifierKeys.Control));
        public static readonly RoutedUICommand PrintRedacted = C("黒塗り済みを印刷", "PrintRedacted", new KeyGesture(Key.P, ModifierKeys.Control));
        public static readonly RoutedUICommand PrintReview = C("確認用を印刷", "PrintReview");
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
    }

    /// <summary>One row of the item list.</summary>
    public sealed class ItemRow
    {
        public ItemRow(RedactionItem item, int number)
        {
            Item = item; Number = number;
        }

        public RedactionItem Item { get; }
        public Guid Id => Item.Id;
        public int Number { get; }
        public int Page => Item.PageIndex + 1;
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

        public ThumbVM(int index, double wPt, double hPt, double dpiScale, Func<PageRenderService?> svc)
        {
            Index = index; _svc = svc; _scale = dpiScale;
            HeightDip = Math.Round(WidthDip * hPt / wPt);
            int wpx = Math.Max(1, (int)Math.Round(WidthDip * dpiScale)), hpx = Math.Max(1, (int)Math.Round(HeightDip * dpiScale));
            Key = RenderKey.Full(index, wpx, hpx);
        }

        public int Index { get; }
        public string Label => (Index + 1).ToString(CultureInfo.InvariantCulture);
        public double HeightDip { get; }
        public RenderKey Key { get; }

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
