using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Kuronuri.App.Themes;

namespace Kuronuri.App
{
    /// <summary>Small screens (ARCHITECTURE §11.10): first window size, automatic one-pane view, compact command bar, pane ratio.</summary>
    public partial class MainWindow
    {
        /// <summary>Narrowest useful width of each of the two page panes (DIP).</summary>
        private const double MinPaneWidth = 280;
        /// <summary>Extra width needed before going back to two panes, so the view does not flicker at the boundary.</summary>
        private const double PaneHysteresis = 40;

        private double _paneRatio = 0.5;     // share of the left (original) pane
        private bool _autoOnePane;           // two panes wanted but there is no room
        private bool? _shownTwoPanes;        // what the grid currently shows
        private double _barFullWidth;        // command bar width with every label, on one row
        private bool _panesUpdateQueued;

        private const double MinThumbsWidth = 100, MinListWidth = 220;
        /// <summary>Width the editing pane should keep before the page list and the side panel are narrowed (DIP).</summary>
        private const double MinEditPaneWidth = 420;

        /// <summary>
        /// The minimum size never exceeds the screen. On first start (no saved window), a screen smaller than the default
        /// window opens maximized; restoring gives 95 % of the work area.
        /// </summary>
        private void FitToScreen()
        {
            var wa = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, wa.Width);
            MinHeight = Math.Min(MinHeight, wa.Height);
            if (_settings.Window != null) return;
            if (Width <= wa.Width && Height <= wa.Height) return;
            Width = Math.Min(Width, wa.Width * 0.95);
            Height = Math.Min(Height, wa.Height * 0.95);
            WindowState = WindowState.Maximized;
        }

        private void InitLayout()
        {
            FitToScreen();
            mainGrid.SizeChanged += (s, e) => { if (e.WidthChanged) ApplySideWidths(); QueueUpdatePanes(); };
            thumbList.SizeChanged += (s, e) => QueueUpdatePanes();
            sidePanel.SizeChanged += (s, e) => QueueUpdatePanes();
            Loaded += (s, e) => { MeasureFullBar(); UpdateCompactBar(); UpdatePanes(); };
        }

        // ------------------------------------------------------------------ page list and side panel widths

        /// <summary>
        /// Shows the page list and the side panel at the widths the user chose; when the window is too small for the
        /// editing pane to keep <see cref="MinEditPaneWidth"/>, both are narrowed (down to their minimum) in proportion to
        /// how much each can give. The chosen widths are kept and come back when the window is widened.
        /// </summary>
        private void ApplySideWidths()
        {
            if (colThumbs == null) return;
            bool t = showThumbs.IsChecked == true, l = showList.IsChecked == true;
            double tw = t ? Math.Max(MinThumbsWidth, _lastThumbsWidth) : 0, lw = l ? Math.Max(MinListWidth, _lastListWidth) : 0;
            double total = mainGrid.ActualWidth;
            if (total > 0)
            {
                double excess = tw + lw + (t ? 4 : 0) + (l ? 4 : 0) + MinEditPaneWidth - total;
                double canT = t ? tw - MinThumbsWidth : 0, canL = l ? lw - MinListWidth : 0;
                if (excess > 0 && canT + canL > 0)
                {
                    double cut = Math.Min(excess, canT + canL);
                    tw -= cut * canT / (canT + canL);
                    lw -= cut * canL / (canT + canL);
                }
            }
            colThumbs.Width = new GridLength(Math.Round(tw));
            colList.Width = new GridLength(Math.Round(lw));
        }

        private void SideSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (showThumbs.IsChecked == true && colThumbs.ActualWidth > 1) _lastThumbsWidth = colThumbs.ActualWidth;
            if (showList.IsChecked == true && colList.ActualWidth > 1) _lastListWidth = colList.ActualWidth;
        }

        // ------------------------------------------------------------------ one pane / two panes

        private void QueueUpdatePanes()
        {
            if (_panesUpdateQueued) return;
            _panesUpdateQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { _panesUpdateQueued = false; UpdatePanes(); }));
        }

        /// <summary>Width left for the page panes once the page list, the side panel and the splitters are placed.</summary>
        private double PaneSpace() =>
            mainGrid.ActualWidth - colThumbs.ActualWidth - colSplit1.ActualWidth - colSplit2.ActualWidth - colList.ActualWidth;

        private void UpdatePanes()
        {
            if (leftHost == null || !IsLoaded) return;
            bool want = twoPane.IsChecked == true;
            double space = PaneSpace();
            double need = 2 * MinPaneWidth + splitPanes.Width;
            if (!want) _autoOnePane = false;
            else if (_autoOnePane) { if (space >= need + PaneHysteresis) _autoOnePane = false; }
            else if (space < need) _autoOnePane = true;

            bool two = want && !_autoOnePane;
            autoPaneNote.Visibility = _autoOnePane ? Visibility.Visible : Visibility.Collapsed;
            if (_shownTwoPanes == two) return;
            if (_shownTwoPanes == true) RememberPaneRatio();
            _shownTwoPanes = two;
            if (two)
            {
                colLeft.Width = new GridLength(_paneRatio, GridUnitType.Star);
                colRight.Width = new GridLength(1 - _paneRatio, GridUnitType.Star);
                colLeft.MinWidth = colRight.MinWidth = 200;
            }
            else
            {
                colLeft.MinWidth = 0;
                colLeft.Width = new GridLength(0);
                colRight.Width = new GridLength(1, GridUnitType.Star);
                colRight.MinWidth = 200;
            }
            colGap.Width = two ? new GridLength(splitPanes.Width) : new GridLength(0);
            splitPanes.Visibility = leftHost.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RememberPaneRatio()
        {
            double l = colLeft.ActualWidth, r = colRight.ActualWidth;
            if (l > 1 && r > 1) _paneRatio = Math.Max(0.2, Math.Min(0.8, l / (l + r)));
        }

        private void SplitPanes_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            RememberPaneRatio();
            // Keep both columns proportional (the splitter leaves pixel-based star values behind).
            colLeft.Width = new GridLength(_paneRatio, GridUnitType.Star);
            colRight.Width = new GridLength(1 - _paneRatio, GridUnitType.Star);
        }

        private void SplitPanes_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            _paneRatio = 0.5;
            colLeft.Width = new GridLength(1, GridUnitType.Star);
            colRight.Width = new GridLength(1, GridUnitType.Star);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ command bar

        private void MeasureFullBar()
        {
            IconButton.SetCompact(commandBar, false);
            commandBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _barFullWidth = commandBar.DesiredSize.Width;
            commandBar.InvalidateMeasure();
        }

        private void CommandBarHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCompactBar();

        /// <summary>Hides the button labels (icons and tooltips stay) when the bar does not fit on one row with them.</summary>
        private void UpdateCompactBar()
        {
            if (_barFullWidth <= 0) return;
            double avail = commandBarHost.ActualWidth - commandBarHost.Padding.Left - commandBarHost.Padding.Right - commandBarHost.BorderThickness.Left - commandBarHost.BorderThickness.Right;
            bool compact = avail < _barFullWidth;
            if (IconButton.GetCompact(commandBar) != compact) IconButton.SetCompact(commandBar, compact);
        }

        // ------------------------------------------------------------------ lists: the text column takes the remaining width

        private void ItemList_SizeChanged(object sender, SizeChangedEventArgs e) => FitColumn(itemList, reasonColumn, 60);
        private void SearchList_SizeChanged(object sender, SizeChangedEventArgs e) => FitColumn(searchList, previewColumn, 60);

        private static void FitColumn(ListView list, GridViewColumn fill, double min)
        {
            if (list.View is not GridView g) return;
            double others = 0;
            foreach (var c in g.Columns) if (c != fill) others += c.ActualWidth;
            double w = list.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 8;
            fill.Width = Math.Max(min, w);
        }
    }
}
