using System;

namespace Kuronuri.App
{
    public enum FitMode { None, Page, Width }

    /// <summary>
    /// The one view state shared by the left and right panes (ARCHITECTURE §6). Both panes draw from it, so they are in
    /// sync by construction. Zoom 1.0 = 100% (1pt = 96/72 DIP).
    /// <para>
    /// Pages are stacked in one column (continuous scrolling): <see cref="PageGap"/> DIP between pages, <see cref="Pad"/>
    /// DIP before the first and after the last, each page centred horizontally. ScrollX/Y = top-left of the viewport
    /// inside that column, in DIP. <see cref="PageIndex"/> (the current page) follows the scroll position.
    /// </para>
    /// <para>
    /// When <see cref="Continuous"/> is off (one page at a time), the column holds only the current page: scrolling never
    /// changes the page, and moving to another page re-applies the fit mode to it.
    /// </para>
    /// </summary>
    public sealed class ViewState
    {
        public const double MinZoom = 0.10, MaxZoom = 8.0;
        public const double PageGap = 12, Pad = 8;

        private int _pageIndex;
        private double _zoom = 1, _scrollX, _scrollY;
        private bool _peek;
        private (double W, double H)[] _sizes = new (double, double)[0];   // view size of each page (pt)
        private double[] _tops = new double[1];                              // sum of the heights above each page (pt)
        private double _maxWidthPt;
        private bool _continuous = true;

        public event EventHandler? Changed;

        /// <summary>Raised only when the current page changes (before <see cref="Changed"/>).</summary>
        public event EventHandler? PageChanged;

        public int PageCount => _sizes.Length;
        public double ViewportW { get; private set; } = 800;
        public double ViewportH { get; private set; } = 600;
        public FitMode Fit { get; private set; } = FitMode.Page;

        public int PageIndex => _pageIndex;
        public double Zoom => _zoom;
        public double ScrollX => _scrollX;
        public double ScrollY => _scrollY;
        public double DipPerPt => _zoom * 96.0 / 72.0;

        /// <summary>Size of the current page (pt).</summary>
        public double PageWidthPt => PageCount > 0 ? _sizes[_pageIndex].W : 595;
        public double PageHeightPt => PageCount > 0 ? _sizes[_pageIndex].H : 842;

        /// <summary>Pages are stacked in one column (otherwise only the current page is laid out).</summary>
        public bool Continuous => _continuous;

        // The pages laid out in the column.
        private int First => _continuous ? 0 : _pageIndex;
        private int Last => _continuous ? PageCount - 1 : _pageIndex;

        /// <summary>Size of the whole column of pages (DIP).</summary>
        public double DocW => (_continuous ? _maxWidthPt : PageWidthPt) * DipPerPt;
        public double DocH => PageCount == 0 ? 0 : 2 * Pad + (_tops[Last + 1] - _tops[First]) * DipPerPt + PageGap * (Last - First);

        public double MaxScrollX => Math.Max(0, DocW - ViewportW);
        public double MaxScrollY => Math.Max(0, DocH - ViewportH);

        /// <summary>Space held: overlays are drawn at 25% opacity.</summary>
        public bool Peek { get => _peek; set { if (_peek == value) return; _peek = value; Raise(); } }

        // ------------------------------------------------------------------ layout

        public (double W, double H) PageSizePt(int view) => _sizes[view];

        /// <summary>Top of a page inside the column (DIP).</summary>
        private double Top(int view) => Pad + (_tops[view] - _tops[First]) * DipPerPt + PageGap * (view - First);

        /// <summary>Top-left of a page in the viewport of the editing pane (DIP).</summary>
        public (double X, double Y) PageOrigin(int view)
        {
            double pw = _sizes[view].W * DipPerPt;
            double x = DocW < ViewportW ? (ViewportW - pw) / 2 : (DocW - pw) / 2 - _scrollX;
            double y = (DocH < ViewportH ? (ViewportH - DocH) / 2 : -_scrollY) + Top(view);
            return (x, y);
        }

        /// <summary>The page at a height of the viewport (DIP); in a gap, the nearer page.</summary>
        public int PageAt(double viewportY)
        {
            if (PageCount == 0) return 0;
            double y = viewportY - (DocH < ViewportH ? (ViewportH - DocH) / 2 : -_scrollY);   // in the column
            int lo = First, hi = Last;
            while (lo < hi)   // last page whose top is above y
            {
                int mid = (lo + hi + 1) / 2;
                if (Top(mid) <= y) lo = mid; else hi = mid - 1;
            }
            double bottom = Top(lo) + _sizes[lo].H * DipPerPt;
            if (y > bottom && lo < Last && y - bottom > PageGap / 2) lo++;
            return lo;
        }

        /// <summary>First and last page that intersect the viewport.</summary>
        public (int First, int Last) VisiblePages()
        {
            if (PageCount == 0) return (0, -1);
            int first = PageAt(0), last = PageAt(ViewportH);
            if (PageOrigin(first).Y + _sizes[first].H * DipPerPt < 0 && first < last) first++;
            if (PageOrigin(last).Y > ViewportH && last > first) last--;
            return (first, last);
        }

        public bool IsVisible(int view)
        {
            var (f, l) = VisiblePages();
            return view >= f && view <= l;
        }

        // ------------------------------------------------------------------ changes

        public void Reset(int pageCount, Func<int, (double W, double H)> pageSize)
        {
            SetSizes(pageCount, pageSize);
            _pageIndex = 0;
            Fit = FitMode.Page;
            ApplyFit();
            _scrollY = 0;
            _scrollX = CenteredX(0);
            Clamp();
            PageChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }

        /// <summary>
        /// The page order or a page size changed (page layout, ARCHITECTURE §4.4): keep view position <paramref name="index"/>
        /// where the current page was, with the current zoom (or fit mode).
        /// </summary>
        public void Relayout(Func<int, (double W, double H)> pageSize, int index)
        {
            double offPt = PageCount > 0 ? (_scrollY - Top(_pageIndex)) / DipPerPt : 0;
            SetSizes(PageCount, pageSize);
            _pageIndex = Math.Max(0, Math.Min(PageCount - 1, index));
            ApplyFit();
            _scrollY = Top(_pageIndex) + offPt * DipPerPt;
            Clamp();
            PageChanged?.Invoke(this, EventArgs.Empty);   // always: the page shown at this position may be another one
            Raise();
        }

        /// <summary>Switches between continuous scrolling and one page at a time, keeping the current page where it is.</summary>
        public void SetContinuous(bool continuous)
        {
            if (_continuous == continuous) return;
            KeepCurrentPage(() => _continuous = continuous);
        }

        public void SetViewport(double w, double h)
        {
            if (w <= 0 || h <= 0 || (Math.Abs(w - ViewportW) < 0.5 && Math.Abs(h - ViewportH) < 0.5)) return;
            KeepCurrentPage(() => { ViewportW = w; ViewportH = h; ApplyFit(); });
        }

        /// <param name="toBottom">Show the bottom of the page instead of its top.</param>
        public void GoToPage(int index, bool toBottom = false)
        {
            if (PageCount == 0) return;
            index = Math.Max(0, Math.Min(PageCount - 1, index));
            bool changed = index != _pageIndex;
            _pageIndex = index;                // first: one page at a time lays out only this page
            if (!_continuous) ApplyFit();      // each page is fitted on its own there
            _scrollY = toBottom ? Top(index) + _sizes[index].H * DipPerPt + Pad - ViewportH : Top(index) - Pad;
            _scrollX = CenteredX(index);
            Clamp();
            if (changed) PageChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }

        /// <summary>Scrolls; the current page follows.</summary>
        public void ScrollBy(double dx, double dy) => ScrollTo(_scrollX + dx, _scrollY + dy);

        public void ScrollTo(double x, double y)
        {
            _scrollX = x; _scrollY = y;
            Clamp();
            SetCurrent(PageFromScroll());
            Raise();
        }

        /// <summary>Centres a point of a page (pt) in the viewport and makes that page the current one.</summary>
        public void CenterOn(int view, double xPt, double yPt)
        {
            if (PageCount == 0) return;
            view = Math.Max(0, Math.Min(PageCount - 1, view));
            bool changed = view != _pageIndex;
            _pageIndex = view;
            if (changed && !_continuous) ApplyFit();
            _scrollX = (DocW - _sizes[view].W * DipPerPt) / 2 + xPt * DipPerPt - ViewportW / 2;
            _scrollY = Top(view) + yPt * DipPerPt - ViewportH / 2;
            Clamp();
            if (changed) PageChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }

        public void SetFit(FitMode fit)
        {
            if (fit == FitMode.Page)
            {
                Fit = fit;
                ApplyFit();
                GoToPage(_pageIndex);   // the whole current page
                return;
            }
            KeepCurrentPage(() => { Fit = fit; ApplyFit(); });
            _scrollX = CenteredX(_pageIndex);
            Clamp();
            Raise();
        }

        /// <summary>Zooms keeping the point under (vx, vy) (viewport DIP) fixed.</summary>
        public void ZoomAt(double newZoom, double vx, double vy)
        {
            if (PageCount == 0) return;
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            int i = PageAt(vy);
            var (ox, oy) = PageOrigin(i);
            double px = (vx - ox) / DipPerPt, py = (vy - oy) / DipPerPt;   // may lie outside the page (gap, margin)
            Fit = FitMode.None;
            _zoom = newZoom;
            _scrollX = (DocW - _sizes[i].W * DipPerPt) / 2 + px * DipPerPt - vx;
            _scrollY = Top(i) + py * DipPerPt - vy;
            Clamp();
            SetCurrent(PageFromScroll());
            Raise();
        }

        public void SetZoom(double z) => ZoomAt(z, ViewportW / 2, ViewportH / 2);

        // ------------------------------------------------------------------ helpers

        private void SetSizes(int count, Func<int, (double W, double H)> pageSize)
        {
            _sizes = new (double, double)[count];
            _tops = new double[count + 1];
            _maxWidthPt = 0;
            for (int i = 0; i < count; i++)
            {
                _sizes[i] = pageSize(i);
                _tops[i + 1] = _tops[i] + _sizes[i].H;
                _maxWidthPt = Math.Max(_maxWidthPt, _sizes[i].W);
            }
        }

        /// <summary>
        /// Horizontal scroll that shows page <paramref name="view"/> centred; a page wider than the viewport keeps the
        /// current position as far as it stays on that page.
        /// </summary>
        private double CenteredX(int view)
        {
            double pw = _sizes[view].W * DipPerPt, centre = (DocW - ViewportW) / 2;
            if (pw <= ViewportW) return centre;
            double left = (DocW - pw) / 2;
            return Math.Max(left, Math.Min(left + pw - ViewportW, _scrollX));
        }

        /// <summary>Runs a change of zoom / viewport keeping the current page's offset (in pt) at the top of the viewport.</summary>
        private void KeepCurrentPage(Action change)
        {
            double offPt = PageCount > 0 ? (_scrollY - Top(_pageIndex)) / DipPerPt : 0;
            double fx = DocW > 0 ? (_scrollX + ViewportW / 2) / DocW : 0.5;
            change();
            if (PageCount > 0)
            {
                _scrollY = Top(_pageIndex) + offPt * DipPerPt;
                _scrollX = fx * DocW - ViewportW / 2;
            }
            Clamp();
            Raise();
        }

        /// <summary>
        /// The page at the vertical centre of the viewport; at the very top / bottom of the column the topmost / bottommost
        /// visible page, so that every page can become the current one when several fit in the viewport.
        /// </summary>
        private int PageFromScroll()
        {
            if (PageCount == 0 || !_continuous) return _pageIndex;
            var (first, last) = VisiblePages();
            if (MaxScrollY > 0.5 && _scrollY <= 0.5) return first;
            if (MaxScrollY > 0.5 && _scrollY >= MaxScrollY - 0.5) return last;
            return PageAt(ViewportH / 2);
        }

        private void SetCurrent(int index)
        {
            if (index == _pageIndex) return;
            _pageIndex = index;
            PageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyFit()
        {
            if (PageCount == 0) return;
            const double margin = 16;
            double w = Math.Max(50, ViewportW - margin), h = Math.Max(50, ViewportH - margin);
            double k = 96.0 / 72.0;
            if (Fit == FitMode.Page) _zoom = Math.Min(w / (PageWidthPt * k), h / (PageHeightPt * k));
            else if (Fit == FitMode.Width) _zoom = w / (PageWidthPt * k);   // the current page (ARCHITECTURE §6)
            _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom));
        }

        private void Clamp()
        {
            _scrollX = Math.Max(0, Math.Min(MaxScrollX, _scrollX));
            _scrollY = Math.Max(0, Math.Min(MaxScrollY, _scrollY));
        }

        private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
