using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;


namespace Kuronuri.Core.Model
{
    public readonly record struct Point2(double X, double Y);
    public abstract record Shape;
    public sealed record RectShape(double X, double Y, double W, double H) : Shape;
    public sealed record StrokeShape(IReadOnlyList<Point2> Points, double WidthPt) : Shape;
    public sealed record Reason(string Code, string Label, string FreeText);
    public sealed record RedactionItem(Guid Id, int PageIndex, IReadOnlyList<Shape> Shapes, Reason? Reason);
    public enum RedactionStyle { Black, WhiteWithBorder }
    public sealed record PageInfo(double Width, double Height, int Rotation);
    public sealed record OriginalInfo(string Sha256, long Length, string FileName);
    public sealed record RedactionProject(OriginalInfo Original, IReadOnlyList<PageInfo> Pages, IReadOnlyList<RedactionItem> Items, RedactionStyle Style,
        PageLayout? Layout = null)
    {
        /// <summary>The page layout, or the identity layout when none was set (ARCHITECTURE §4.4).</summary>
        public PageLayout EffectiveLayout => Layout ?? PageLayout.Identity(Pages.Count);
    }

    public static class ShapeMath
    {
        /// <summary>Bounding box in display page space: (left, top, right, bottom).</summary>
        public static (double L, double T, double R, double B) Bounds(Shape s)
        {
            if (s is RectShape r) return (r.X, r.Y, r.X + r.W, r.Y + r.H);
            var st = (StrokeShape)s;
            double h = st.WidthPt / 2;
            return (st.Points.Min(p => p.X) - h, st.Points.Min(p => p.Y) - h, st.Points.Max(p => p.X) + h, st.Points.Max(p => p.Y) + h);
        }

        public static (double L, double T, double R, double B) Bounds(RedactionItem item)
        {
            var b = item.Shapes.Select(Bounds).ToList();
            return (b.Min(x => x.L), b.Min(x => x.T), b.Max(x => x.R), b.Max(x => x.B));
        }

        /// <summary>Display order used for numbering: page, then top, then left.</summary>
        public static List<RedactionItem> InDisplayOrder(IEnumerable<RedactionItem> items) =>
            items.OrderBy(i => i.PageIndex).ThenBy(i => Bounds(i).T).ThenBy(i => Bounds(i).L).ToList();
    }

    // ---------- JSON (DataContractJsonSerializer; no extra dependency) ----------

    [DataContract] public sealed class ProjectDto
    {
        [DataMember(Name = "app", Order = 0)] public string App = "kuronuri";
        [DataMember(Name = "schema", Order = 1)] public int Schema = ProjectJson.CurrentSchema;
        [DataMember(Name = "appVersion", Order = 2)] public string AppVersion = "";
        [DataMember(Name = "original", Order = 3)] public OriginalDto Original = new OriginalDto();
        [DataMember(Name = "pages", Order = 4)] public List<PageDto> Pages = new List<PageDto>();
        [DataMember(Name = "style", Order = 5)] public string Style = "black";
        [DataMember(Name = "items", Order = 6)] public List<ItemDto> Items = new List<ItemDto>();
        [DataMember(Name = "layout", Order = 7, EmitDefaultValue = false)] public LayoutDto? Layout;
    }
    [DataContract] public sealed class OriginalDto
    {
        [DataMember(Name = "sha256", Order = 0)] public string Sha256 = "";
        [DataMember(Name = "length", Order = 1)] public long Length;
        [DataMember(Name = "fileName", Order = 2)] public string FileName = "";
    }
    [DataContract] public sealed class PageDto
    {
        [DataMember(Name = "w", Order = 0)] public double W;
        [DataMember(Name = "h", Order = 1)] public double H;
        [DataMember(Name = "rot", Order = 2)] public int Rot;
    }
    [DataContract] public sealed class ItemDto
    {
        [DataMember(Name = "id", Order = 0)] public string Id = "";
        [DataMember(Name = "page", Order = 1)] public int Page;
        [DataMember(Name = "reason", Order = 2)] public ReasonDto? Reason;
        [DataMember(Name = "shapes", Order = 3)] public List<ShapeDto> Shapes = new List<ShapeDto>();
    }
    [DataContract] public sealed class ReasonDto
    {
        [DataMember(Name = "code", Order = 0)] public string Code = "";
        [DataMember(Name = "label", Order = 1)] public string Label = "";
        [DataMember(Name = "text", Order = 2)] public string Text = "";
    }
    [DataContract] public sealed class LayoutDto
    {
        [DataMember(Name = "order", Order = 0)] public int[] Order = new int[0];
        [DataMember(Name = "rot", Order = 1)] public int[] Rot = new int[0];
        [DataMember(Name = "excluded", Order = 2)] public bool[] Excluded = new bool[0];
    }
    [DataContract] public sealed class ShapeDto
    {
        [DataMember(Name = "t", Order = 0)] public string T = "";                              // "rect" | "stroke"
        [DataMember(Name = "x", Order = 1, EmitDefaultValue = false)] public double? X;
        [DataMember(Name = "y", Order = 2, EmitDefaultValue = false)] public double? Y;
        [DataMember(Name = "w", Order = 3, EmitDefaultValue = false)] public double? W;
        [DataMember(Name = "h", Order = 4, EmitDefaultValue = false)] public double? H;
        [DataMember(Name = "sw", Order = 5, EmitDefaultValue = false)] public double? Sw;      // stroke width (pt)
        [DataMember(Name = "pts", Order = 6, EmitDefaultValue = false)] public double[]? Pts;  // x0,y0,x1,y1,...
    }

    public static class ProjectJson
    {
        /// <summary>Written schema. Schema 1 (no page layout) is still read (ARCHITECTURE §4.4).</summary>
        public const int CurrentSchema = 2;

        public static byte[] Serialize(RedactionProject p, string appVersion)
        {
            var dto = new ProjectDto
            {
                AppVersion = appVersion,
                Original = new OriginalDto { Sha256 = p.Original.Sha256, Length = p.Original.Length, FileName = p.Original.FileName },
                Pages = p.Pages.Select(x => new PageDto { W = x.Width, H = x.Height, Rot = x.Rotation }).ToList(),
                Style = p.Style == RedactionStyle.Black ? "black" : "whiteBorder",
                Items = p.Items.Select(i => new ItemDto
                {
                    Id = i.Id.ToString("D"),
                    Page = i.PageIndex,
                    Reason = i.Reason == null ? null : new ReasonDto { Code = i.Reason.Code, Label = i.Reason.Label, Text = i.Reason.FreeText },
                    Shapes = i.Shapes.Select(ToDto).ToList(),
                }).ToList(),
                Layout = p.Layout == null ? null : new LayoutDto
                {
                    Order = p.Layout.Order.ToArray(),
                    Rot = p.Layout.Rotations.ToArray(),
                    Excluded = p.Layout.Excluded.ToArray(),
                },
            };
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ProjectDto)).WriteObject(ms, dto);
                return ms.ToArray();   // UTF-8
            }
        }

        public static RedactionProject Deserialize(byte[] utf8)
        {
            ProjectDto dto;
            using (var ms = new MemoryStream(utf8))
                dto = (ProjectDto)new DataContractJsonSerializer(typeof(ProjectDto)).ReadObject(ms);
            if (dto == null || dto.App != "kuronuri" || dto.Schema < 1 || dto.Schema > CurrentSchema) throw new InvalidDataException("unsupported project data");
            if (dto.Original == null || dto.Pages == null || dto.Items == null) throw new InvalidDataException("incomplete project data");
            var ids = new HashSet<string>();
            foreach (var i in dto.Items)
            {
                if (i == null || i.Shapes == null || i.Shapes.Count == 0 || i.Page < 0 || i.Page >= dto.Pages.Count || !ids.Add(i.Id ?? ""))
                    throw new InvalidDataException("invalid item");
                if (!Guid.TryParse(i.Id, out _)) throw new InvalidDataException("invalid item id");
            }
            PageLayout? layout = null;
            if (dto.Layout != null)
            {
                if (dto.Layout.Order == null || dto.Layout.Rot == null || dto.Layout.Excluded == null) throw new InvalidDataException("invalid layout");
                layout = new PageLayout(dto.Layout.Order, dto.Layout.Rot, dto.Layout.Excluded);
                if (!layout.IsValidFor(dto.Pages.Count)) throw new InvalidDataException("invalid layout");
            }
            return new RedactionProject(
                new OriginalInfo(dto.Original.Sha256, dto.Original.Length, dto.Original.FileName),
                dto.Pages.Select(x => new PageInfo(x.W, x.H, x.Rot)).ToList(),
                dto.Items.Select(i => new RedactionItem(
                    Guid.Parse(i.Id), i.Page, i.Shapes.Select(FromDto).ToList(),
                    i.Reason == null ? null : new Reason(i.Reason.Code, i.Reason.Label, i.Reason.Text))).ToList(),
                dto.Style == "whiteBorder" ? RedactionStyle.WhiteWithBorder : RedactionStyle.Black,
                layout);
        }

        private static ShapeDto ToDto(Shape s)
        {
            if (s is RectShape r) return new ShapeDto { T = "rect", X = r.X, Y = r.Y, W = r.W, H = r.H };
            var st = (StrokeShape)s;
            return new ShapeDto { T = "stroke", Sw = st.WidthPt, Pts = st.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray() };
        }

        private static Shape FromDto(ShapeDto d)
        {
            if (d.T == "rect") return new RectShape(d.X ?? 0, d.Y ?? 0, d.W ?? 0, d.H ?? 0);
            if (d.T == "stroke" && d.Pts != null && d.Pts.Length >= 2 && d.Pts.Length % 2 == 0)
            {
                var pts = new List<Point2>();
                for (int i = 0; i < d.Pts.Length; i += 2) pts.Add(new Point2(d.Pts[i], d.Pts[i + 1]));
                return new StrokeShape(pts, d.Sw ?? 0);
            }
            throw new InvalidDataException("unknown shape");
        }
    }
}
