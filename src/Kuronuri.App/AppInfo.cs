namespace Kuronuri.App
{
    /// <summary>
    /// Name and version of the app (ARCHITECTURE §11.8 "版の表示"). The version comes only from the assembly, which gets it
    /// from Directory.Build.props.
    /// </summary>
    public static class AppInfo
    {
        public const string Name = "公文書くろぬり";

        /// <summary>"0.9.2".</summary>
        public static readonly string Version = typeof(AppInfo).Assembly.GetName().Version is { } v ? v.Major + "." + v.Minor + "." + v.Build : "0.0.0";

        /// <summary>0.x is a preview.</summary>
        public static bool IsPreview => Version.StartsWith("0.", System.StringComparison.Ordinal);

        /// <summary>"版 0.9.2（プレビュー）".</summary>
        public static string VersionLabel => "版 " + Version + (IsPreview ? "（プレビュー）" : "");

        /// <summary>"公文書くろぬり 0.9.2" (window titles).</summary>
        public static string NameWithVersion => Name + " " + Version;
    }
}
