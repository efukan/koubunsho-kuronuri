using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Settings
{
    /// <summary>
    /// settings.json next to the EXE (ARCHITECTURE §12). Holds no document information (no recent files, no paths).
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Name = "app", Order = 0)] public string App = "kuronuri";
        [DataMember(Name = "schema", Order = 1)] public int Schema = 1;
        [DataMember(Name = "reasons", Order = 2)] public List<ReasonPreset> Reasons = DefaultReasons();
        [DataMember(Name = "exportDpi", Order = 3)] public int ExportDpi = 200;
        [DataMember(Name = "jpegQuality", Order = 4)] public int JpegQuality = 85;
        [DataMember(Name = "defaultStyle", Order = 5)] public string DefaultStyle = "black";
        [DataMember(Name = "brushWidthsMm", Order = 6)] public List<double> BrushWidthsMm = new List<double> { 2, 4, 6, 10 };
        [DataMember(Name = "printDpi", Order = 7)] public int PrintDpi = 300;
        [DataMember(Name = "window", Order = 8, EmitDefaultValue = false)] public WindowLayout? Window;

        public static readonly int[] ExportDpiChoices = { 150, 200, 300 };
        public static readonly int[] PrintDpiChoices = { 200, 300 };

        public static List<ReasonPreset> DefaultReasons() => new List<ReasonPreset>
        {
            new ReasonPreset("1号", "個人情報"),
            new ReasonPreset("2号", "法人等情報"),
            new ReasonPreset("3号", "公共安全情報"),
            new ReasonPreset("4号", "審議・検討情報"),
            new ReasonPreset("5号", "事務事業情報"),
            new ReasonPreset("6号", "法令秘情報"),
        };

        public RedactionStyle Style => DefaultStyle == "whiteBorder" ? RedactionStyle.WhiteWithBorder : RedactionStyle.Black;

        /// <summary>Replaces out-of-range or missing values with defaults so a hand-edited file can never break the app.</summary>
        public AppSettings Normalize()
        {
            var d = new AppSettings();
            Reasons = (Reasons ?? d.Reasons).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Code)).Select(r => new ReasonPreset(r.Code.Trim(), (r.Label ?? "").Trim())).ToList();
            if (!ExportDpiChoices.Contains(ExportDpi)) ExportDpi = d.ExportDpi;
            if (JpegQuality < 50 || JpegQuality > 100) JpegQuality = d.JpegQuality;
            if (DefaultStyle != "black" && DefaultStyle != "whiteBorder") DefaultStyle = d.DefaultStyle;
            BrushWidthsMm = (BrushWidthsMm ?? d.BrushWidthsMm).Where(w => w >= 0.5 && w <= 50).Distinct().OrderBy(w => w).ToList();
            if (BrushWidthsMm.Count == 0) BrushWidthsMm = d.BrushWidthsMm;
            if (!PrintDpiChoices.Contains(PrintDpi)) PrintDpi = d.PrintDpi;
            if (Window != null && !Window.IsUsable()) Window = null;
            App = "kuronuri"; Schema = 1;
            return this;
        }

        public AppSettings Clone() => Parse(Serialize());

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, System.Text.Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(w, this);
                return ms.ToArray();
            }
        }

        public static AppSettings Parse(byte[] utf8)
        {
            using (var ms = new MemoryStream(utf8))
            {
                var s = (AppSettings?)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(ms);
                if (s == null || s.App != "kuronuri") throw new InvalidDataException("not a kuronuri settings file");
                return s.Normalize();
            }
        }

        // DataContractJsonSerializer does not run field initializers; make deserialized objects start from defaults.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext c)
        {
            var d = new AppSettings(false);
            App = d.App; Schema = d.Schema; Reasons = d.Reasons; ExportDpi = d.ExportDpi; JpegQuality = d.JpegQuality;
            DefaultStyle = d.DefaultStyle; BrushWidthsMm = d.BrushWidthsMm; PrintDpi = d.PrintDpi; Window = null;
        }

        public AppSettings() { }
        private AppSettings(bool _) { }
    }

    [DataContract]
    public sealed class ReasonPreset
    {
        [DataMember(Name = "code", Order = 0)] public string Code = "";
        [DataMember(Name = "label", Order = 1)] public string Label = "";

        public ReasonPreset() { }
        public ReasonPreset(string code, string label) { Code = code; Label = label; }

        public Reason ToReason(string freeText = "") => new Reason(Code, Label, freeText);
        public override string ToString() => Code + " " + Label;
    }

    /// <summary>Reads / writes settings.json. Never throws: an unreadable or unwritable file falls back to defaults.</summary>
    public static class SettingsStore
    {
        public const string FileName = "settings.json";

        public static string DefaultPath() =>
            Path.Combine(Path.GetDirectoryName(typeof(SettingsStore).Assembly.Location)!, FileName);

        public static AppSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new AppSettings();
                return AppSettings.Parse(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is InvalidDataException || e is ArgumentException)
            {
                return new AppSettings();
            }
        }

        /// <returns>false when the folder is not writable (no exception).</returns>
        public static bool TrySave(string path, AppSettings settings)
        {
            try
            {
                byte[] data = settings.Clone().Serialize();
                File.WriteAllBytes(path, data);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is NotSupportedException)
            {
                return false;
            }
        }
    }
}

namespace Kuronuri.Core.Settings
{
    /// <summary>
    /// Window placement and pane layout (ARCHITECTURE §12). Units are WPF DIPs. Holds no document information.
    /// </summary>
    [DataContract]
    public sealed class WindowLayout
    {
        [DataMember(Name = "left", Order = 0)] public double Left;
        [DataMember(Name = "top", Order = 1)] public double Top;
        [DataMember(Name = "width", Order = 2)] public double Width;
        [DataMember(Name = "height", Order = 3)] public double Height;
        [DataMember(Name = "maximized", Order = 4)] public bool Maximized;
        [DataMember(Name = "thumbsWidth", Order = 5)] public double ThumbsWidth = 160;
        [DataMember(Name = "listWidth", Order = 6)] public double ListWidth = 320;
        [DataMember(Name = "showThumbs", Order = 7)] public bool ShowThumbs = true;
        [DataMember(Name = "showList", Order = 8)] public bool ShowList = true;
        [DataMember(Name = "twoPane", Order = 9)] public bool TwoPane = true;

        public bool IsUsable() =>
            Finite(Left) && Finite(Top) && Width >= 400 && Height >= 300 && Width <= 20000 && Height <= 20000 &&
            ThumbsWidth >= 60 && ThumbsWidth <= 1000 && ListWidth >= 120 && ListWidth <= 2000;

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 100000;

        /// <summary>True when at least half of the window lies inside the given screen area (virtual screen).</summary>
        public bool MostlyInside(double areaLeft, double areaTop, double areaWidth, double areaHeight)
        {
            double l = Math.Max(Left, areaLeft), t = Math.Max(Top, areaTop);
            double r = Math.Min(Left + Width, areaLeft + areaWidth), b = Math.Min(Top + Height, areaTop + areaHeight);
            return r > l && b > t && (r - l) * (b - t) >= 0.5 * Width * Height;
        }
    }
}
