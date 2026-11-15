using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Kuronuri.App
{
    /// <summary>
    /// In-app help (F1, ARCHITECTURE §11.8): the user guide embedded in the EXE, shown as a FlowDocument.
    /// Modeless and single-instance; no browser or external file is involved.
    /// </summary>
    public sealed class HelpWindow : Window
    {
        private static HelpWindow? _open;

        public static void ShowFor(Window owner)
        {
            if (_open != null) { if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; _open.Activate(); return; }
            _open = new HelpWindow { Owner = owner };
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        private HelpWindow()
        {
            Title = "公文書くろぬり - 使い方";
            Width = 760; Height = Math.Min(820, SystemParameters.WorkArea.Height * 0.9);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            Themes.ThemeManager.Attach(this);
            var doc = MarkdownFlow.Build(LoadGuide());
            doc.PagePadding = new Thickness(32, 24, 32, 32);
            doc.FontFamily = FontFamily;
            doc.FontSize = 14;
            doc.TextAlignment = TextAlignment.Left;   // FlowDocument justifies by default, which spreads Japanese / mixed text oddly
            doc.SetResourceReference(FlowDocument.ForegroundProperty, "Text.Primary");
            doc.SetResourceReference(FlowDocument.BackgroundProperty, "Bg.Layer");
            Content = new FlowDocumentScrollViewer { Document = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false };
        }

        private static string LoadGuide()
        {
            using (var s = typeof(HelpWindow).Assembly.GetManifestResourceStream("USER_GUIDE.md"))
            {
                if (s == null) return "# 使い方\n\n利用の手引きを読み込めませんでした。同梱の「利用の手引き.txt」を参照してください。";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }

    /// <summary>Minimal Markdown → FlowDocument: headings, paragraphs, bullet / numbered lists, tables, **bold**, `code`.</summary>
    public static class MarkdownFlow
    {
        private static readonly Regex Inline = new Regex(@"(\*\*[^*]+\*\*|`[^`]+`)");

        public static FlowDocument Build(string markdown)
        {
            var doc = new FlowDocument();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                string t = line.Trim();
                if (t.Length == 0) { i++; continue; }

                if (t.StartsWith("#"))
                {
                    int level = t.TakeWhile(c => c == '#').Count();
                    var p = new Paragraph { Margin = new Thickness(0, level == 1 ? 0 : 18, 0, 8), FontWeight = FontWeights.SemiBold, FontSize = level == 1 ? 26 : level == 2 ? 19 : 16 };
                    AddInline(p.Inlines, t.Substring(level).Trim());
                    doc.Blocks.Add(p);
                    i++;
                    continue;
                }

                if (t.StartsWith("|"))
                {
                    var rows = new List<string[]>();
                    while (i < lines.Length && lines[i].Trim().StartsWith("|"))
                    {
                        var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                        if (!cells.All(c => Regex.IsMatch(c, @"^:?-{3,}:?$"))) rows.Add(cells);
                        i++;
                    }
                    doc.Blocks.Add(BuildTable(rows));
                    continue;
                }

                bool bullet = Regex.IsMatch(t, @"^[-*] "), numbered = Regex.IsMatch(t, @"^\d+\. ");
                if (bullet || numbered)
                {
                    var list = new List { MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(24, 0, 0, 0) };
                    while (i < lines.Length)
                    {
                        string lt = lines[i].Trim();
                        bool isItem = numbered ? Regex.IsMatch(lt, @"^\d+\. ") : Regex.IsMatch(lt, @"^[-*] ");
                        if (!isItem) break;
                        var text = new StringBuilder(numbered ? Regex.Replace(lt, @"^\d+\. ", "") : lt.Substring(2));
                        i++;
                        while (i < lines.Length && lines[i].StartsWith("  ") && lines[i].Trim().Length > 0) { text.Append(Join(text.ToString(), lines[i].Trim())); i++; }
                        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                        AddInline(p.Inlines, text.ToString());
                        list.ListItems.Add(new ListItem(p));
                    }
                    doc.Blocks.Add(list);
                    continue;
                }

                var para = new StringBuilder(t);
                i++;
                while (i < lines.Length)
                {
                    string nt = lines[i].Trim();
                    if (nt.Length == 0 || nt.StartsWith("#") || nt.StartsWith("|") || Regex.IsMatch(nt, @"^([-*]|\d+\.) ")) break;
                    para.Append(Join(para.ToString(), nt));
                    i++;
                }
                var pp = new Paragraph { Margin = new Thickness(0, 0, 0, 10), LineHeight = 22 };
                AddInline(pp.Inlines, para.ToString());
                doc.Blocks.Add(pp);
            }
            return doc;
        }

        /// <summary>Joins wrapped lines: no space between Japanese characters, one space otherwise.</summary>
        private static string Join(string before, string next) =>
            before.Length > 0 && (before[before.Length - 1] > 0x2E7F || (next.Length > 0 && next[0] > 0x2E7F)) ? next : " " + next;

        private static Table BuildTable(List<string[]> rows)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 14), BorderThickness = new Thickness(1) };
            table.SetResourceReference(Table.BorderBrushProperty, "Stroke.Divider");
            int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = new TableRow();
                if (r == 0) row.SetResourceReference(TableRow.BackgroundProperty, "Bg.Subtle.Hover");
                for (int c = 0; c < cols; c++)
                {
                    var p = new Paragraph { Margin = new Thickness(0), FontSize = 13 };
                    if (r == 0) p.FontWeight = FontWeights.SemiBold;
                    AddInline(p.Inlines, c < rows[r].Length ? rows[r][c] : "");
                    var cell = new TableCell(p) { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "Stroke.Divider");
                    row.Cells.Add(cell);
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static void AddInline(InlineCollection inlines, string text)
        {
            foreach (var part in Inline.Split(text))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4) inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
                else if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                {
                    var run = new Run(part.Substring(1, part.Length - 2)) { FontFamily = new FontFamily("Consolas, MS Gothic") };
                    run.SetResourceReference(TextElement.BackgroundProperty, "Bg.Subtle.Hover");
                    inlines.Add(run);
                }
                else inlines.Add(new Run(part));
            }
        }
    }
}
