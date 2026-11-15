using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Text
{
    public sealed class TextSearchOptions
    {
        /// <summary>Spaces and line breaks are removed before matching (names split by a line break or "山田　太郎").</summary>
        public bool IgnoreWhitespace = true;

        /// <summary>The pattern is a .NET regular expression applied to the normalized text.</summary>
        public bool Regex;

        public TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    }

    /// <summary>One search hit: a run of characters on one page and the rectangles that cover it.</summary>
    public sealed class TextHit
    {
        public TextHit(int page, int first, int last, string preview, IReadOnlyList<RectShape> rects)
        {
            Page = page; First = first; Last = last; Preview = preview; Rects = rects;
        }

        public int Page { get; }
        public int First { get; }
        public int Last { get; }
        public string Preview { get; }
        public IReadOnlyList<RectShape> Rects { get; }
    }

    /// <summary>Text search over <see cref="PageText"/> (ARCHITECTURE §11.5). Pure; no PDFium calls.</summary>
    public static class TextSearch
    {
        /// <summary>Fixed patterns, written for the normalized text (NFKC, lower case; whitespace removed by default).</summary>
        public static readonly IReadOnlyList<(string Name, string Pattern)> Presets = new[]
        {
            ("電話番号", @"(?<!\d)0\d{1,4}[-(]?\d{1,4}[-)]?\d{3,4}(?!\d)"),
            ("メールアドレス", @"[a-z0-9._%+\-]+@[a-z0-9\-]+(\.[a-z0-9\-]+)*\.[a-z]{2,}"),
            ("郵便番号", @"(?<!\d)\d{3}-\d{4}(?!\d)"),
            ("12 桁の番号", @"(?<!\d)\d{4}-?\d{4}-?\d{4}(?!\d)"),
        };

        public const int PreviewContext = 12;

        /// <summary>NFKC + lower case, so that full-width / half-width and case do not matter.</summary>
        public static string Normalize(string s) => s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

        private static bool IsSpace(string c) => c.Length == 0 || c.All(char.IsWhiteSpace);

        /// <summary>The normalized page text and, for every UTF-16 unit of it, the index of the source character.</summary>
        public static (string Text, int[] Map) BuildIndex(PageText t, bool ignoreWhitespace)
        {
            var sb = new StringBuilder();
            var map = new List<int>();
            for (int i = 0; i < t.Count; i++)
            {
                string c = t.Chars[i];
                if (c.Length == 0 || (ignoreWhitespace && IsSpace(c))) continue;
                string n = Normalize(c);
                sb.Append(n);
                for (int k = 0; k < n.Length; k++) map.Add(i);
            }
            return (sb.ToString(), map.ToArray());
        }

        public static string PreparePattern(string pattern, TextSearchOptions o)
        {
            if (o.Regex) return pattern;
            string p = Normalize(pattern);
            return o.IgnoreWhitespace ? new string(p.Where(ch => !char.IsWhiteSpace(ch)).ToArray()) : p;
        }

        /// <exception cref="ArgumentException">Invalid regular expression.</exception>
        public static List<TextHit> Find(PageText t, string pattern, TextSearchOptions o)
        {
            var hits = new List<TextHit>();
            string p = PreparePattern(pattern, o);
            if (p.Length == 0 || t.Count == 0) return hits;
            var (text, map) = BuildIndex(t, o.IgnoreWhitespace);
            if (text.Length == 0) return hits;

            var spans = new List<(int Start, int End)>();
            if (o.Regex)
            {
                var rx = new Regex(p, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, o.RegexTimeout);
                try
                {
                    for (var m = rx.Match(text); m.Success; m = m.NextMatch())
                        if (m.Length > 0) spans.Add((m.Index, m.Index + m.Length));
                }
                catch (RegexMatchTimeoutException) { /* keep what was found before the time limit */ }
            }
            else
            {
                for (int at = text.IndexOf(p, StringComparison.Ordinal); at >= 0; at = text.IndexOf(p, at + p.Length, StringComparison.Ordinal))
                    spans.Add((at, at + p.Length));
            }

            foreach (var (s, e) in spans)
            {
                int first = map[s], last = map[e - 1];
                var rects = TextRects.ForRange(t, first, last);
                if (rects.Count == 0) continue;   // characters without boxes (e.g. hidden text with no geometry)
                string preview = (first > PreviewContext ? "…" : "") + t.Slice(first - PreviewContext, last + PreviewContext) + (last + PreviewContext < t.Count - 1 ? "…" : "");
                hits.Add(new TextHit(t.PageIndex, first, last, preview, rects));
            }
            return hits;
        }

        /// <summary>
        /// True when every rectangle of the hit is already covered by the given items (sampled on a 5 x 3 grid inside each
        /// rectangle). Used to mark hits as "済" in the search list.
        /// </summary>
        public static bool IsCovered(TextHit hit, IEnumerable<RedactionItem> items)
        {
            var shapes = items.Where(i => i.PageIndex == hit.Page).SelectMany(i => i.Shapes).ToList();
            if (shapes.Count == 0) return false;
            foreach (var r in hit.Rects)
                for (int iy = 0; iy < 3; iy++)
                    for (int ix = 0; ix < 5; ix++)
                    {
                        var p = new Point2(r.X + r.W * (ix + 0.5) / 5, r.Y + r.H * (iy + 0.5) / 3);
                        if (!shapes.Any(s => ShapeOps.Hit(s, p, 0))) return false;
                    }
            return true;
        }

        /// <summary>One item per hit (all rectangles of the hit in one item).</summary>
        public static RedactionItem ToItem(TextHit hit, Reason? reason) =>
            new RedactionItem(Guid.NewGuid(), hit.Page, hit.Rects.Cast<Shape>().ToList(), reason);
    }
}
