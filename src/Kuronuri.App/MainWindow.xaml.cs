using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kuronuri.App.Interop;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Export;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Printing;
using Kuronuri.Core.Rendering;
using Kuronuri.Core.Settings;

namespace Kuronuri.App
{
    public partial class MainWindow : Window, IEditorHost
    {
        private const string AppTitle = AppInfo.Name;   // dialog captions; the window title also shows the version
        private const string UnsetLabel = "（未設定）";

        private readonly PdfiumWorker _worker;
        private AppSettings _settings;
        private readonly string _settingsPath;
        private DocumentSession? _session;
        private readonly ViewState _view = new ViewState();
        private Tool _tool = Tool.Hand;   // the default: looking around never changes a redaction (ARCHITECTURE §11.2)
        private double _brushPt = ShapeOps.MmToPt(4);
        private Reason? _currentReason;
        private Guid? _highlight;
        private readonly DispatcherTimer _highlightTimer;
        private readonly PageBitmapCache _pageCache = new PageBitmapCache(PageBitmapCache.DefaultBudget());
        private readonly PageBitmapCache _thumbCache = new PageBitmapCache(64L << 20, 200);
        private bool _syncing;
        private double _lastThumbsWidth = 160, _lastListWidth = 320;
        private bool _busy;
        private ObservableCollection<ItemRow> _rows = new ObservableCollection<ItemRow>();

        public MainWindow(PdfiumWorker worker, AppSettings settings, string settingsPath)
        {
            _worker = worker; _settings = settings; _settingsPath = settingsPath;
            InitializeComponent();
            leftCanvas.Host = this;
            rightCanvas.Host = this;
            rightCanvas.Editable = true;
            itemList.ItemsSource = _rows;
            _highlightTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) =>
            {
                _highlightTimer!.Stop(); _highlight = null; rightCanvas.InvalidateVisual();
            }, Dispatcher);
            _highlightTimer.Stop();

            _view.Changed += (s, e) => { leftCanvas.InvalidateVisual(); rightCanvas.InvalidateVisual(); UpdateScrollBars(); UpdatePinnedPages(); UpdateStatus(); };
            _view.PageChanged += (s, e) => OnPageChanged();
            rightCanvas.SizeChanged += (s, e) => _view.SetViewport(rightCanvas.ActualWidth, rightCanvas.ActualHeight);

            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewKeyUp += Window_PreviewKeyUp;
            Deactivated += (s, e) => { _view.Peek = false; };
            Closing += Window_Closing;
            ApplySettingsToUi();
            styleBlack.IsChecked = _settings.Style == RedactionStyle.Black;
            styleWhite.IsChecked = _settings.Style == RedactionStyle.WhiteWithBorder;
            InitUx();
            UpdateStatus();
        }

        // ================================================================== IEditorHost

        public DocumentSession? Session => _session;
        public ViewState View => _view;
        public Tool Tool => _tool;
        public double BrushPt => _brushPt;
        public Reason? CurrentReason => _currentReason;
        public Guid? HighlightId => _highlight;
        public bool OriginalSameZoom => originalSameZoom.IsChecked == true;

        public void Execute(EditCommand cmd)
        {
            var s = _session;
            if (s == null) return;
            try { s.Undo.Execute(cmd); }
            catch (InvalidOperationException) { Beep(); return; }
            s.PruneSelection();
        }

        public void HandleWheel(PageCanvas source, MouseWheelEventArgs e)
        {
            if (_session == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                var p = e.GetPosition(source);
                _view.ZoomAt(_view.Zoom * Math.Pow(1.2, e.Delta / 120.0), p.X, p.Y);
                return;
            }
            double step = -e.Delta / 120.0 * 60;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { _view.ScrollBy(step, 0); return; }
            if (!_view.Continuous)
            {
                // One page at a time: scrolling on at the edge of the page moves to the next / previous page (ARCHITECTURE §6).
                if (step > 0 && _view.ScrollY >= _view.MaxScrollY - 0.5) { if (_view.PageIndex < _view.PageCount - 1) _view.GoToPage(_view.PageIndex + 1); return; }
                if (step < 0 && _view.ScrollY <= 0.5) { if (_view.PageIndex > 0) _view.GoToPage(_view.PageIndex - 1, toBottom: true); return; }
            }
            _view.ScrollBy(0, step);   // continuous: the column goes on across page edges
        }

        // ================================================================== settings / combos

        private void ApplySettingsToUi()
        {
            _syncing = true;
            try
            {
                brushCombo.Items.Clear();
                foreach (var mm in _settings.BrushWidthsMm) brushCombo.Items.Add(new ComboBoxItem { Content = mm.ToString("0.#") + " mm", Tag = mm });
                double curMm = ShapeOps.PtToMm(_brushPt);
                int best = 0;
                for (int i = 0; i < _settings.BrushWidthsMm.Count; i++)
                    if (Math.Abs(_settings.BrushWidthsMm[i] - curMm) < Math.Abs(_settings.BrushWidthsMm[best] - curMm)) best = i;
                brushCombo.SelectedIndex = best;
                _brushPt = ShapeOps.MmToPt(_settings.BrushWidthsMm[best]);

                FillReasonCombo(currentReasonCombo);
                currentReasonCombo.SelectedIndex = IndexOfPreset(_currentReason);
                FillReasonCombo(reasonEditCombo);
            }
            finally { _syncing = false; }
            UpdateReasonEditor();
        }

        private void FillReasonCombo(ComboBox combo)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = UnsetLabel, Tag = null });
            foreach (var p in _settings.Reasons) combo.Items.Add(new ComboBoxItem { Content = p.ToString(), Tag = p });
        }

        private int IndexOfPreset(Reason? r)
        {
            if (r == null) return 0;
            for (int i = 0; i < _settings.Reasons.Count; i++)
                if (_settings.Reasons[i].Code == r.Code && _settings.Reasons[i].Label == r.Label) return i + 1;
            return -1;
        }

        private void Brush_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || brushCombo.SelectedItem is not ComboBoxItem it || it.Tag is not double mm) return;
            _brushPt = ShapeOps.MmToPt(mm);
        }

        private void StepBrush(int dir)
        {
            int i = Math.Max(0, Math.Min(brushCombo.Items.Count - 1, brushCombo.SelectedIndex + dir));
            brushCombo.SelectedIndex = i;
        }

        private void CurrentReason_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            _currentReason = (currentReasonCombo.SelectedItem as ComboBoxItem)?.Tag is ReasonPreset p ? p.ToReason() : null;
        }

        // ================================================================== tools / style / panes

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string t && Enum.TryParse(t, out Tool tool)) SetTool(tool);
        }

        private void SetTool(Tool tool)
        {
            rightCanvas.CancelDrag();
            _tool = tool;
            _session?.Grouper.Close();
            var target = tool switch { Tool.Select => toolSelect, Tool.Rect => toolRect, Tool.Text => toolText, Tool.Freehand => toolFree, Tool.Eraser => toolEraser, _ => toolHand };
            if (tool == Tool.Text && _session != null) PreloadText(_session.BaseOf(_view.PageIndex));
            stHint.Text = ToolHint(tool);
            if (target.IsChecked != true) target.IsChecked = true;
            leftCanvas.Cursor = null;   // the original pane shows the hand only with the hand tool; refreshed on the next move
            rightCanvas.Cursor = null;
            rightCanvas.InvalidateVisual();
        }

        private void Style_Checked(object sender, RoutedEventArgs e)
        {
            if (_session == null || _syncing) return;
            _session.Style = styleWhite.IsChecked == true ? RedactionStyle.WhiteWithBorder : RedactionStyle.Black;
            rightCanvas.InvalidateVisual();
            UpdateStatus();
        }

        private void OriginalSameZoom_Changed(object sender, RoutedEventArgs e) => leftCanvas?.InvalidateVisual();

        private void Continuous_Changed(object sender, RoutedEventArgs e)
        {
            if (continuousPages == null) return;   // while the window is being built
            _view.SetContinuous(continuousPages.IsChecked == true);
        }

        private void Panes_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePanes();
        }

        private void TogglePanes_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (twoPane.IsChecked == true && _autoOnePane)
            {
                Notify(InfoSeverity.Info, "ウィンドウの幅が足りないため、原本と並べて表示できません",
                    "ウィンドウを広げるか、ページ一覧・箇所リストを狭く（または「表示」から非表示に）すると、原本と並べて表示します。");
                return;
            }
            twoPane.IsChecked = !twoPane.IsChecked;
        }

        private void Panels_Changed(object sender, RoutedEventArgs e)
        {
            if (thumbList == null) return;
            bool t = showThumbs.IsChecked == true, l = showList.IsChecked == true;
            thumbList.Visibility = split1.Visibility = t ? Visibility.Visible : Visibility.Collapsed;
            colThumbs.MinWidth = t ? MinThumbsWidth : 0;
            colSplit1.Width = t ? new GridLength(4) : new GridLength(0);
            sidePanel.Visibility = split2.Visibility = l ? Visibility.Visible : Visibility.Collapsed;
            colList.MinWidth = l ? MinListWidth : 0;
            colSplit2.Width = l ? new GridLength(4) : new GridLength(0);
            ApplySideWidths();   // the chosen widths (_lastThumbsWidth / _lastListWidth), narrowed while the window is small
            QueueUpdatePanes();
        }

        // ================================================================== keyboard

        private static bool IsTextInput(object? focused) => focused is TextBox || focused is PasswordBox || (focused is ComboBox cb && cb.IsEditable);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTextInput(Keyboard.FocusedElement)) return;
            var mods = Keyboard.Modifiers;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Space && mods == ModifierKeys.None)
            {
                if (_session != null) _view.Peek = true;
                e.Handled = true;
                return;
            }
            var focused = Keyboard.FocusedElement;
            bool inSelector = focused is ComboBox || focused is ComboBoxItem || focused is ListBoxItem || focused is CheckBox;
            bool arrow = key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down;

            // Ctrl+A: every item on the current page (a list keeps its own Ctrl+A).
            if (key == Key.A && mods == ModifierKeys.Control && !inSelector && _session != null)
            {
                _session.Select(_session.Items.Items.Where(i => i.PageIndex == _session.BaseOf(_view.PageIndex)).Select(i => i.Id).ToList());
                e.Handled = true;
                return;
            }
            // Arrows: nudge the selection by 1pt (Shift: 10pt); without a selection they scroll the page.
            if (arrow && (mods == ModifierKeys.None || mods == ModifierKeys.Shift) && !inSelector && _session != null)
            {
                double step = mods == ModifierKeys.Shift ? 10 : 1;
                double dx = key == Key.Left ? -step : key == Key.Right ? step : 0, dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                if (!Nudge(dx, dy)) _view.ScrollBy(dx * 40 / step, dy * 40 / step);
                e.Handled = true;
                return;
            }
            if (mods != ModifierKeys.None) return;
            if (inSelector && (key == Key.Enter || key == Key.Escape || key == Key.Up || key == Key.Down || key == Key.Home ||
                               key == Key.End || key == Key.PageUp || key == Key.PageDown))
                return;
            switch (key)
            {
                case Key.H: SetTool(Tool.Hand); break;
                case Key.V: SetTool(Tool.Select); break;
                case Key.R: SetTool(Tool.Rect); break;
                case Key.B: SetTool(Tool.Freehand); break;
                case Key.E: SetTool(Tool.Eraser); break;
                case Key.T: SetTool(Tool.Text); break;
                case Key.OemOpenBrackets: StepBrush(-1); break;
                case Key.OemCloseBrackets: StepBrush(+1); break;
                case Key.Delete: if (_session?.SelectedIds.Count > 0) DeleteSelection(); break;
                case Key.PageDown: if (_session != null) _view.GoToPage(_view.PageIndex + 1); break;
                case Key.PageUp: if (_session != null) _view.GoToPage(_view.PageIndex - 1); break;
                case Key.Home: if (_session != null) _view.GoToPage(0); break;
                case Key.End: if (_session != null) _view.GoToPage(_view.PageCount - 1); break;
                case Key.Enter:
                case Key.Escape:
                    rightCanvas.CancelDrag();
                    _session?.Grouper.Close();
                    break;
                default: return;
            }
            e.Handled = true;
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { _view.Peek = false; if (!IsTextInput(Keyboard.FocusedElement)) e.Handled = true; }
        }

        // ================================================================== open

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_busy || !ConfirmDiscard()) return;
            string? path = FileDialogs.Open(this, "PDF を開く");
            if (path == null) return;
            OpenPath(path);
        }

        public void OpenPath(string path)
        {
            DocumentSession? loaded = null;
            try
            {
                loaded = RunWithProgress("開いています…", null, token => _worker.Run(() => DocumentSession.Load(_worker, path, _settings.Style), token));
            }
            catch (PdfLoadException ex) { Error(PdfLoadException.Describe(ex.Kind)); return; }
            catch (ReviewFileCorruptException) { Error("確認用PDFを開けませんでした。中に埋め込まれた原本が保存時の記録と一致しません（ファイルが壊れているか、書き換えられています）。\n\n別の確認用PDF、または元の原本PDFを開いてください。"); return; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error("ファイルを開けませんでした。別のアプリで開いていないか、読み取りできる場所にあるか確認してください。"); return; }
            catch (Exception ex) when (Recoverable(ex)) { UnexpectedError("PDFを開けませんでした", ex); return; }
            if (loaded == null) return;
            SetSession(loaded);

            var w = loaded.Warnings;
            var notes = new List<string>();
            if (w.Xfa) notes.Add("XFA フォームを含むため、内容が正しく表示されない可能性があります。画面と原本を見比べて確認してください。");
            if ((w.HasAttachments || w.HasBookmarks) && loaded.Kind == SourceKind.Original)
                notes.Add((w.HasAttachments ? "添付ファイル" : "") + (w.HasAttachments && w.HasBookmarks ? "・" : "") + (w.HasBookmarks ? "しおり" : "") +
                          "は開示用PDFには含まれません（ページの内容だけが入ります）。");
            if (notes.Count > 0) Notify(InfoSeverity.Warning, "この PDF についてのお知らせ", string.Join("\n", notes));
            else if (loaded.Kind == SourceKind.Review) Notify(InfoSeverity.Info, "確認用PDFを開きました", "保存したときの黒塗り箇所と理由から続きを作業できます。");
        }

        private void SetSession(DocumentSession s)
        {
            var old = _session;
            _session = null;
            if (old != null) { old.Items.Changed -= Items_Changed; old.SelectionChanged -= Selection_Changed; old.Undo.StateChanged -= Undo_StateChanged; old.Dispose(); }
            _pageCache.Clear(); _thumbCache.Clear();
            _pinned = (-1, -1);

            s.Renderer = new PageRenderService(_worker, s.Doc, _pageCache, _thumbCache);
            _session = s;
            s.Items.Changed += Items_Changed;
            s.SelectionChanged += Selection_Changed;
            s.Undo.StateChanged += Undo_StateChanged;

            _syncing = true;
            styleBlack.IsChecked = s.Style == RedactionStyle.Black;
            styleWhite.IsChecked = s.Style == RedactionStyle.WhiteWithBorder;
            _syncing = false;

            ClearSearch();
            startScreen.Visibility = Visibility.Collapsed;
            infoBar.Hide();
            _view.SetViewport(rightCanvas.ActualWidth, rightCanvas.ActualHeight);
            _view.Reset(s.Pages.Count, s.ViewSize);
            _shownLayout = null;
            BuildThumbs();
            UpdateExcludedBanner();
            RebuildList();
            UpdateReasonEditor();
            UpdateStatus();
            CommandManager.InvalidateRequerySuggested();
        }

        private void Items_Changed(object? sender, EventArgs e)
        {
            SyncLayout();   // the page layout is part of the undoable state (ARCHITECTURE §5)
            ItemsChangedCore();
        }

        private void ItemsChangedCore()
        {
            RebuildList();
            UpdateThumbBadges();
            RefreshSearchCoverage();
            leftCanvas.InvalidateVisual();
            rightCanvas.InvalidateVisual();
            UpdateStatus();
        }

        private void Undo_StateChanged(object? sender, EventArgs e) => UpdateStatus();

        private void Selection_Changed(object? sender, EventArgs e)
        {
            SyncListSelection();
            UpdateReasonEditor();
            rightCanvas.InvalidateVisual();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnPageChanged()
        {
            var s = _session;
            if (s == null) return;
            s.Grouper.Close();
            int p = _view.PageIndex;   // view position; the cache and the text are keyed by original page numbers
            if (_tool == Tool.Text) PreloadText(s.BaseOf(p));
            UpdateExcludedBanner();
            if (thumbList.SelectedIndex != p)
            {
                _syncing = true;
                thumbList.SelectedIndex = p;
                _syncing = false;
            }
            if (thumbList.Items.Count > p) thumbList.ScrollIntoView(thumbList.Items[p]);
            // Neighbours are rendered once the user stops on a page; requests for pages already passed are dropped.
            _prefetchCts?.Cancel();
            _prefetchCts = new CancellationTokenSource();
            var token = _prefetchCts.Token;
            Dispatcher.BeginInvoke(new Action(() => { Prefetch(p - 1, token); Prefetch(p + 1, token); }), DispatcherPriority.ApplicationIdle);
        }

        private CancellationTokenSource? _prefetchCts;
        private (int First, int Last) _pinned = (-1, -1);

        /// <summary>The cache keeps the visible pages and one page before and after them (ARCHITECTURE §6).</summary>
        private void UpdatePinnedPages()
        {
            var s = _session;
            if (s == null) return;
            var (first, last) = _view.VisiblePages();
            first = Math.Max(0, first - 1); last = Math.Min(s.Pages.Count - 1, last + 1);
            if (_pinned == (first, last)) return;
            _pinned = (first, last);
            _pageCache.SetPinnedPages(Enumerable.Range(first, Math.Max(0, last - first + 1)).Select(s.BaseOf));
        }

        /// <param name="view">View position of the neighbouring page.</param>
        private void Prefetch(int view, CancellationToken token)
        {
            var s = _session;
            if (token.IsCancellationRequested || s?.Renderer == null || view < 0 || view >= s.Pages.Count) return;
            double dps = _view.DipPerPt * VisualTreeHelper.GetDpi(this).DpiScaleX;
            var (vw, vh) = s.ViewSize(view);
            PageRenderer.PixelSize(vw, vh, 72 * dps, long.MaxValue, out int w, out int h, out _);
            if ((long)w * h > PageRenderService.FullRenderLimitPixels) return;
            _ = s.Renderer.RenderAsync(RenderKey.Full(s.BaseOf(view), w, h, s.RotationOfView(view)), token).ContinueWith(t => { var _ = t.Exception; }, TaskScheduler.Default);
        }

        private void Thumb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _session == null || thumbList.SelectedIndex < 0) return;
            _view.GoToPage(thumbList.SelectedIndex);
        }

        // ================================================================== item list

        private void RebuildList()
        {
            var s = _session;
            var rows = new ObservableCollection<ItemRow>();
            if (s != null)
            {
                var ordered = Kuronuri.Core.Geometry.CoordMap.InDisplayOrder(s.Items.Items, s.Layout, s.Pages);
                for (int i = 0; i < ordered.Count; i++) rows.Add(new ItemRow(ordered[i], i + 1, s.ViewOf(ordered[i].PageIndex) + 1));
            }
            _rows = rows;
            _syncing = true;
            try { itemList.ItemsSource = rows; }
            finally { _syncing = false; }
            SyncListSelection();
            UpdateReasonEditor();
        }

        private void SyncListSelection()
        {
            var s = _session;
            _syncing = true;
            try
            {
                itemList.SelectedItems.Clear();
                if (s == null) return;
                ItemRow? first = null;
                foreach (var r in _rows)
                    if (s.IsSelected(r.Id)) { itemList.SelectedItems.Add(r); first ??= r; }
                if (first != null) itemList.ScrollIntoView(first);
            }
            finally { _syncing = false; }
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _session == null) return;
            _session.Select(itemList.SelectedItems.Cast<ItemRow>().Select(r => r.Id).ToList());
        }

        private void ItemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0) return;
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListViewItem)) dep = VisualTreeHelper.GetParent(dep);
            if (dep is ListViewItem lvi && lvi.DataContext is ItemRow row) JumpTo(row.Item);
        }

        /// <summary>Shows the item centred on its page and highlights it for one second.</summary>
        private void JumpTo(RedactionItem item)
        {
            var s = _session;
            if (s == null) return;
            CenterOnBase(item.PageIndex, ShapeOps.Box(item));
            _highlight = item.Id;
            _highlightTimer.Stop(); _highlightTimer.Start();
            rightCanvas.InvalidateVisual();
        }

        private void UpdateReasonEditor()
        {
            var s = _session;
            var sel = s?.SelectedItems ?? new List<RedactionItem>();
            bool any = sel.Count > 0;
            reasonEditCombo.IsEnabled = freeTextEdit.IsEnabled = any;
            reasonEditTitle.Text = any ? "選択した箇所の理由（" + sel.Count + " 件）" : "選択した箇所の理由";
            _syncing = true;
            try
            {
                if (!any) { reasonEditCombo.SelectedIndex = -1; freeTextEdit.Text = ""; return; }
                var reasons = sel.Select(i => i.Reason).ToList();
                bool sameHead = reasons.All(r => (r?.Code, r?.Label) == (reasons[0]?.Code, reasons[0]?.Label));
                reasonEditCombo.SelectedIndex = sameHead ? IndexOfPreset(reasons[0] is { } r0 && r0.Code.Length > 0 ? r0 : null) : -1;
                bool sameText = reasons.All(r => (r?.FreeText ?? "") == (reasons[0]?.FreeText ?? ""));
                freeTextEdit.Text = sameText ? reasons[0]?.FreeText ?? "" : "";
            }
            finally { _syncing = false; }
        }

        private void ReasonEdit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = _session;
            if (_syncing || s == null || reasonEditCombo.SelectedItem is not ComboBoxItem it) return;
            var preset = it.Tag as ReasonPreset;
            var parts = new List<EditCommand>();
            foreach (var item in s.SelectedItems)
            {
                string text = item.Reason?.FreeText ?? "";
                Reason? next = preset != null ? preset.ToReason(text) : (text.Length > 0 ? new Reason("", "", text) : null);
                if (next != item.Reason) parts.Add(new ReplaceItemCommand(item, item with { Reason = next }));
            }
            if (parts.Count > 0) Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts));
        }

        private void FreeText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitFreeText(); e.Handled = true; }
        }

        private void FreeText_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitFreeText();

        /// <summary>The free text is committed as one command when the box loses focus (or on Enter).</summary>
        private void CommitFreeText()
        {
            var s = _session;
            if (_syncing || s == null || !freeTextEdit.IsEnabled) return;
            string text = freeTextEdit.Text.Trim();
            var parts = new List<EditCommand>();
            foreach (var item in s.SelectedItems)
            {
                Reason? next = item.Reason == null
                    ? (text.Length > 0 ? new Reason("", "", text) : null)
                    : (item.Reason.Code.Length == 0 && item.Reason.Label.Length == 0 && text.Length == 0 ? null : item.Reason with { FreeText = text });
                if (next != item.Reason) parts.Add(new ReplaceItemCommand(item, item with { Reason = next }));
            }
            if (parts.Count > 0) Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts));
        }

        // ================================================================== edit commands

        private void HasDoc_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session != null && !_busy;
        private void HasSelection_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.SelectedIds.Count > 0 && !_busy;
        private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.Undo.CanUndo == true && !_busy;
        private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.Undo.CanRedo == true && !_busy;
        private void Merge_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session != null && EditOps.CanMerge(_session.SelectedItems.ToList());
        private void Split_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
            e.CanExecute = _session?.SelectedItems is { Count: 1 } sel && EditOps.CanSplit(sel[0]);

        private void Undo_Executed(object sender, ExecutedRoutedEventArgs e) => AfterHistory(_session?.Undo.Undo(), undo: true);
        private void Redo_Executed(object sender, ExecutedRoutedEventArgs e) => AfterHistory(_session?.Undo.Redo(), undo: false);

        /// <summary>After undo / redo: select the affected items and go to their page (ARCHITECTURE §5).</summary>
        private void AfterHistory(EditCommand? cmd, bool undo)
        {
            var s = _session;
            if (s == null || cmd == null) return;
            rightCanvas.CancelDrag();
            s.Grouper.Close();
            var items = (undo ? cmd.ItemsBefore : cmd.ItemsAfter).ToList();
            s.Select(items.Select(i => i.Id));
            s.PruneSelection();
            var first = items.OrderBy(i => s.ViewOf(i.PageIndex)).FirstOrDefault();
            if (first != null && !_view.IsVisible(s.ViewOf(first.PageIndex))) _view.GoToPage(s.ViewOf(first.PageIndex));
            else if (first == null)
            {
                // Removal undone by redo of an add, etc.: show the page of what disappeared.
                var gone = (undo ? cmd.ItemsAfter : cmd.ItemsBefore).FirstOrDefault();
                if (gone != null && !_view.IsVisible(s.ViewOf(gone.PageIndex))) _view.GoToPage(s.ViewOf(gone.PageIndex));
            }
        }

        private void Merge_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var s = _session;
            if (s == null) return;
            var sel = s.SelectedItems.ToList();
            if (!EditOps.CanMerge(sel)) { Notify(InfoSeverity.Info, "結合できません", "結合できるのは、同じページにある2つ以上の箇所です。Shift か Ctrl を押しながらクリックして選んでください。"); return; }
            var cmd = EditOps.Merge(sel);
            Execute(cmd);
            s.Select(cmd.ItemsAfter.Select(i => i.Id));
        }

        private void Split_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var s = _session;
            if (s == null || s.SelectedItems is not { Count: 1 } sel || !EditOps.CanSplit(sel[0])) return;
            var cmd = EditOps.Split(s.Items, sel[0]);
            Execute(cmd);
            s.Select(cmd.ItemsAfter.Select(i => i.Id));
        }

        private void Delete_Executed(object sender, ExecutedRoutedEventArgs e) => DeleteSelection();

        private void DeleteSelection()
        {
            var s = _session;
            if (s == null) return;
            var cmd = EditOps.Delete(s.SelectedItems.ToList());
            if (cmd != null) Execute(cmd);
            s.Select(new Guid[0]);
        }

        // ================================================================== zoom

        private static readonly double[] ZoomSteps = { 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8 };

        private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) =>
            _view.SetZoom(ZoomSteps.FirstOrDefault(z => z > _view.Zoom + 1e-3) is var z && z > 0 ? z : ViewState.MaxZoom);

        private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) =>
            _view.SetZoom(ZoomSteps.LastOrDefault(z => z < _view.Zoom - 1e-3) is var z && z > 0 ? z : ViewState.MinZoom);

        private void Zoom100_Executed(object sender, ExecutedRoutedEventArgs e) => _view.SetZoom(1);
        private void FitWidth_Executed(object sender, ExecutedRoutedEventArgs e) => _view.SetFit(FitMode.Width);
        private void FitPage_Executed(object sender, ExecutedRoutedEventArgs e) => _view.SetFit(FitMode.Page);

        // ================================================================== save (review PDF)

        private void SaveReview_Executed(object sender, ExecutedRoutedEventArgs e) => SaveReview(saveAs: false);
        private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveReview(saveAs: true);

        /// <returns>true when saved.</returns>
        private bool SaveReview(bool saveAs)
        {
            var s = _session;
            if (s == null || _busy) return false;
            rightCanvas.CancelDrag();
            CommitFreeText();
            string? dest = saveAs || s.ReviewPath == null ? null : s.ReviewPath;   // an original is always "save as"
            if (dest == null)
            {
                dest = FileDialogs.Save(this, "確認用PDFに保存", s.Folder, ReviewFile.DefaultReviewName(s.DisplayFileName));
                if (dest == null) return false;
            }
            if (!SavePathGuard.IsAllowed(dest, s.ProtectedFiles)) { Error("原本のPDFには上書きできません（原本は書き換えない決まりです）。別の名前を指定してください。"); return false; }
            if (s.Original.Length > DocumentSession.LargeOriginalBytes &&
                !Confirm("原本が 300MB を超えています。確認用PDFは原本を丸ごと含むため、サイズが約2倍になり、保存中に多くのメモリを使います。続けますか？"))
                return false;

            var project = s.ToProject();
            ReviewSaveResult res;
            try
            {
                res = RunWithProgress("確認用PDFを保存しています…", null, token => _worker.Run(() =>
                {
                    byte[] original = s.ReadOriginalBytes();
                    try { return ReviewFile.Save(original, project, DocumentSession.AppVersion, dest, s.ProtectedFiles); }
                    finally { if (!ReferenceEquals(original, s.OriginalBytes)) Array.Clear(original, 0, original.Length); }
                }, token));
            }
            catch (ForbiddenDestinationException) { Error("原本のPDFには上書きできません。別の名前を指定してください。"); return false; }
            catch (OriginalChangedException) { Error("確認用PDFに保存できませんでした。開いた後に原本のPDFが変更・移動されています。\n\n原本を元に戻すか、開き直してから作業してください。"); return false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error("保存できませんでした。書き込みできない場所か、ファイルが別のアプリで開かれています。\n\n別の場所を選ぶか、ファイルを閉じてからやり直してください。"); return false; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is PdfLoadException) { Error("確認用PDFを作成できませんでした。もう一度やり直してください。"); return false; }
            catch (Exception ex) when (Recoverable(ex)) { UnexpectedError("確認用PDFに保存できませんでした", ex); return false; }

            s.ReviewPath = dest;
            s.MarkSaved();
            UpdateStatus();
            if (res.Method == ReviewSaveMethod.Raster)
                Notify(InfoSeverity.Warning, "確認用PDFを画像方式で保存しました: " + Path.GetFileName(dest),
                    "この文書はページをそのまま取り込めなかったため、ページを画像にして保存しました。原本と黒塗り箇所は通常どおり保存されています（確認用PDFの中で文字の検索はできません）。");
            else
                Notify(InfoSeverity.Success, "確認用PDFに保存しました: " + Path.GetFileName(dest), "このファイルを開くと、続きから作業できます。外部には渡さないでください。");
            return true;
        }

        // ================================================================== final output

        private void Export_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var s = _session;
            if (s == null || _busy) return;
            rightCanvas.CancelDrag();
            CommitFreeText();
            int unset = s.Items.Items.Count(i => i.Reason == null);
            if (s.Items.Count == 0 && !Confirm("黒塗り箇所が1つもありません。\nこのまま開示用PDFを作成しますか？（全ページがそのまま画像になります）")) return;
            if (unset > 0 && !Confirm("理由が設定されていない箇所が " + unset + " 件あります。\nこのまま開示用PDFを作成しますか？\n\n（「キャンセル」して F8 キーで未設定の箇所を順に確認できます）")) return;
            int excluded = s.Pages.Count - s.Layout.IncludedCount;
            if (excluded > 0 && !Confirm(excluded + " ページを開示用PDFから除いて作成します（全 " + s.Pages.Count + " ページ中 " + s.Layout.IncludedCount + " ページを出力）。\nよろしいですか？")) return;
            string? dest = FileDialogs.Save(this, "開示用PDFを作成", s.Folder, ReviewFile.DefaultFinalName(s.DisplayFileName));
            if (dest == null) return;
            if (!SavePathGuard.IsAllowed(dest, s.ProtectedFilesForFinal)) { Error("原本のPDFや、開いている確認用PDFには上書きできません。別の名前を指定してください。"); return; }

            var items = s.Items.Items.ToList();
            var plan = OutputPlan.Build(s.Pages, items, s.Layout, includeExcluded: false);   // order / rotation / exclusion (ARCHITECTURE §4.4)
            var style = s.Style;
            var opt = new FinalExportOptions { Dpi = _settings.ExportDpi, JpegQuality = _settings.JpegQuality };
            FinalOutputResult res;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                res = RunWithProgress("開示用PDFを作成しています…", plan.Pages.Count, (token, progress) => _worker.Run(() =>
                    FinalOutput.Write(s.Doc, plan, style, dest, s.ProtectedFilesForFinal, opt, (d, n) => progress.Report(d), token), token));
            }
            catch (OperationCanceledException) { Notify(InfoSeverity.Info, "開示用PDFの作成を中止しました", "ファイルは作成されていません。"); return; }
            catch (OutputVerificationException) { Error("開示用PDFの検証に合格しなかったため、作成したファイルを削除しました。\n（黒塗りが正しく焼き込まれていないか、想定外のデータが含まれていました）\n\nもう一度作成してください。同じ結果になる場合は原本のPDFを作り直してください。"); return; }
            catch (ForbiddenDestinationException) { Error("原本のPDFや、開いている確認用PDFには上書きできません。別の名前を指定してください。"); return; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error("開示用PDFを作成できませんでした。書き込みできない場所か、同じ名前のファイルが別のアプリで開かれています。\n\n別の場所を選ぶか、ファイルを閉じてからやり直してください。"); return; }
            catch (InvalidOperationException) { Error("開示用PDFの作成中にエラーが発生しました。ファイルは作成されていません。もう一度やり直してください。"); return; }
            catch (Exception ex) when (Recoverable(ex)) { UnexpectedError("開示用PDFを作成できませんでした", ex); return; }
            sw.Stop();

            string msg = plan.Pages.Count + " ページ・" + FormatBytes(res.FileBytes) + "・" + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0") + " 秒。黒塗りが焼き込まれ、元の文字や情報が残っていないことを確認済みです。";
            var severity = InfoSeverity.Success;
            if (res.ReducedPages.Count > 0)
            {
                severity = InfoSeverity.Warning;
                msg += "\n大きすぎるページは解像度を下げて作成しました: " +
                       string.Join(", ", res.ReducedPages.Take(10).Select(i => (s.ViewOf(res.ReducedBasePages[res.ReducedPages.IndexOf(i)]) + 1) + "ページ（約" + Math.Round(res.Pages[i].EffectiveDpi) + "dpi）")) +
                       (res.ReducedPages.Count > 10 ? " ほか" : "");
            }
            string created = dest;
            Notify(severity, "開示用PDFを作成しました: " + Path.GetFileName(created), msg, "開いて確認", () => { if (ConfirmDiscard()) OpenPath(created); });
        }

        private static string FormatBytes(long b) => b >= 1 << 20 ? (b / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB";

        // ================================================================== print

        private void PrintRedacted_Executed(object sender, ExecutedRoutedEventArgs e) => Print(review: false);

        private void PrintReview_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!Confirm("「確認用として印刷」は、原本の内容をそのまま印刷します（黒塗り箇所は半透明の赤で囲むだけで、下の文字は読めます）。\n" +
                         "印刷データにも原本の内容が残ります。庁内での確認以外には使わないでください。\n\n印刷しますか？", MessageBoxImage.Warning))
                return;
            Print(review: true);
        }

        private void Print(bool review)
        {
            var s = _session;
            if (s == null || _busy) return;
            rightCanvas.CancelDrag();
            var dlg = new PrintDialog { UserPageRangeEnabled = false };
            if (dlg.ShowDialog() != true) return;
            var paper = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
            PagePaginator pag = review
                ? new ReviewPaginator(_worker, s.Doc, OutputPlan.Build(s.Pages, s.Items.Items.ToList(), s.Layout, includeExcluded: true), _settings.PrintDpi, paper, new HybridOptions().Watermark)
                : new RedactedPaginator(_worker, s.Doc, OutputPlan.Build(s.Pages, s.Items.Items.ToList(), s.Layout, includeExcluded: false), s.Style, _settings.PrintDpi, paper);
            _busy = true;
            var old = Cursor;
            Cursor = Cursors.Wait;
            try { dlg.PrintDocument(pag, PrintJob.Name); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Printing.PrintSystemException) { Error("印刷できませんでした。"); }
            catch (Exception ex) when (Recoverable(ex)) { UnexpectedError("印刷できませんでした", ex); }
            finally
            {
                pag.Release();
                Cursor = old;
                _busy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // ================================================================== settings

        private void Settings_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings.Clone()) { Owner = this };
            if (win.ShowDialog() != true) return;
            var layout = _settings.Window;
            _settings = win.Result.Normalize();
            _settings.Window = layout;
            if (_settings.Theme != Themes.ThemeManager.Current) Themes.ThemeManager.Apply(_settings.Theme);
            if (!SettingsStore.TrySave(_settingsPath, _settings))
                Notify(InfoSeverity.Warning, "設定は今回だけ有効です", "設定ファイル（settings.json）を書き込めない場所にあるため、次回の起動では元に戻ります。");
            ApplySettingsToUi();
        }

        // ================================================================== closing

        /// <returns>true when it is fine to drop the current document.</returns>
        private bool ConfirmDiscard()
        {
            var s = _session;
            if (s == null || !s.IsModified) return true;
            CommitFreeText();
            var r = MessageBox.Show(this, "変更が保存されていません。確認用PDFに保存しますか？\n\n「いいえ」を選ぶと、保存していない変更は失われます。", AppTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.No) return true;
            return SaveReview(saveAs: false);
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_busy) { e.Cancel = true; return; }
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            SaveWindowLayout();
            var s = _session;
            _session = null;
            s?.Dispose();
        }

        // ================================================================== status

        private void UpdateStatus()
        {
            var s = _session;
            if (styleButton != null) UpdateStyleButton();
            if (s == null)
            {
                if (!pageBox.IsKeyboardFocusWithin) pageBox.Text = "";
                pageCountText.Text = "/ -";
                if (!zoomBox.IsKeyboardFocusWithin) zoomBox.Text = "";
                stItems.Text = "箇所 0"; stUnset.Text = "理由未設定 0";
                stState.Text = "ファイルが開かれていません";
                Title = AppInfo.NameWithVersion;
                return;
            }
            if (!pageBox.IsKeyboardFocusWithin) pageBox.Text = (_view.PageIndex + 1).ToString();
            pageCountText.Text = "/ " + s.Pages.Count;
            if (!zoomBox.IsKeyboardFocusWithin) { _syncing = true; zoomBox.SelectedIndex = -1; zoomBox.Text = Math.Round(_view.Zoom * 100) + "%"; _syncing = false; }
            stItems.Text = "箇所 " + s.Items.Count;
            int unset = s.Items.Items.Count(i => i.Reason == null);
            stUnset.Text = "理由未設定 " + unset;
            stUnset.Foreground = unset > 0 ? Brushes.DarkOrange : Brushes.Black;
            stState.Text = s.ReviewPath != null
                ? "確認用PDF" + (s.IsModified ? "・変更あり（未保存）" : "・保存済み")
                : "原本" + (s.IsModified ? "・変更あり（未保存）" : "・変更なし");
            Title = AppInfo.NameWithVersion + " - " + Path.GetFileName(s.ReviewPath ?? s.OriginalPath ?? s.DisplayFileName) + (s.IsModified ? " *" : "");
        }

        // ================================================================== helpers

        /// <summary>Runs work on the worker while a modal progress window keeps the UI responsive and blocks other actions.</summary>
        private T RunWithProgress<T>(string title, int? total, Func<CancellationToken, Task<T>> start) =>
            RunWithProgress(title, total, (token, progress) => start(token));

        private T RunWithProgress<T>(string title, int? total, Func<CancellationToken, IProgress<int>, Task<T>> start)
        {
            _busy = true;
            CommandManager.InvalidateRequerySuggested();
            var win = new ProgressWindow(title, total) { Owner = this };
            using var cts = new CancellationTokenSource();
            win.CancelRequested += (s, e) => cts.Cancel();
            var progress = new Progress<int>(win.SetProgress);
            Task<T> task;
            try { task = start(cts.Token, progress); }
            catch { _busy = false; throw; }
            task.ContinueWith(_ => win.CloseFromTask(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            if (!task.IsCompleted) win.ShowDialog();
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
            return task.GetAwaiter().GetResult();
        }

        private void Error(string msg) => MessageBox.Show(this, msg, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        private void Info(string msg) => MessageBox.Show(this, msg, AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        private bool Confirm(string msg, MessageBoxImage icon = MessageBoxImage.Question) =>
            MessageBox.Show(this, msg, AppTitle, MessageBoxButton.OKCancel, icon) == MessageBoxResult.OK;
        private static void Beep() => System.Media.SystemSounds.Beep.Play();
    }
}
