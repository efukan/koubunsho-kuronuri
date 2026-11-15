using System;

namespace Kuronuri.Core.Pdfium
{
    /// <summary>Why a PDF could not be opened. Carries no path, file name or document content.</summary>
    public enum PdfErrorKind
    {
        Unknown,
        File,
        Format,
        PasswordRequired,
        UnsupportedSecurity,
        Page,
        TooLarge,
        NoPages,
    }

    public sealed class PdfLoadException : Exception
    {
        public PdfErrorKind Kind { get; }

        public PdfLoadException(PdfErrorKind kind) : base("PDF load failed: " + kind) { Kind = kind; }

        public static PdfErrorKind FromPdfiumError(uint error) => error switch
        {
            PdfiumNative.FPDF_ERR_FILE => PdfErrorKind.File,
            PdfiumNative.FPDF_ERR_FORMAT => PdfErrorKind.Format,
            PdfiumNative.FPDF_ERR_PASSWORD => PdfErrorKind.PasswordRequired,
            PdfiumNative.FPDF_ERR_SECURITY => PdfErrorKind.UnsupportedSecurity,
            PdfiumNative.FPDF_ERR_PAGE => PdfErrorKind.Page,
            _ => PdfErrorKind.Unknown,
        };

        /// <summary>Japanese message for the UI (kind only, never the file name).</summary>
        public static string Describe(PdfErrorKind kind) => kind switch
        {
            PdfErrorKind.File => "ファイルを読み込めませんでした。",
            PdfErrorKind.Format => "PDFとして読み込めませんでした（壊れているか、PDFではありません）。",
            PdfErrorKind.PasswordRequired => "パスワードで保護されたPDFには対応していません。",
            PdfErrorKind.UnsupportedSecurity => "このPDFの暗号化方式には対応していません。",
            PdfErrorKind.Page => "ページを読み込めませんでした。",
            PdfErrorKind.TooLarge => "4GB 以上のPDFには対応していません。",
            PdfErrorKind.NoPages => "ページが1つもないPDFは開けません。",
            _ => "PDFを読み込めませんでした。",
        };
    }
}
