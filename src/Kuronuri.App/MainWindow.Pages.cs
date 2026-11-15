using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;

namespace Kuronuri.App
{
    /// <summary>Page order, rotation and exclusion in the window (ARCHITECTURE §4.4, §11.9).</summary>
    public partial class MainWindow
    {
        private PageLayout? _shownLayout;   // the layout the thumbnails / view were built for

        /// <summary>Original page shown at the current view position.</summary>
        private int CurrentBase => _session!.BaseOf(_view.PageIndex);

        private void BuildThumbs()
        {
            var s = _session;
            if (s == null) return;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var l = s.Layout;
            _thumbs = Enumerable.Range(0, s.Pages.Count).Select(v =>
            {
                int b = l.BaseOf(v);
                return new ThumbVM(v, b, l.RotationOf(b), l.IsExcluded(b), s.Pages[b].Width, s.Pages[b].Height, dpi, () => _session?.Renderer);
            }).ToList();
            _syncing = true;
            thumbList.ItemsSource = _thumbs;
            thumbList.SelectedIndex = _view.PageIndex;
            _syncing = false;
            UpdateThumbBadges();
            _shownLayout = l;
        }

        /// <summary>Centres a box given in base coordinates of <paramref name="basePage"/>; that page becomes the current one.</summary>
        private void CenterOnBase(int basePage, BoxD box)
        {
            var s = _session!;
            var p = s.Pages[basePage];
            var c = CoordMap.ToView(s.Layout.RotationOf(basePage), p.Width, p.Height, new Point2((box.L + box.R) / 2, (box.T + box.B) / 2));
            _view.CenterOn(s.ViewOf(basePage), c.X, c.Y);
        }

        private void UpdateExcludedBanner()
        {
            var s = _session;
            excludedBanner.Visibility = s != null && s.Layout.IsExcluded(CurrentBase) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Called on every item / layout change: rebuilds what depends on the layout when it changed.</summary>
        private void SyncLayout()
        {
            var s = _session;
            if (s == null) return;
            var l = s.Layout;
            if (_shownLayout != null && ReferenceEquals(_shownLayout, l)) return;
            int keepBase = _shownLayout != null ? _shownLayout.BaseOf(Math.Min(_view.PageIndex, _shownLayout.Count - 1)) : l.BaseOf(0);
            _view.Relayout(s.ViewSize, l.ViewOf(keepBase));
            BuildThumbs();
            foreach (var r in _searchRows) r.SetViewPage(l.ViewOf(r.Hit.Page) + 1);
            UpdateExcludedBanner();
        }

        // ------------------------------------------------------------------ page commands

        private void ChangeLayout(Func<PageLayout, int, PageLayout> change)
        {
            var s = _session;
            if (s == null || _busy) return;
            rightCanvas.CancelDrag();
            var before = s.Layout;
            var after = change(before, CurrentBase);
            if (after.SameAs(before)) return;
            Execute(new SetLayoutCommand(before, after));
        }

        private void RotateRight_Executed(object sender, ExecutedRoutedEventArgs e) => ChangeLayout((l, b) => l.Rotate(b, 1));
        private void RotateLeft_Executed(object sender, ExecutedRoutedEventArgs e) => ChangeLayout((l, b) => l.Rotate(b, -1));
        private void Rotate180_Executed(object sender, ExecutedRoutedEventArgs e) => ChangeLayout((l, b) => l.Rotate(b, 2));

        private void ToggleExclude_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var s = _session;
            if (s == null) return;
            bool exclude = !s.Layout.IsExcluded(CurrentBase);
            if (exclude && s.Layout.IncludedCount == 1)
            {
                Notify(InfoSeverity.Info, "このページは除外できません", "開示用PDFには少なくとも1ページが必要です。");
                return;
            }
            ChangeLayout((l, b) => l.SetExcluded(b, exclude));   // the banner over the page and the thumbnail tag show the result
        }

        private void MovePageUp_Executed(object sender, ExecutedRoutedEventArgs e) => MovePage(_view.PageIndex, _view.PageIndex - 1);
        private void MovePageDown_Executed(object sender, ExecutedRoutedEventArgs e) => MovePage(_view.PageIndex, _view.PageIndex + 1);

        private void MovePage(int from, int to)
        {
            var s = _session;
            if (s == null || from < 0 || to < 0 || from >= s.Pages.Count || to >= s.Pages.Count || from == to) return;
            int moved = s.BaseOf(from);
            ChangeLayout((l, b) => l.Move(from, to));
            _view.GoToPage(s.ViewOf(moved));
        }

        private void PageCmd_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session != null && !_busy;

        private void MovePageUp_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session != null && !_busy && _view.PageIndex > 0;
        private void MovePageDown_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
            e.CanExecute = _session != null && !_busy && _view.PageIndex < _session.Pages.Count - 1;

        // ------------------------------------------------------------------ thumbnail list: menu and drag to reorder

        private void Thumb_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var vm = ThumbUnder(e.OriginalSource as DependencyObject);
            if (vm == null || _session == null) { e.Handled = true; return; }
            if (vm.Index != _view.PageIndex) _view.GoToPage(vm.Index);
            excludeMenuThumb.Header = _session.Layout.IsExcluded(vm.BasePage) ? "開示用PDFに含める" : "開示用PDFから除く";
        }

        private void PageMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (_session != null) excludeMenuBar.Header = _session.Layout.IsExcluded(CurrentBase) ? "開示用PDFに含める" : "開示用PDFから除く";
        }

        private static ThumbVM? ThumbUnder(DependencyObject? d)
        {
            while (d != null && !(d is ListBoxItem)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext as ThumbVM;
        }

        private Point _thumbDragStart;
        private ThumbVM? _thumbDragItem;
        private const string ThumbDragFormat = "kuronuri-page";

        private void Thumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _thumbDragStart = e.GetPosition(thumbList);
            _thumbDragItem = ThumbUnder(e.OriginalSource as DependencyObject);
        }

        private void Thumb_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_thumbDragItem == null || e.LeftButton != MouseButtonState.Pressed || _busy) return;
            var p = e.GetPosition(thumbList);
            if (Math.Abs(p.X - _thumbDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - _thumbDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var item = _thumbDragItem;
            _thumbDragItem = null;
            DragDrop.DoDragDrop(thumbList, new DataObject(ThumbDragFormat, item.Index), DragDropEffects.Move);
        }

        private void Thumb_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(ThumbDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Thumb_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(ThumbDragFormat) || _session == null) return;
            int from = (int)e.Data.GetData(ThumbDragFormat);
            var target = ThumbUnder(e.OriginalSource as DependencyObject);
            int to = target?.Index ?? _session.Pages.Count - 1;
            MovePage(from, to);
        }
    }
}
