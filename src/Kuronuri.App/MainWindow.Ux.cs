using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Model;
using Kuronuri.Core.Settings;
using Kuronuri.Core.Text;
using TextSearch = Kuronuri.Core.Text.TextSearch;

namespace Kuronuri.App
{
    /// <summary>Usability additions (ARCHITECTURE §11.1, §11.2, §11.5, §11.6, §12 window layout).</summary>
    public partial class MainWindow
    {
        private List<ThumbVM> _thumbs = new List<ThumbVM>();
        private ObservableCollection<SearchHitVM> _searchRows = new ObservableCollection<SearchHitVM>();
        private IReadOnlyList<RectShape> _searchHighlight = new RectShape[0];
        private int _searchHighlightPage = -1;
        private DispatcherTimer? _hintTimer;

        private void InitUx()
        {
            Themes.ThemeManager.Attach(this);
            Themes.ThemeManager.Changed += (s, e) => { leftCanvas.InvalidateVisual(); rightCanvas.InvalidateVisual(); UpdateThemeChecks(); };
            UpdateThemeChecks();
            searchList.ItemsSource = _searchRows;
            presetCombo.Items.Add(new ComboBoxItem { Content = "定型から選ぶ…", Tag = null });
            foreach (var p in TextSearch.Presets) presetCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Pattern });
            presetCombo.SelectedIndex = 0;
            stHint.Text = ToolHint(_tool);
            ApplyWindowLayout();
            UpdateScrollBars();
        }

        // ================================================================== IEditorHost additions

        public PageText? TextFor(int page)
        {
            var s = _session;
            if (s == null) return null;
            var t = s.CachedText(page);
            if (t == null) PreloadText(page);
            return t;
        }

        public IReadOnlyList<RectShape> SearchHighlight => _session != null && _searchHighlightPage == CurrentBase ? _searchHighlight : new RectShape[0];   // base page / coordinates

        private void PreloadText(int page)
        {
            var s = _session;
            if (s == null || s.CachedText(page) != null) return;
            s.GetTextAsync(page).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && ReferenceEquals(s, _session)) rightCanvas.InvalidateVisual();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static string ToolHint(Tool t) => t switch
        {
            Tool.Select => "選択: クリックで選択・ドラッグで移動。空き場所をドラッグで範囲選択。矢印キーで微調整",
            Tool.Rect => "矩形: ドラッグで黒塗りの矩形を作ります",
            Tool.Text => "文字: 文字の上をドラッグすると、その文字を行ごとに覆います",
            Tool.Freehand => "手書き: なぞって黒塗りします。近くの線は1つの箇所にまとまります（Enter で区切り）",
            Tool.Eraser => "消しゴム: クリックした図形を消します",
            _ => "",
        };

        /// <summary>Shows a short message in the status bar for a few seconds, then restores the tool hint.</summary>
        private void ShowHint(string text)
        {
            stHint.Text = text;
            _hintTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (s, e) => { _hintTimer!.Stop(); stHint.Text = ToolHint(_tool); }, Dispatcher);
            _hintTimer.Stop(); _hintTimer.Start();
        }

        // ================================================================== notifications / help (ARCHITECTURE §11.8)

        /// <summary>Completion and notices go to the in-window bar; errors and confirmations stay dialogs.</summary>
        private void Notify(InfoSeverity severity, string title, string message = "", string? actionLabel = null, Action? action = null) =>
            infoBar.Show(severity, title, message, actionLabel, action);

        private void Help_Executed(object sender, ExecutedRoutedEventArgs e) => HelpWindow.ShowFor(this);

        // ================================================================== unexpected errors in user commands (ARCHITECTURE §13)

        /// <summary>
        /// Open / save / create / print / search do not change the redaction items, so an unexpected failure there is
        /// reported and the user keeps working instead of losing everything. Only states the runtime cannot recover from
        /// are left to the global handler.
        /// </summary>
        private static bool Recoverable(Exception ex) =>
            !(ex is StackOverflowException || ex is AccessViolationException || ex is System.Threading.ThreadAbortException);

        private void UnexpectedError(string what, Exception ex)
        {
            bool memory = ex is OutOfMemoryException || ex is InsufficientExecutionStackException;
            Error(what + "。\n種別: " + ex.GetType().Name + "\n\n作業中の黒塗り箇所はそのまま残っています。" +
                  (memory ? "メモリが不足しています。ほかのアプリを閉じてからやり直してください。" : "もう一度やり直してください。"));
        }

        // ================================================================== style dropdown

        private void StyleMenu_Click(object sender, RoutedEventArgs e)
        {
            styleBlack.IsChecked = ReferenceEquals(sender, styleBlack);
            styleWhite.IsChecked = ReferenceEquals(sender, styleWhite);
            Style_Checked(sender, e);
            UpdateStyleButton();
        }

        /// <summary>The dropdown shows the current style of the document.</summary>
        private void UpdateStyleButton()
        {
            bool white = styleWhite.IsChecked;
            styleButton.Label = white ? "白塗り+黒枠" : "黒塗り";
            // The icon stays the palette glyph (set in XAML): a black / white swatch glyph would invert in dark mode.
        }

        // ================================================================== theme (ARCHITECTURE §11.7)

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            string theme = (sender as FrameworkElement)?.Tag as string ?? Themes.ThemeManager.Light;
            _settings.Theme = theme;
            Themes.ThemeManager.Apply(theme);
            SettingsStore.TrySave(_settingsPath, _settings);   // silently ignored when the folder is not writable
        }

        private void UpdateThemeChecks()
        {
            themeLight.IsChecked = Themes.ThemeManager.Current == Themes.ThemeManager.Light;
            themeDark.IsChecked = Themes.ThemeManager.Current == Themes.ThemeManager.Dark;
        }

        // ================================================================== nudge

        /// <returns>false when nothing on the current page is selected.</returns>
        private bool Nudge(double dx, double dy)
        {
            var s = _session;
            if (s == null) return false;
            int b = CurrentBase;
            var pg = s.Pages[b];
            // Arrow keys move in screen directions: turn the view delta into a base delta (ARCHITECTURE §2.4).
            var o = Kuronuri.Core.Geometry.CoordMap.FromView(s.Layout.RotationOf(b), pg.Width, pg.Height, new Point2(0, 0));
            var d = Kuronuri.Core.Geometry.CoordMap.FromView(s.Layout.RotationOf(b), pg.Width, pg.Height, new Point2(dx, dy));
            dx = d.X - o.X; dy = d.Y - o.Y;
            var sel = s.SelectedItems.Where(i => i.PageIndex == b).ToList();
            if (sel.Count == 0) return false;
            var union = new RedactionItem(Guid.Empty, b, sel.SelectMany(i => i.Shapes).ToList(), null);
            (dx, dy) = ShapeOps.ClampDelta(union, dx, dy, pg.Width, pg.Height);
            var cmd = EditOps.MoveMany(sel, dx, dy);
            if (cmd != null) Execute(cmd);
            return true;
        }

        // ================================================================== scroll bars / page box / zoom box

        private void UpdateScrollBars()
        {
            foreach (var (v, h) in new[] { (leftV, leftH), (rightV, rightH) })
            {
                Set(v, _view.MaxScrollY, _view.ViewportH, _view.ScrollY);
                Set(h, _view.MaxScrollX, _view.ViewportW, _view.ScrollX);
            }

            void Set(ScrollBar b, double max, double viewport, double value)
            {
                b.Minimum = 0;
                b.Maximum = Math.Max(0, max);
                b.ViewportSize = Math.Max(1, viewport);
                b.LargeChange = Math.Max(1, viewport * 0.9);
                b.SmallChange = 40;
                b.Value = Math.Min(b.Maximum, Math.Max(0, value));
                b.IsEnabled = _session != null && max > 0.5;
            }
        }

        private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            if (_session == null) return;
            var b = (ScrollBar)sender;
            if (b.Orientation == Orientation.Vertical) _view.ScrollTo(_view.ScrollX, b.Value);
            else _view.ScrollTo(b.Value, _view.ScrollY);
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e) { if (_session != null) _view.GoToPage(_view.PageIndex - 1); }
        private void NextPage_Click(object sender, RoutedEventArgs e) { if (_session != null) _view.GoToPage(_view.PageIndex + 1); }

        private void PageBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_session != null && int.TryParse(pageBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    _view.GoToPage(Math.Max(1, Math.Min(_view.PageCount, n)) - 1);
                rightCanvas.Focus();
                UpdateStatus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { rightCanvas.Focus(); UpdateStatus(); e.Handled = true; }
        }

        private void PageBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateStatus();

        private void ZoomBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _session == null || zoomBox.SelectedItem is not ComboBoxItem it) return;
            ApplyZoomText((string)it.Content);
        }

        private void ZoomBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ApplyZoomText(zoomBox.Text); rightCanvas.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { rightCanvas.Focus(); UpdateStatus(); e.Handled = true; }
        }

        private void ZoomBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateStatus();

        private void ApplyZoomText(string text)
        {
            if (_session == null) return;
            text = text.Trim();
            if (text == "幅に合わせる") _view.SetFit(FitMode.Width);
            else if (text == "全体") _view.SetFit(FitMode.Page);
            else if (double.TryParse(text.TrimEnd('%', '％', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct > 0)
                _view.SetZoom(pct / 100);
            Dispatcher.BeginInvoke(new Action(UpdateStatus), DispatcherPriority.Background);
        }

        // ================================================================== next / previous unset

        private void NextUnset_Executed(object sender, ExecutedRoutedEventArgs e) => JumpUnset(+1);
        private void PrevUnset_Executed(object sender, ExecutedRoutedEventArgs e) => JumpUnset(-1);

        private void JumpUnset(int dir)
        {
            var s = _session;
            if (s == null) return;
            var ordered = Kuronuri.Core.Geometry.CoordMap.InDisplayOrder(s.Items.Items, s.Layout, s.Pages);
            var unset = ordered.Where(i => i.Reason == null).ToList();
            if (unset.Count == 0) { ShowHint("理由未設定の箇所はありません"); return; }
            int cur = -1;
            if (s.SelectedIds.Count > 0)
                for (int k = 0; k < ordered.Count; k++) if (s.IsSelected(ordered[k].Id)) { cur = k; if (dir < 0) break; }
            RedactionItem target;
            if (dir > 0) target = ordered.Skip(cur + 1).FirstOrDefault(i => i.Reason == null) ?? unset[0];
            else target = (cur < 0 ? ordered : ordered.Take(cur)).LastOrDefault(i => i.Reason == null) ?? unset[unset.Count - 1];
            s.Select(new[] { target.Id });
            JumpTo(target);
            ShowHint("理由未設定: 残り " + unset.Count + " 件");
        }

        // ================================================================== context menu

        public void ShowItemMenu(PageCanvas source)
        {
            var menu = BuildItemMenu();
            if (menu == null) return;
            menu.PlacementTarget = source;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void ItemList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = BuildItemMenu();
            if (menu == null) { e.Handled = true; return; }
            itemList.ContextMenu = menu;
        }

        private ContextMenu? BuildItemMenu()
        {
            var s = _session;
            if (s == null || s.SelectedIds.Count == 0) return null;
            var sel = s.SelectedItems.ToList();
            var menu = new ContextMenu();
            var reason = new MenuItem { Header = "理由（" + sel.Count + " 件）" };
            reason.Items.Add(MenuEntry("（未設定）", () => ApplyPreset(null)));
            foreach (var p in _settings.Reasons)
            {
                var preset = p;
                reason.Items.Add(MenuEntry(p.ToString(), () => ApplyPreset(preset)));
            }
            menu.Items.Add(reason);
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("結合", () => Cmd.Merge.Execute(null, this), EditOps.CanMerge(sel), "Ctrl+G"));
            menu.Items.Add(MenuEntry("分割", () => Cmd.Split.Execute(null, this), sel.Count == 1 && EditOps.CanSplit(sel[0]), "Ctrl+Shift+G"));
            menu.Items.Add(MenuEntry("削除", DeleteSelection, true, "Delete"));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("箇所リストで表示", () =>
            {
                showList.IsChecked = true;
                sidePanel.SelectedItem = listTab;
                SyncListSelection();
            }));
            return menu;
        }

        private static MenuItem MenuEntry(string header, Action action, bool enabled = true, string? gesture = null)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
            m.Click += (s, e) => action();
            return m;
        }

        private void ApplyPreset(ReasonPreset? preset)
        {
            var s = _session;
            if (s == null) return;
            var parts = new List<EditCommand>();
            foreach (var item in s.SelectedItems)
            {
                string text = item.Reason?.FreeText ?? "";
                Reason? next = preset != null ? preset.ToReason(text) : (text.Length > 0 ? new Reason("", "", text) : null);
                if (next != item.Reason) parts.Add(new ReplaceItemCommand(item, item with { Reason = next }));
            }
            if (parts.Count > 0) Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts));
        }

        // ================================================================== drag & drop

        private static string? DroppedPdf(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return null;
            return string.Equals(Path.GetExtension(files[0]), ".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(files[0]) ? files[0] : null;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_busy && DroppedPdf(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            string? path = DroppedPdf(e);
            e.Handled = true;
            if (path == null || _busy) return;
            Activate();
            // Let the drop operation finish before showing any dialog.
            Dispatcher.BeginInvoke(new Action(() => { if (ConfirmDiscard()) OpenPath(path); }), DispatcherPriority.Background);
        }

        // ================================================================== thumbnails

        private void UpdateThumbBadges()
        {
            var s = _session;
            if (s == null || _thumbs.Count == 0) return;
            var byPage = s.Items.Items.GroupBy(i => i.PageIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<RedactionItem>)g.ToList());
            var none = new RedactionItem[0];
            for (int p = 0; p < _thumbs.Count; p++)
            {
                var vm = _thumbs[p];
                var items = byPage.TryGetValue(vm.BasePage, out var list) ? list : none;   // thumbnails are in view order
                if (items.Count == 0 && vm.Count == 0) continue;
                vm.SetItems(items);
            }
        }

        // ================================================================== search

        private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            showList.IsChecked = true;
            sidePanel.SelectedItem = searchTab;
            Dispatcher.BeginInvoke(new Action(() => { searchBox.Focus(); searchBox.SelectAll(); }), DispatcherPriority.Input);
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { RunSearch(); e.Handled = true; }
        }

        private void Search_Click(object sender, RoutedEventArgs e) => RunSearch();

        private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (presetCombo.SelectedItem is not ComboBoxItem it || it.Tag is not string pattern) return;
            searchBox.Text = pattern;
            regexCheck.IsChecked = true;
            ignoreSpaceCheck.IsChecked = true;
            presetCombo.SelectedIndex = 0;
            RunSearch();
        }

        private void ClearSearch()
        {
            _searchRows.Clear();
            _searchHighlight = new RectShape[0];
            _searchHighlightPage = -1;
            searchStatus.Text = "";
            searchApplyButton.IsEnabled = false;
        }

        private void RunSearch()
        {
            var s = _session;
            if (s == null || _busy) return;
            string pattern = searchBox.Text;
            if (pattern.Trim().Length == 0) { searchStatus.Text = "検索する語句を入れてください。"; return; }
            var opt = new TextSearchOptions { Regex = regexCheck.IsChecked == true, IgnoreWhitespace = ignoreSpaceCheck.IsChecked == true };
            if (opt.Regex)
            {
                try { _ = new System.Text.RegularExpressions.Regex(pattern); }
                catch (ArgumentException) { searchStatus.Text = "正規表現の書き方に誤りがあります。"; return; }
            }
            int pages = s.Pages.Count;
            (List<TextHit> Hits, int TextChars) result;
            try
            {
                result = RunWithProgress("検索しています…", pages, (token, progress) => _worker.Run(() =>
                {
                    var hits = new List<TextHit>();
                    int chars = 0;
                    for (int p = 0; p < pages; p++)
                    {
                        token.ThrowIfCancellationRequested();
                        var t = s.ReadText(p);
                        chars += t.Count;
                        hits.AddRange(TextSearch.Find(t, pattern, opt));
                        progress.Report(p + 1);
                    }
                    return (hits, chars);
                }, token));
            }
            catch (OperationCanceledException) { searchStatus.Text = "検索を中止しました。"; return; }
            catch (Exception ex) when (Recoverable(ex)) { UnexpectedError("検索できませんでした", ex); return; }

            ClearSearch();
            var items = s.Items.Items.ToList();
            foreach (var h in result.Hits) _searchRows.Add(new SearchHitVM(h, TextSearch.IsCovered(h, items), s.ViewOf(h.Page) + 1));
            int covered = _searchRows.Count(r => r.Covered);
            if (result.TextChars == 0)
                searchStatus.Text = "この文書には文字情報がありません（スキャン画像の可能性があります）。検索は使えないため、目視で黒塗りしてください。";
            else if (_searchRows.Count == 0)
                searchStatus.Text = "見つかりませんでした。";
            else
                searchStatus.Text = _searchRows.Count + " 件（" + _searchRows.Select(r => r.Page).Distinct().Count() + " ページ）" +
                                    (covered > 0 ? "、うち " + covered + " 件は黒塗り済み" : "");
            searchApplyButton.IsEnabled = _searchRows.Count > 0;
        }

        private void RefreshSearchCoverage()
        {
            var s = _session;
            if (s == null || _searchRows.Count == 0) return;
            var items = s.Items.Items.ToList();
            foreach (var r in _searchRows)
            {
                bool c = TextSearch.IsCovered(r.Hit, items);
                if (c && !r.Covered) r.Selected = false;
                r.Covered = c;
            }
        }

        private void SearchSelectAll_Click(object sender, RoutedEventArgs e) { foreach (var r in _searchRows) r.Selected = !r.Covered; }
        private void SearchSelectNone_Click(object sender, RoutedEventArgs e) { foreach (var r in _searchRows) r.Selected = false; }

        private void SearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (searchList.SelectedItem is not SearchHitVM row) return;
            _searchHighlight = row.Hit.Rects;
            _searchHighlightPage = row.Hit.Page;
            _view.GoToPage(_session!.ViewOf(row.Hit.Page));
            var b = ShapeOps.Box(new RedactionItem(Guid.Empty, row.Hit.Page, row.Hit.Rects.Cast<Shape>().ToList(), null));
            CenterOnBase(row.Hit.Page, b);
            rightCanvas.InvalidateVisual();
        }

        private void SearchApply_Click(object sender, RoutedEventArgs e)
        {
            var s = _session;
            if (s == null) return;
            var chosen = _searchRows.Where(r => r.Selected).ToList();
            if (chosen.Count == 0) { searchStatus.Text = "黒塗りにするヒットを選んでください。"; return; }
            var newItems = chosen.Select(r => TextSearch.ToItem(r.Hit, _currentReason)).ToList();
            Execute(new CompositeCommand(newItems.Select(i => (EditCommand)new AddItemCommand(i))));
            s.Select(newItems.Select(i => i.Id));
            _searchHighlight = new RectShape[0];
            Notify(InfoSeverity.Success, newItems.Count + " 箇所を黒塗りにしました", "理由は「" + (_currentReason == null ? "未設定" : ItemRow.Describe(_currentReason)) + "」で追加しました。", "元に戻す", () => Cmd.Undo.Execute(null, this));
        }

        // ================================================================== window layout (settings.json "window")

        private void ApplyWindowLayout()
        {
            var w = _settings.Window;
            if (w == null) return;
            if (w.MostlyInside(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = w.Left; Top = w.Top; Width = w.Width; Height = w.Height;
            }
            if (w.Maximized) WindowState = WindowState.Maximized;
            _lastThumbsWidth = w.ThumbsWidth; _lastListWidth = w.ListWidth;
            colThumbs.Width = new GridLength(w.ThumbsWidth);
            colList.Width = new GridLength(w.ListWidth);
            showThumbs.IsChecked = w.ShowThumbs;
            showList.IsChecked = w.ShowList;
            twoPane.IsChecked = w.TwoPane;
            Panels_Changed(this, new RoutedEventArgs());
        }

        private void SaveWindowLayout()
        {
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (b.IsEmpty) return;
            _settings.Window = new WindowLayout
            {
                Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
                Maximized = WindowState == WindowState.Maximized,
                ThumbsWidth = showThumbs.IsChecked == true ? colThumbs.ActualWidth : _lastThumbsWidth,
                ListWidth = showList.IsChecked == true ? colList.ActualWidth : _lastListWidth,
                ShowThumbs = showThumbs.IsChecked == true,
                ShowList = showList.IsChecked == true,
                TwoPane = twoPane.IsChecked == true,
            };
            if (!_settings.Window.IsUsable()) { _settings.Window = null; return; }
            SettingsStore.TrySave(_settingsPath, _settings);   // silently ignored when the folder is not writable
        }
    }
}
