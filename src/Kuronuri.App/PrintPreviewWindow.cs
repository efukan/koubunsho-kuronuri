using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Export;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Printing;
using Kuronuri.Core.Settings;

namespace Kuronuri.App
{
    /// <summary>
    /// In-app print preview and print execution dialog (ARCHITECTURE §10).
    /// Renders only the currently viewed page on demand via <see cref="PrintPreviewRenderer"/> with zero temporary files.
    /// </summary>
    public sealed class PrintPreviewWindow : Window
    {
        private const double PreviewDpi = 150.0;
        private static readonly double[] ZoomSteps = { 0.25, 0.33, 0.5, 0.67, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };

        private sealed class PaperOption
        {
            public string Label { get; }
            public double WidthDip { get; }
            public double HeightDip { get; }
            public PageMediaSize? MediaSize { get; }

            public PaperOption(string label, double widthDip, double heightDip, PageMediaSize? mediaSize)
            {
                Label = label; WidthDip = widthDip; HeightDip = heightDip; MediaSize = mediaSize;
            }

            public override string ToString() => Label;
        }

        private sealed class PrinterOption
        {
            public string Name { get; }
            public PrintQueue Queue { get; }

            public PrinterOption(PrintQueue queue)
            {
                Queue = queue;
                Name = queue.Name;
            }

            public override string ToString() => Name;
        }

        private sealed class TaggedOption<T>
        {
            public string Label { get; }
            public T Value { get; }

            public TaggedOption(string label, T value) { Label = label; Value = value; }
            public override string ToString() => Label;
        }

        private readonly PdfiumWorker _worker;
        private readonly PdfDoc _doc;
        private readonly OutputPlan _redactedPlan;
        private readonly OutputPlan _reviewPlan;
        private readonly string _watermark;
        private readonly int _currentBasePage;
        private readonly PrintPreviewRenderer _renderer;

        private bool _review;
        private RedactionStyle _style;
        private int _printDpi;

        private LocalPrintServer? _printServer;
        private readonly List<PrinterOption> _printers = new List<PrinterOption>();
        private readonly List<PaperOption> _papers = new List<PaperOption>();

        private readonly Border _reviewBanner;
        private readonly RadioButton _modeRedactedRadio;
        private readonly RadioButton _modeReviewRadio;
        private readonly StackPanel _stylePanel;
        private readonly RadioButton _styleBlackRadio;
        private readonly RadioButton _styleWhiteBorderRadio;
        private readonly ComboBox _printerBox;
        private readonly ComboBox _paperBox;
        private readonly RadioButton _portraitRadio;
        private readonly RadioButton _landscapeRadio;
        private readonly ComboBox _fitBox;
        private readonly CheckBox _autoRotateCheck;
        private readonly RadioButton _rangeAllRadio;
        private readonly RadioButton _rangeCurrentRadio;
        private readonly RadioButton _rangeCustomRadio;
        private readonly TextBox _rangeBox;
        private readonly TextBlock _rangeError;
        private readonly Button _copiesMinus;
        private readonly TextBox _copiesBox;
        private readonly Button _copiesPlus;
        private readonly TextBlock _copiesHint;
        private readonly ComboBox _duplexBox;
        private readonly ComboBox _colorBox;
        private readonly ComboBox _dpiBox;
        private readonly TextBlock _summaryText;
        private readonly Button _printButton;
        private readonly Button _osDialogButton;
        private readonly Button _closeButton;

        private readonly Button _prevBtn;
        private readonly TextBox _pageBox;
        private readonly TextBlock _pageTotalText;
        private readonly Button _nextBtn;
        private readonly TextBlock _zoomText;
        private readonly ScrollViewer _scroll;
        private readonly PrintSheetPreview _sheet;
        private readonly TextBlock _placementText;
        private readonly TextBlock _renderStateText;

        private bool _updatingUi;
        private bool _printing;
        private int _maxCopies = 99;
        private int _copies = 1;
        private bool _fitToWindow = true;
        private double _zoom = 1.0;

        private IReadOnlyList<int> _selectedPages;
        private bool _rangeValid = true;
        private int _previewPos;   // 0-based index into _selectedPages

        private int _cachedPlanIndex = -1;
        private bool _cachedReview;
        private RedactionStyle _cachedStyle;
        private BitmapSource? _cachedBitmap;
        private double _cachedWd;
        private double _cachedHd;
        private Rect? _cachedImageableArea;
        private CancellationTokenSource? _renderCts;
        private int _renderVersion;

        public int PrintedPages { get; private set; }
        public int PrintedCopies { get; private set; }
        public bool PrintedReview { get; private set; }

        private OutputPlan ActivePlan => _review ? _reviewPlan : _redactedPlan;
        private PrintFitMode SelectedFitMode => (_fitBox.SelectedItem as TaggedOption<PrintFitMode>)?.Value ?? PrintFitMode.ActualOrShrink;
        private bool AutoRotateEnabled => _autoRotateCheck.IsChecked == true;

        public PrintPreviewWindow(
            PdfiumWorker worker,
            PdfDoc doc,
            OutputPlan redactedPlan,
            OutputPlan reviewPlan,
            bool initialReview,
            RedactionStyle initialStyle,
            int initialPrintDpi,
            string watermark,
            int currentBasePage)
        {
            _worker = worker;
            _doc = doc;
            _redactedPlan = redactedPlan;
            _reviewPlan = reviewPlan;
            _review = initialReview || redactedPlan.Pages.Count == 0;
            _style = initialStyle;
            _printDpi = initialPrintDpi;
            _watermark = watermark;
            _currentBasePage = currentBasePage;
            _renderer = new PrintPreviewRenderer(worker, doc);

            _selectedPages = Enumerable.Range(0, ActivePlan.Pages.Count).ToList();
            int initIdx = IndexOfBasePage(ActivePlan, _currentBasePage);
            _previewPos = initIdx >= 0 ? initIdx : 0;

            Width = Math.Min(1120, SystemParameters.WorkArea.Width * 0.92);
            Height = Math.Min(800, SystemParameters.WorkArea.Height * 0.92);
            MinWidth = 720;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            FontSize = 13;
            Themes.ThemeManager.Attach(this);

            // ---------------- Top-level layout
            var outer = new DockPanel();

            _reviewBanner = new Border
            {
                Padding = new Thickness(14, 8, 14, 8),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            _reviewBanner.SetResourceReference(Border.BackgroundProperty, "InfoBar.Warning.Bg");
            _reviewBanner.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
            var bannerIcon = new TextBlock
            {
                Text = "\uE7BA",
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            bannerIcon.SetResourceReference(TextBlock.ForegroundProperty, "InfoBar.Warning.Icon");
            var bannerMsg = new TextBlock
            {
                Text = "確認用として印刷します：原本の内容がそのまま印刷され、印刷データにも残ります。庁内での確認以外には使わないでください。",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bannerRow.Children.Add(bannerIcon);
            bannerRow.Children.Add(bannerMsg);
            _reviewBanner.Child = bannerRow;
            DockPanel.SetDock(_reviewBanner, Dock.Top);
            outer.Children.Add(_reviewBanner);

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(312) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---------------- Left panel: print settings + action buttons
            var leftBorder = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            leftBorder.SetResourceReference(Border.BackgroundProperty, "Bg.Bar");
            leftBorder.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            var leftDock = new DockPanel();

            // Action buttons pinned at bottom
            var actionsBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 10, 14, 10) };
            actionsBorder.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            var actionsStack = new StackPanel();
            _printButton = new Button
            {
                Content = "印刷",
                Style = (Style)Application.Current.FindResource("AccentButton"),
                MinHeight = 34,
                FontWeight = FontWeights.SemiBold,
            };
            _printButton.Click += (s, e) => ExecuteDirectPrint();

            _osDialogButton = new Button
            {
                Content = "詳細設定で印刷…",
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "給紙トレイや割り付けなど、プリンター固有の設定を変更して印刷します",
            };
            _osDialogButton.Click += (s, e) => ExecuteOsDialogPrint();

            _closeButton = new Button
            {
                Content = "閉じる",
                IsCancel = true,
                Margin = new Thickness(0, 6, 0, 0),
            };
            actionsStack.Children.Add(_printButton);
            actionsStack.Children.Add(_osDialogButton);
            actionsStack.Children.Add(_closeButton);
            actionsBorder.Child = actionsStack;
            DockPanel.SetDock(actionsBorder, Dock.Bottom);
            leftDock.Children.Add(actionsBorder);

            // Scrollable settings form
            var form = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

            // --- Print mode & redaction style
            form.Children.Add(SectionLabel("印刷の種類", topMargin: 0));
            _modeRedactedRadio = new RadioButton
            {
                Content = "黒塗りした状態",
                GroupName = "PrintMode",
                IsChecked = !_review,
                IsEnabled = _redactedPlan.Pages.Count > 0,
                ToolTip = _redactedPlan.Pages.Count > 0 ? "黒塗りを焼き込んだ画像だけを印刷します" : "すべてのページが除外されているため選択できません",
                Margin = new Thickness(0, 4, 0, 4),
            };
            _modeReviewRadio = new RadioButton
            {
                Content = "確認用（赤枠・透かし・庁内限定）",
                GroupName = "PrintMode",
                IsChecked = _review,
                ToolTip = "原本の上に半透明の赤・番号・透かしを重ねて印刷します（庁内確認専用）",
                Margin = new Thickness(0, 0, 0, 4),
            };
            _modeRedactedRadio.Checked += (s, e) => { if (!_updatingUi) OnModeChanged(review: false); };
            _modeReviewRadio.Checked += (s, e) => { if (!_updatingUi) OnModeChanged(review: true); };
            form.Children.Add(_modeRedactedRadio);
            form.Children.Add(_modeReviewRadio);

            _stylePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 2, 0, 0) };
            var styleLabel = new TextBlock { Text = "見た目:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            styleLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _styleBlackRadio = new RadioButton { Content = "黒塗り", GroupName = "PrintStyle", IsChecked = _style == RedactionStyle.Black, Margin = new Thickness(0, 0, 12, 0), FontSize = 12 };
            _styleWhiteBorderRadio = new RadioButton { Content = "白塗り＋黒枠", GroupName = "PrintStyle", IsChecked = _style == RedactionStyle.WhiteWithBorder, FontSize = 12 };
            _styleBlackRadio.Checked += (s, e) => { if (!_updatingUi) OnStyleChanged(RedactionStyle.Black); };
            _styleWhiteBorderRadio.Checked += (s, e) => { if (!_updatingUi) OnStyleChanged(RedactionStyle.WhiteWithBorder); };
            _stylePanel.Children.Add(styleLabel);
            _stylePanel.Children.Add(_styleBlackRadio);
            _stylePanel.Children.Add(_styleWhiteBorderRadio);
            form.Children.Add(_stylePanel);

            // --- Printer & paper
            form.Children.Add(SectionLabel("プリンター"));
            _printerBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            _printerBox.SelectionChanged += (s, e) => { if (!_updatingUi) OnPrinterChanged(); };
            form.Children.Add(_printerBox);

            form.Children.Add(SectionLabel("用紙サイズ"));
            _paperBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            _paperBox.SelectionChanged += (s, e) => { if (!_updatingUi) OnPaperOrOrientationChanged(); };
            form.Children.Add(_paperBox);

            form.Children.Add(SectionLabel("用紙の向き"));
            var orientPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _portraitRadio = new RadioButton { Content = "縦", GroupName = "PrintOrient", IsChecked = true, Margin = new Thickness(0, 0, 20, 0) };
            _landscapeRadio = new RadioButton { Content = "横", GroupName = "PrintOrient" };
            _portraitRadio.Checked += (s, e) => { if (!_updatingUi) OnPaperOrOrientationChanged(); };
            _landscapeRadio.Checked += (s, e) => { if (!_updatingUi) OnPaperOrOrientationChanged(); };
            orientPanel.Children.Add(_portraitRadio);
            orientPanel.Children.Add(_landscapeRadio);
            form.Children.Add(orientPanel);

            // --- Placement / scaling
            form.Children.Add(SectionLabel("配置と倍率"));
            _fitBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            _fitBox.Items.Add(new TaggedOption<PrintFitMode>("等倍優先（はみ出す場合は縮小）", PrintFitMode.ActualOrShrink));
            _fitBox.Items.Add(new TaggedOption<PrintFitMode>("用紙に合わせる（拡大・縮小）", PrintFitMode.FitToPaper));
            _fitBox.SelectedIndex = 0;
            _fitBox.SelectionChanged += (s, e) => { if (!_updatingUi) UpdateSheetLayout(); };
            _autoRotateCheck = new CheckBox
            {
                Content = "縦横が違うページを自動で回転",
                IsChecked = true,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
            };
            _autoRotateCheck.Checked += (s, e) => { if (!_updatingUi) UpdateSheetLayout(); };
            _autoRotateCheck.Unchecked += (s, e) => { if (!_updatingUi) UpdateSheetLayout(); };
            form.Children.Add(_fitBox);
            form.Children.Add(_autoRotateCheck);

            // --- Page range
            form.Children.Add(SectionLabel("印刷するページ"));
            _rangeAllRadio = new RadioButton { GroupName = "PrintRange", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            _rangeCurrentRadio = new RadioButton { GroupName = "PrintRange", Margin = new Thickness(0, 0, 0, 4) };
            _rangeCustomRadio = new RadioButton { Content = "ページを指定", GroupName = "PrintRange", Margin = new Thickness(0, 0, 0, 4) };
            _rangeBox = new TextBox
            {
                Margin = new Thickness(20, 0, 0, 0),
                IsEnabled = false,
                ToolTip = "例: 1-3, 5（全角数字や「、」も使えます）",
            };
            _rangeError = new TextBlock
            {
                Margin = new Thickness(20, 4, 0, 0),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            _rangeError.SetResourceReference(TextBlock.ForegroundProperty, "Text.Warn");

            _rangeAllRadio.Checked += (s, e) => { if (!_updatingUi) OnRangeModeChanged(); };
            _rangeCurrentRadio.Checked += (s, e) => { if (!_updatingUi) OnRangeModeChanged(); };
            _rangeCustomRadio.Checked += (s, e) => { if (!_updatingUi) OnRangeModeChanged(); };
            _rangeBox.TextChanged += (s, e) => { if (!_updatingUi && _rangeCustomRadio.IsChecked == true) ApplyRangeSelection(); };

            form.Children.Add(_rangeAllRadio);
            form.Children.Add(_rangeCurrentRadio);
            form.Children.Add(_rangeCustomRadio);
            form.Children.Add(_rangeBox);
            form.Children.Add(_rangeError);

            // --- Copies
            form.Children.Add(SectionLabel("部数"));
            var copiesRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _copiesMinus = new Button { Content = "－", Width = 28, Padding = new Thickness(0) };
            _copiesBox = new TextBox { Text = "1", Width = 52, TextAlignment = TextAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            _copiesPlus = new Button { Content = "＋", Width = 28, Padding = new Thickness(0) };
            _copiesHint = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 11.5 };
            _copiesHint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _copiesMinus.Click += (s, e) => SetCopies(_copies - 1);
            _copiesPlus.Click += (s, e) => SetCopies(_copies + 1);
            _copiesBox.LostFocus += (s, e) => CommitCopiesBox();
            _copiesBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitCopiesBox(); e.Handled = true; } };
            copiesRow.Children.Add(_copiesMinus);
            copiesRow.Children.Add(_copiesBox);
            copiesRow.Children.Add(_copiesPlus);
            copiesRow.Children.Add(_copiesHint);
            form.Children.Add(copiesRow);

            // --- Printer options (duplex, color, resolution)
            form.Children.Add(SectionLabel("印刷オプション"));
            var optGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _duplexBox = new ComboBox();
            _colorBox = new ComboBox();
            _dpiBox = new ComboBox();
            foreach (int d in AppSettings.PrintDpiChoices)
                _dpiBox.Items.Add(new TaggedOption<int>(d == 200 ? "200 dpi（速い）" : d == 300 ? "300 dpi（きれい）" : d + " dpi", d));
            int dpiSel = Array.IndexOf(AppSettings.PrintDpiChoices, _printDpi);
            _dpiBox.SelectedIndex = dpiSel >= 0 ? dpiSel : AppSettings.PrintDpiChoices.Length - 1;
            _dpiBox.SelectionChanged += (s, e) =>
            {
                if (_dpiBox.SelectedItem is TaggedOption<int> opt) { _printDpi = opt.Value; UpdateSummaryAndButtons(); }
            };

            AddOptionRow(optGrid, "両面印刷", _duplexBox);
            AddOptionRow(optGrid, "カラー", _colorBox);
            AddOptionRow(optGrid, "解像度", _dpiBox);
            form.Children.Add(optGrid);

            var infoBox = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            infoBox.SetResourceReference(Border.BackgroundProperty, "Bg.Layer");
            infoBox.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            _summaryText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
            _summaryText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            infoBox.Child = _summaryText;
            form.Children.Add(infoBox);

            leftDock.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            leftBorder.Child = leftDock;
            Grid.SetColumn(leftBorder, 0);
            mainGrid.Children.Add(leftBorder);

            // ---------------- Right panel: toolbar + paper canvas + status bar
            var rightDock = new DockPanel();

            var toolBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 6, 10, 6) };
            toolBorder.SetResourceReference(Border.BackgroundProperty, "Bg.Bar");
            toolBorder.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            var toolGrid = new Grid();
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var navPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var subtleStyle = (Style)Application.Current.FindResource("SubtleButton");
            _prevBtn = new Button { Content = "◀", Style = subtleStyle, ToolTip = "前のページ (PageUp)", Width = 28 };
            _prevBtn.Click += (s, e) => GoToPreviewPos(_previewPos - 1);
            _pageBox = new TextBox { Width = 44, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            _pageBox.LostFocus += (s, e) => CommitPageBox();
            _pageBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitPageBox(); e.Handled = true; } };
            _pageTotalText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            _nextBtn = new Button { Content = "▶", Style = subtleStyle, ToolTip = "次のページ (PageDown)", Width = 28 };
            _nextBtn.Click += (s, e) => GoToPreviewPos(_previewPos + 1);
            navPanel.Children.Add(_prevBtn);
            navPanel.Children.Add(_pageBox);
            navPanel.Children.Add(_pageTotalText);
            navPanel.Children.Add(_nextBtn);
            Grid.SetColumn(navPanel, 0);
            toolGrid.Children.Add(navPanel);

            var zoomPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var zoomOutBtn = new Button { Content = "－", Style = subtleStyle, ToolTip = "縮小", Width = 28 };
            var fitBtn = new Button { Content = "全体", Style = subtleStyle, ToolTip = "用紙全体を表示", Margin = new Thickness(2, 0, 2, 0) };
            var z100Btn = new Button { Content = "100%", Style = subtleStyle, ToolTip = "等倍表示", Margin = new Thickness(0, 0, 2, 0) };
            var zoomInBtn = new Button { Content = "＋", Style = subtleStyle, ToolTip = "拡大", Width = 28 };
            _zoomText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 44, TextAlignment = TextAlignment.Right };
            _zoomText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            zoomOutBtn.Click += (s, e) => StepZoom(-1);
            zoomInBtn.Click += (s, e) => StepZoom(1);
            fitBtn.Click += (s, e) => { _fitToWindow = true; UpdateSheetLayout(); };
            z100Btn.Click += (s, e) => { _fitToWindow = false; _zoom = 1.0; UpdateSheetLayout(); };
            zoomPanel.Children.Add(zoomOutBtn);
            zoomPanel.Children.Add(fitBtn);
            zoomPanel.Children.Add(z100Btn);
            zoomPanel.Children.Add(zoomInBtn);
            zoomPanel.Children.Add(_zoomText);
            Grid.SetColumn(zoomPanel, 2);
            toolGrid.Children.Add(zoomPanel);

            toolBorder.Child = toolGrid;
            DockPanel.SetDock(toolBorder, Dock.Top);
            rightDock.Children.Add(toolBorder);

            var statusBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
            statusBorder.SetResourceReference(Border.BackgroundProperty, "Bg.Bar");
            statusBorder.SetResourceReference(Border.BorderBrushProperty, "Stroke.Divider");
            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _placementText = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            _placementText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _renderStateText = new TextBlock { FontSize = 12, Margin = new Thickness(12, 0, 0, 0) };
            _renderStateText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            Grid.SetColumn(_placementText, 0);
            Grid.SetColumn(_renderStateText, 1);
            statusGrid.Children.Add(_placementText);
            statusGrid.Children.Add(_renderStateText);
            statusBorder.Child = statusGrid;
            DockPanel.SetDock(statusBorder, Dock.Bottom);
            rightDock.Children.Add(statusBorder);

            _sheet = new PrintSheetPreview(_watermark)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20),
            };
            _scroll = new ScrollViewer
            {
                Content = _sheet,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
            };
            _scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "Bg.Canvas");
            _scroll.SizeChanged += (s, e) => { if (_fitToWindow) UpdateSheetLayout(); };
            _scroll.PreviewMouseWheel += Scroll_PreviewMouseWheel;
            rightDock.Children.Add(_scroll);

            Grid.SetColumn(rightDock, 1);
            mainGrid.Children.Add(rightDock);
            outer.Children.Add(mainGrid);
            Content = outer;

            ApplyModeChrome();
            UpdateRangeRadioLabels();

            PreviewKeyDown += Window_PreviewKeyDown;
            Loaded += (s, e) =>
            {
                LoadPrinters();
                _cachedImageableArea = QueryImageableArea(CurrentPaperSize);
                UpdateSummaryAndButtons();
                UpdateSheetLayout();
                RequestRenderCurrentPage();
            };
        }

        private static TextBlock SectionLabel(string text, double topMargin = 11)
        {
            var tb = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, topMargin, 0, 0) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
            return tb;
        }

        private static void AddOptionRow(Grid g, string label, FrameworkElement control)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 3, 6, 3) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            Grid.SetRow(tb, r);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 3, 0, 3);
            g.Children.Add(tb);
            g.Children.Add(control);
        }

        private static int IndexOfBasePage(OutputPlan plan, int basePage)
        {
            for (int i = 0; i < plan.Pages.Count; i++)
                if (plan.Pages[i].BasePage == basePage) return i;
            return -1;
        }

        // ================================================================== mode & style switching

        private void ApplyModeChrome()
        {
            Title = _review ? "印刷プレビュー（確認用・庁内限定）" : "印刷プレビュー（黒塗りした状態）";
            _reviewBanner.Visibility = _review ? Visibility.Visible : Visibility.Collapsed;
            _stylePanel.Visibility = _review ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateRangeRadioLabels()
        {
            var plan = ActivePlan;
            int curIdx = IndexOfBasePage(plan, _currentBasePage);
            _rangeAllRadio.Content = "すべてのページ（全 " + plan.Pages.Count + " ページ）";
            _rangeCurrentRadio.Content = curIdx >= 0
                ? "現在のページ（ページ " + (curIdx + 1) + "）"
                : "現在のページ（除外中）";
            _rangeCurrentRadio.IsEnabled = curIdx >= 0;
            if (curIdx < 0 && _rangeCurrentRadio.IsChecked == true)
            {
                _updatingUi = true;
                try { _rangeAllRadio.IsChecked = true; }
                finally { _updatingUi = false; }
            }
        }

        private void OnModeChanged(bool review)
        {
            if (_review == review) return;
            int viewingBase = _selectedPages.Count > 0 && _previewPos < _selectedPages.Count
                ? ActivePlan.Pages[_selectedPages[_previewPos]].BasePage
                : _currentBasePage;

            _review = review;
            ApplyModeChrome();
            UpdateRangeRadioLabels();
            InvalidateRenderCache();
            ApplyRangeSelection(preferredBasePage: viewingBase);
        }

        private void OnStyleChanged(RedactionStyle style)
        {
            if (_style == style) return;
            _style = style;
            InvalidateRenderCache();
            UpdateSummaryAndButtons();
            RequestRenderCurrentPage();
        }

        private void InvalidateRenderCache()
        {
            _cachedBitmap = null;
            _cachedPlanIndex = -1;
        }

        // ================================================================== printers & capabilities

        private void LoadPrinters()
        {
            _updatingUi = true;
            try
            {
                _printerBox.Items.Clear();
                _printers.Clear();

                string? defaultName = null;
                try
                {
                    using (var def = LocalPrintServer.GetDefaultPrintQueue())
                        defaultName = def?.Name;
                }
                catch { }

                try
                {
                    _printServer = new LocalPrintServer();
                    var queues = _printServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
                    foreach (var q in queues)
                    {
                        try { _printers.Add(new PrinterOption(q)); }
                        catch { try { q.Dispose(); } catch { } }
                    }
                }
                catch { }

                _printers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                foreach (var p in _printers) _printerBox.Items.Add(p);

                if (_printers.Count == 0)
                {
                    _printerBox.Items.Add("プリンターが見つかりません（A4 でプレビュー）");
                    _printerBox.SelectedIndex = 0;
                    _printerBox.IsEnabled = false;
                    PopulateFallbackPapers();
                    PopulateDuplexAndColor(null, null);
                    SetMaxCopies(1);
                }
                else
                {
                    int idx = defaultName != null ? _printers.FindIndex(p => string.Equals(p.Name, defaultName, StringComparison.OrdinalIgnoreCase)) : -1;
                    _printerBox.SelectedIndex = idx >= 0 ? idx : 0;
                    _printerBox.IsEnabled = true;
                    LoadCapabilitiesForSelectedPrinter();
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void OnPrinterChanged()
        {
            _updatingUi = true;
            try { LoadCapabilitiesForSelectedPrinter(); }
            finally { _updatingUi = false; }
            _cachedImageableArea = QueryImageableArea(CurrentPaperSize);
            UpdateSummaryAndButtons();
            UpdateSheetLayout();
        }

        private void OnPaperOrOrientationChanged()
        {
            _cachedImageableArea = QueryImageableArea(CurrentPaperSize);
            UpdateSheetLayout();
        }

        private PrinterOption? SelectedPrinter => _printerBox.SelectedItem as PrinterOption;

        private void LoadCapabilitiesForSelectedPrinter()
        {
            var opt = SelectedPrinter;
            if (opt == null) { PopulateFallbackPapers(); PopulateDuplexAndColor(null, null); SetMaxCopies(1); return; }

            PrintCapabilities? caps = null;
            PrintTicket? defTicket = null;
            try { defTicket = opt.Queue.UserPrintTicket ?? opt.Queue.DefaultPrintTicket; } catch { }
            try { caps = opt.Queue.GetPrintCapabilities(defTicket); } catch { }

            _papers.Clear();
            _paperBox.Items.Clear();

            if (caps?.PageMediaSizeCapability != null)
            {
                var seen = new HashSet<string>();
                foreach (var m in caps.PageMediaSizeCapability)
                {
                    if (!m.Width.HasValue || !m.Height.HasValue || m.Width.Value < 10 || m.Height.Value < 10) continue;
                    double shortSide = Math.Min(m.Width.Value, m.Height.Value);
                    double longSide = Math.Max(m.Width.Value, m.Height.Value);
                    string key = (m.PageMediaSizeName?.ToString() ?? "") + ":" + Math.Round(shortSide) + "x" + Math.Round(longSide);
                    if (!seen.Add(key)) continue;
                    _papers.Add(new PaperOption(FormatPaperLabel(m.PageMediaSizeName, shortSide, longSide), shortSide, longSide, m));
                }
            }

            if (_papers.Count == 0)
            {
                PopulateFallbackPapers();
            }
            else
            {
                _papers.Sort((a, b) =>
                {
                    int pa = PaperPriority(a.MediaSize?.PageMediaSizeName);
                    int pb = PaperPriority(b.MediaSize?.PageMediaSizeName);
                    return pa != pb ? pa.CompareTo(pb) : string.Compare(a.Label, b.Label, StringComparison.CurrentCulture);
                });
                foreach (var p in _papers) _paperBox.Items.Add(p);

                int sel = FindDefaultPaperIndex(defTicket?.PageMediaSize);
                _paperBox.SelectedIndex = sel >= 0 ? sel : 0;
            }

            bool isLandscape = defTicket?.PageOrientation == PageOrientation.Landscape || defTicket?.PageOrientation == PageOrientation.ReverseLandscape;
            _portraitRadio.IsChecked = !isLandscape;
            _landscapeRadio.IsChecked = isLandscape;

            PopulateDuplexAndColor(caps, defTicket);

            int maxCopy = caps?.MaxCopyCount ?? 99;
            SetMaxCopies(Math.Max(1, Math.Min(99, maxCopy)));
        }

        private void PopulateDuplexAndColor(PrintCapabilities? caps, PrintTicket? defTicket)
        {
            _duplexBox.Items.Clear();
            var duplexList = caps?.DuplexingCapability;
            if (duplexList != null && duplexList.Count > 1)
            {
                if (duplexList.Contains(Duplexing.OneSided))
                    _duplexBox.Items.Add(new TaggedOption<Duplexing?>("片面", Duplexing.OneSided));
                if (duplexList.Contains(Duplexing.TwoSidedLongEdge))
                    _duplexBox.Items.Add(new TaggedOption<Duplexing?>("両面（長辺とじ）", Duplexing.TwoSidedLongEdge));
                if (duplexList.Contains(Duplexing.TwoSidedShortEdge))
                    _duplexBox.Items.Add(new TaggedOption<Duplexing?>("両面（短辺とじ）", Duplexing.TwoSidedShortEdge));
            }
            if (_duplexBox.Items.Count <= 1)
            {
                _duplexBox.Items.Clear();
                _duplexBox.Items.Add(new TaggedOption<Duplexing?>("片面のみ", null));
                _duplexBox.SelectedIndex = 0;
                _duplexBox.IsEnabled = false;
            }
            else
            {
                int sel = 0;
                if (defTicket?.Duplexing != null)
                {
                    for (int i = 0; i < _duplexBox.Items.Count; i++)
                        if (_duplexBox.Items[i] is TaggedOption<Duplexing?> o && o.Value == defTicket.Duplexing) { sel = i; break; }
                }
                _duplexBox.SelectedIndex = sel;
                _duplexBox.IsEnabled = true;
            }

            _colorBox.Items.Clear();
            var colorList = caps?.OutputColorCapability;
            if (colorList != null && colorList.Count > 0)
            {
                if (colorList.Contains(OutputColor.Color))
                    _colorBox.Items.Add(new TaggedOption<OutputColor?>("カラー", OutputColor.Color));
                if (colorList.Contains(OutputColor.Monochrome))
                    _colorBox.Items.Add(new TaggedOption<OutputColor?>("白黒（モノクロ）", OutputColor.Monochrome));
                else if (colorList.Contains(OutputColor.Grayscale))
                    _colorBox.Items.Add(new TaggedOption<OutputColor?>("白黒（グレースケール）", OutputColor.Grayscale));
            }
            if (_colorBox.Items.Count <= 1)
            {
                if (_colorBox.Items.Count == 0)
                    _colorBox.Items.Add(new TaggedOption<OutputColor?>("プリンター既定", null));
                _colorBox.SelectedIndex = 0;
                _colorBox.IsEnabled = false;
            }
            else
            {
                int sel = 0;
                if (defTicket?.OutputColor != null)
                {
                    for (int i = 0; i < _colorBox.Items.Count; i++)
                        if (_colorBox.Items[i] is TaggedOption<OutputColor?> o && o.Value == defTicket.OutputColor) { sel = i; break; }
                }
                _colorBox.SelectedIndex = sel;
                _colorBox.IsEnabled = true;
            }
        }

        private void PopulateFallbackPapers()
        {
            _papers.Clear();
            _paperBox.Items.Clear();
            _papers.Add(new PaperOption("A4 (210 × 297 mm)", 793.70, 1122.52, new PageMediaSize(PageMediaSizeName.ISOA4)));
            _papers.Add(new PaperOption("A3 (297 × 420 mm)", 1122.52, 1587.40, new PageMediaSize(PageMediaSizeName.ISOA3)));
            _papers.Add(new PaperOption("B5 (182 × 257 mm)", 687.87, 971.34, new PageMediaSize(PageMediaSizeName.JISB5)));
            _papers.Add(new PaperOption("B4 (257 × 364 mm)", 971.34, 1375.75, new PageMediaSize(PageMediaSizeName.JISB4)));
            _papers.Add(new PaperOption("レター (216 × 279 mm)", 816.0, 1056.0, new PageMediaSize(PageMediaSizeName.NorthAmericaLetter)));
            foreach (var p in _papers) _paperBox.Items.Add(p);
            _paperBox.SelectedIndex = 0;
        }

        private int FindDefaultPaperIndex(PageMediaSize? def)
        {
            if (def?.PageMediaSizeName != null)
            {
                int i = _papers.FindIndex(p => p.MediaSize?.PageMediaSizeName == def.PageMediaSizeName);
                if (i >= 0) return i;
            }
            if (def?.Width != null && def.Height != null)
            {
                double s = Math.Min(def.Width.Value, def.Height.Value), l = Math.Max(def.Width.Value, def.Height.Value);
                int i = _papers.FindIndex(p => Math.Abs(p.WidthDip - s) < 4 && Math.Abs(p.HeightDip - l) < 4);
                if (i >= 0) return i;
            }
            int a4 = _papers.FindIndex(p => p.MediaSize?.PageMediaSizeName == PageMediaSizeName.ISOA4);
            return a4 >= 0 ? a4 : 0;
        }

        private static int PaperPriority(PageMediaSizeName? name) => name switch
        {
            PageMediaSizeName.ISOA4 => 0,
            PageMediaSizeName.ISOA3 => 1,
            PageMediaSizeName.JISB5 => 2,
            PageMediaSizeName.JISB4 => 3,
            PageMediaSizeName.ISOA5 => 4,
            PageMediaSizeName.NorthAmericaLetter => 5,
            PageMediaSizeName.NorthAmericaLegal => 6,
            PageMediaSizeName.JapanHagakiPostcard => 7,
            _ => 50,
        };

        private static string FormatPaperLabel(PageMediaSizeName? name, double shortDip, double longDip)
        {
            int wMm = (int)Math.Round(shortDip * 25.4 / 96.0);
            int hMm = (int)Math.Round(longDip * 25.4 / 96.0);
            string mm = "(" + wMm + " × " + hMm + " mm)";
            return name switch
            {
                PageMediaSizeName.ISOA4 => "A4 " + mm,
                PageMediaSizeName.ISOA3 => "A3 " + mm,
                PageMediaSizeName.ISOA5 => "A5 " + mm,
                PageMediaSizeName.ISOA6 => "A6 " + mm,
                PageMediaSizeName.JISB4 => "B4 " + mm,
                PageMediaSizeName.JISB5 => "B5 " + mm,
                PageMediaSizeName.NorthAmericaLetter => "レター " + mm,
                PageMediaSizeName.NorthAmericaLegal => "リーガル " + mm,
                PageMediaSizeName.JapanHagakiPostcard => "はがき " + mm,
                _ => wMm + " × " + hMm + " mm",
            };
        }

        private Size CurrentPaperSize
        {
            get
            {
                var p = _paperBox.SelectedItem as PaperOption ?? _papers.FirstOrDefault();
                double shortSide = p != null ? p.WidthDip : 793.70;
                double longSide = p != null ? p.HeightDip : 1122.52;
                return _landscapeRadio.IsChecked == true ? new Size(longSide, shortSide) : new Size(shortSide, longSide);
            }
        }

        private Rect? QueryImageableArea(Size paper)
        {
            var opt = SelectedPrinter;
            if (opt == null) return null;
            try
            {
                var ticket = BuildPrintTicket(opt.Queue);
                var caps = opt.Queue.GetPrintCapabilities(ticket);
                var area = caps?.PageImageableArea;
                if (area == null || area.ExtentWidth <= 0 || area.ExtentHeight <= 0) return null;
                if (area.OriginWidth <= 1 && area.OriginHeight <= 1 &&
                    Math.Abs(area.ExtentWidth - paper.Width) <= 2 && Math.Abs(area.ExtentHeight - paper.Height) <= 2)
                    return null;
                return new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
            }
            catch { return null; }
        }

        private PrintTicket BuildPrintTicket(PrintQueue queue)
        {
            PrintTicket ticket;
            try { ticket = queue.UserPrintTicket?.Clone() ?? queue.DefaultPrintTicket?.Clone() ?? new PrintTicket(); }
            catch { ticket = new PrintTicket(); }

            if (_paperBox.SelectedItem is PaperOption p && p.MediaSize != null)
                ticket.PageMediaSize = p.MediaSize;
            ticket.PageOrientation = _landscapeRadio.IsChecked == true ? PageOrientation.Landscape : PageOrientation.Portrait;
            ticket.CopyCount = Math.Max(1, Math.Min(_maxCopies, _copies));
            if (_duplexBox.IsEnabled && _duplexBox.SelectedItem is TaggedOption<Duplexing?> dup && dup.Value.HasValue)
                ticket.Duplexing = dup.Value.Value;
            if (_colorBox.IsEnabled && _colorBox.SelectedItem is TaggedOption<OutputColor?> col && col.Value.HasValue)
                ticket.OutputColor = col.Value.Value;

            try
            {
                var validated = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket ?? new PrintTicket(), ticket);
                return validated.ValidatedPrintTicket ?? ticket;
            }
            catch { return ticket; }
        }

        // ================================================================== page range & copies

        private void OnRangeModeChanged()
        {
            _rangeBox.IsEnabled = _rangeCustomRadio.IsChecked == true;
            if (_rangeBox.IsEnabled) _rangeBox.Focus();
            ApplyRangeSelection();
        }

        private void ApplyRangeSelection(int? preferredBasePage = null)
        {
            var plan = ActivePlan;
            int targetBase = preferredBasePage ??
                (_selectedPages.Count > 0 && _previewPos < _selectedPages.Count && _selectedPages[_previewPos] < plan.Pages.Count
                    ? plan.Pages[_selectedPages[_previewPos]].BasePage
                    : _currentBasePage);

            if (_rangeAllRadio.IsChecked == true)
            {
                _selectedPages = Enumerable.Range(0, plan.Pages.Count).ToList();
                _rangeValid = true;
                _rangeError.Visibility = Visibility.Collapsed;
            }
            else if (_rangeCurrentRadio.IsChecked == true)
            {
                int curIdx = IndexOfBasePage(plan, _currentBasePage);
                if (curIdx >= 0)
                {
                    _selectedPages = new[] { curIdx };
                    _rangeValid = true;
                    _rangeError.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _selectedPages = Array.Empty<int>();
                    _rangeValid = false;
                    _rangeError.Text = "現在のページは除外されています。";
                    _rangeError.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (PrintPageRange.TryParse(_rangeBox.Text, plan.Pages.Count, out var parsed, out string? err))
                {
                    _selectedPages = parsed;
                    _rangeValid = true;
                    _rangeError.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _rangeValid = false;
                    _rangeError.Text = err ?? "ページ指定が正しくありません。";
                    _rangeError.Visibility = Visibility.Visible;
                }
            }

            if (_rangeValid && _selectedPages.Count > 0)
            {
                int keep = -1;
                for (int i = 0; i < _selectedPages.Count; i++)
                    if (plan.Pages[_selectedPages[i]].BasePage == targetBase) { keep = i; break; }
                _previewPos = keep >= 0 ? keep : Math.Min(_previewPos, _selectedPages.Count - 1);
                if (_previewPos < 0) _previewPos = 0;
                RequestRenderCurrentPage();
            }

            UpdateNavigationUi();
            UpdateSummaryAndButtons();
        }

        private void SetMaxCopies(int max)
        {
            _maxCopies = Math.Max(1, max);
            if (_copies > _maxCopies) _copies = _maxCopies;
            _copiesBox.Text = _copies.ToString(CultureInfo.InvariantCulture);
            bool multi = _maxCopies > 1;
            _copiesBox.IsEnabled = multi;
            _copiesMinus.IsEnabled = multi && _copies > 1;
            _copiesPlus.IsEnabled = multi && _copies < _maxCopies;
            _copiesHint.Text = multi ? "（最大 " + _maxCopies + "）" : "（1部固定）";
        }

        private void SetCopies(int value)
        {
            _copies = Math.Max(1, Math.Min(_maxCopies, value));
            _copiesBox.Text = _copies.ToString(CultureInfo.InvariantCulture);
            _copiesMinus.IsEnabled = _maxCopies > 1 && _copies > 1;
            _copiesPlus.IsEnabled = _maxCopies > 1 && _copies < _maxCopies;
            UpdateSummaryAndButtons();
        }

        private void CommitCopiesBox()
        {
            if (int.TryParse(_copiesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                SetCopies(n);
            else
                SetCopies(_copies);
        }

        private void UpdateSummaryAndButtons()
        {
            int count = _rangeValid ? _selectedPages.Count : 0;
            string mode = _review
                ? "確認用（原本＋赤枠・透かし）"
                : (_style == RedactionStyle.WhiteWithBorder ? "黒塗りした状態（白塗り＋黒枠）" : "黒塗りした状態（黒塗り）");
            _summaryText.Text =
                "モード: " + mode + "\n" +
                "印刷対象: " + count + " ページ × " + _copies + " 部\n" +
                "解像度: " + _printDpi + " dpi";

            bool canRun = !_printing && _rangeValid && count > 0;
            _printButton.IsEnabled = canRun && SelectedPrinter != null;
            _osDialogButton.IsEnabled = canRun;
            _closeButton.IsEnabled = !_printing;
        }

        // ================================================================== preview navigation, zoom & rendering

        private void GoToPreviewPos(int pos)
        {
            if (_selectedPages.Count == 0) return;
            int clamped = Math.Max(0, Math.Min(_selectedPages.Count - 1, pos));
            if (clamped == _previewPos && _cachedPlanIndex == _selectedPages[clamped] && _cachedReview == _review && _cachedStyle == _style && _cachedBitmap != null) return;
            _previewPos = clamped;
            UpdateNavigationUi();
            RequestRenderCurrentPage();
        }

        private void CommitPageBox()
        {
            if (_selectedPages.Count == 0) return;
            if (int.TryParse(_pageBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
                GoToPreviewPos(p - 1);
            else
                UpdateNavigationUi();
        }

        private void UpdateNavigationUi()
        {
            int total = _selectedPages.Count;
            _pageBox.Text = total > 0 ? (_previewPos + 1).ToString(CultureInfo.InvariantCulture) : "0";
            string suffix = "/ " + total;
            if (total > 0 && total < ActivePlan.Pages.Count)
                suffix += "（文書 " + (_selectedPages[_previewPos] + 1) + " ページ目）";
            _pageTotalText.Text = suffix;
            _prevBtn.IsEnabled = _previewPos > 0;
            _nextBtn.IsEnabled = _previewPos < total - 1;
        }

        private void StepZoom(int dir)
        {
            _fitToWindow = false;
            double next = dir > 0
                ? (ZoomSteps.FirstOrDefault(z => z > _zoom + 1e-3) is var z1 && z1 > 0 ? z1 : ZoomSteps[ZoomSteps.Length - 1])
                : (ZoomSteps.LastOrDefault(z => z < _zoom - 1e-3) is var z2 && z2 > 0 ? z2 : ZoomSteps[0]);
            _zoom = next;
            UpdateSheetLayout();
        }

        private void UpdateSheetLayout()
        {
            Size paper = CurrentPaperSize;
            if (_fitToWindow)
            {
                double vw = _scroll.ViewportWidth > 40 ? _scroll.ViewportWidth - 40 : Math.Max(300, ActualWidth - 380);
                double vh = _scroll.ViewportHeight > 40 ? _scroll.ViewportHeight - 40 : Math.Max(300, ActualHeight - 160);
                double fit = Math.Min(vw / paper.Width, vh / paper.Height);
                _zoom = Math.Max(0.1, Math.Min(3.0, fit));
            }
            _zoomText.Text = Math.Round(_zoom * 100) + "%";

            var plan = ActivePlan;
            int planIdx = _selectedPages.Count > 0 && _previewPos < _selectedPages.Count ? _selectedPages[_previewPos] : -1;
            var op = planIdx >= 0 && planIdx < plan.Pages.Count ? plan.Pages[planIdx] : null;
            bool cacheMatch = _cachedPlanIndex == planIdx && _cachedReview == _review && _cachedStyle == _style;
            double wd = cacheMatch && _cachedWd > 0 ? _cachedWd : (op?.Width ?? 595);
            double hd = cacheMatch && _cachedHd > 0 ? _cachedHd : (op?.Height ?? 842);
            var placement = PrintLayout.Compute(wd, hd, paper, SelectedFitMode, AutoRotateEnabled);

            _sheet.SetContent(
                plan,
                _review,
                paper,
                _cachedImageableArea,
                _zoom,
                cacheMatch ? _cachedBitmap : null,
                planIdx,
                wd,
                hd,
                placement);

            UpdatePlacementStatus(paper, wd, hd, placement);
            UpdateNavigationUi();
        }

        private void UpdatePlacementStatus(Size paper, double wd, double hd, PrintPlacement placement)
        {
            int paperW = (int)Math.Round(paper.Width * 25.4 / 96.0);
            int paperH = (int)Math.Round(paper.Height * 25.4 / 96.0);
            int pageW = (int)Math.Round(wd * 25.4 / 72.0);
            int pageH = (int)Math.Round(hd * 25.4 / 72.0);
            string orient = paper.Width > paper.Height ? "横" : "縦";
            string scaleStr = Math.Abs(placement.Scale - 1.0) < 0.005
                ? "等倍 (100%)"
                : (placement.Scale > 1.0 ? "拡大 " : "縮小 ") + Math.Round(placement.Scale * 100) + "%";
            string rotStr = placement.Rotated ? "90°回転・" : "";
            _placementText.Text = "用紙: " + orient + " (" + paperW + " × " + paperH + " mm) ・ ページ: " + pageW + " × " + pageH + " mm ・ 配置: " + rotStr + scaleStr;
        }

        private async void RequestRenderCurrentPage()
        {
            var plan = ActivePlan;
            if (_selectedPages.Count == 0 || _previewPos < 0 || _previewPos >= _selectedPages.Count) return;
            int planIdx = _selectedPages[_previewPos];
            if (planIdx < 0 || planIdx >= plan.Pages.Count) return;
            if (_cachedPlanIndex == planIdx && _cachedReview == _review && _cachedStyle == _style && _cachedBitmap != null)
            {
                UpdateSheetLayout();
                return;
            }

            bool review = _review;
            var style = _style;
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            var cts = new CancellationTokenSource();
            _renderCts = cts;
            int ver = ++_renderVersion;
            _renderStateText.Text = "描画中…";

            try
            {
                var (bmp, wd, hd) = await _renderer.RenderPageAsync(plan, planIdx, review, style, PreviewDpi, cts.Token);
                if (ver != _renderVersion || cts.IsCancellationRequested) return;
                _cachedPlanIndex = planIdx;
                _cachedReview = review;
                _cachedStyle = style;
                _cachedBitmap = bmp;
                _cachedWd = wd;
                _cachedHd = hd;
                _renderStateText.Text = "";
                UpdateSheetLayout();
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception)
            {
                if (ver == _renderVersion) _renderStateText.Text = "プレビューを描画できませんでした";
            }
        }

        private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                StepZoom(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (_fitToWindow || (_scroll.ScrollableHeight < 1 && _scroll.ScrollableWidth < 1))
            {
                if (e.Delta < 0) GoToPreviewPos(_previewPos + 1);
                else if (e.Delta > 0) GoToPreviewPos(_previewPos - 1);
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.PageDown || e.Key == Key.Right) { GoToPreviewPos(_previewPos + 1); e.Handled = true; }
            else if (e.Key == Key.PageUp || e.Key == Key.Left) { GoToPreviewPos(_previewPos - 1); e.Handled = true; }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) { StepZoom(1); e.Handled = true; }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { StepZoom(-1); e.Handled = true; }
            else if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _fitToWindow = true; UpdateSheetLayout(); e.Handled = true; }
        }

        // ================================================================== actual printing

        private bool ConfirmReviewWarning()
        {
            if (!_review) return true;
            return MessageBox.Show(
                this,
                "「確認用として印刷」は、原本の内容をそのまま印刷します（黒塗り箇所は半透明の赤で囲むだけで、下の文字は読めます）。\n" +
                "印刷データにも原本の内容が残ります。庁内での確認以外には使わないでください。\n\n印刷しますか？",
                Title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        private void ExecuteDirectPrint()
        {
            CommitCopiesBox();
            if (!_rangeValid || _selectedPages.Count == 0 || SelectedPrinter == null || _printing) return;
            if (!ConfirmReviewWarning()) return;

            var queue = SelectedPrinter.Queue;
            var ticket = BuildPrintTicket(queue);
            var dlg = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
            Size paper = dlg.PrintableAreaWidth > 1 && dlg.PrintableAreaHeight > 1
                ? new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight)
                : CurrentPaperSize;
            RunPrint(dlg, paper, _copies);
        }

        private void ExecuteOsDialogPrint()
        {
            CommitCopiesBox();
            if (!_rangeValid || _selectedPages.Count == 0 || _printing) return;
            if (!ConfirmReviewWarning()) return;

            var dlg = new PrintDialog { UserPageRangeEnabled = false };
            if (SelectedPrinter != null)
            {
                try
                {
                    dlg.PrintQueue = SelectedPrinter.Queue;
                    dlg.PrintTicket = BuildPrintTicket(SelectedPrinter.Queue);
                }
                catch { }
            }
            if (dlg.ShowDialog() != true) return;
            Size paper = dlg.PrintableAreaWidth > 1 && dlg.PrintableAreaHeight > 1
                ? new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight)
                : CurrentPaperSize;
            int copies = dlg.PrintTicket?.CopyCount ?? _copies;
            RunPrint(dlg, paper, Math.Max(1, copies));
        }

        private void RunPrint(PrintDialog dlg, Size paper, int copies)
        {
            var plan = ActivePlan;
            var fit = SelectedFitMode;
            bool autoRotate = AutoRotateEnabled;
            PagePaginator pag = _review
                ? (PagePaginator)new ReviewPaginator(_worker, _doc, plan, _printDpi, paper, _watermark, _selectedPages, fit, autoRotate)
                : new RedactedPaginator(_worker, _doc, plan, _style, _printDpi, paper, _selectedPages, fit, autoRotate);

            _printing = true;
            UpdateSummaryAndButtons();
            _renderStateText.Text = "印刷データを送信しています…";
            var old = Cursor;
            Cursor = Cursors.Wait;
            try
            {
                dlg.PrintDocument(pag, PrintJob.Name);
                PrintedPages = _selectedPages.Count;
                PrintedCopies = copies;
                PrintedReview = _review;
                DialogResult = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is PrintSystemException)
            {
                MessageBox.Show(this, "印刷できませんでした。プリンターの状態や接続を確認してください。", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                MessageBox.Show(this, "印刷できませんでした（" + ex.GetType().Name + "）。", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                pag.Release();
                Cursor = old;
                _printing = false;
                _renderStateText.Text = "";
                UpdateSummaryAndButtons();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
            _renderer.Dispose();
            foreach (var p in _printers) { try { p.Queue.Dispose(); } catch { } }
            _printers.Clear();
            try { _printServer?.Dispose(); } catch { }
            _printServer = null;
        }
    }

    /// <summary>Draws a single preview sheet: white paper, optional printable-margin guide, and the placed page.</summary>
    internal sealed class PrintSheetPreview : FrameworkElement
    {
        private static readonly Pen PaperBorder = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1));
        private static readonly Pen MarginPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(140, 128, 128, 128)), 0.75) { DashStyle = DashStyles.Dash });

        private readonly string _watermark;

        private OutputPlan? _plan;
        private bool _review;
        private Size _paper = new Size(793.7, 1122.5);
        private Rect? _imageable;
        private double _zoom = 1.0;
        private BitmapSource? _bitmap;
        private int _planIndex = -1;
        private double _wd;
        private double _hd;
        private PrintPlacement? _placement;

        public PrintSheetPreview(string watermark)
        {
            _watermark = watermark;
            SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public void SetContent(
            OutputPlan plan,
            bool review,
            Size paper,
            Rect? imageable,
            double zoom,
            BitmapSource? bitmap,
            int planIndex,
            double wd,
            double hd,
            PrintPlacement placement)
        {
            _plan = plan;
            _review = review;
            _paper = paper;
            _imageable = imageable;
            _zoom = zoom;
            _bitmap = bitmap;
            _planIndex = planIndex;
            _wd = wd;
            _hd = hd;
            _placement = placement;
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(Math.Max(1, Math.Ceiling(_paper.Width * _zoom)), Math.Max(1, Math.Ceiling(_paper.Height * _zoom)));

        protected override void OnRender(DrawingContext dc)
        {
            double sw = _paper.Width * _zoom, sh = _paper.Height * _zoom;
            var sheetRect = new Rect(0, 0, sw, sh);
            dc.DrawRectangle(Brushes.White, PaperBorder, sheetRect);

            dc.PushClip(new RectangleGeometry(sheetRect));
            dc.PushTransform(new ScaleTransform(_zoom, _zoom));

            if (_plan != null && _bitmap != null && _placement != null && _planIndex >= 0 && _planIndex < _plan.Pages.Count)
            {
                dc.PushTransform(_placement.Transform);
                dc.DrawImage(_bitmap, new Rect(0, 0, _placement.PageWidthDip, _placement.PageHeightDip));
                if (_review)
                {
                    dc.PushTransform(new ScaleTransform(96.0 / 72, 96.0 / 72));
                    ReviewPaginator.DrawOverlay(dc, _plan, _planIndex, _wd, _hd, _watermark);
                    dc.Pop();
                }
                dc.Pop();
            }

            if (_imageable.HasValue)
            {
                dc.DrawRectangle(null, MarginPen, _imageable.Value);
            }

            dc.Pop();
            dc.Pop();
        }
    }
}
