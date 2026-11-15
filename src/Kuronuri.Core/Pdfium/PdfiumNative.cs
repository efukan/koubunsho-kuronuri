// PDFium P/Invoke declarations (x64 only, so no calling-convention attribute is needed on functions).
// FPDF_BOOL = int, unsigned long = uint (Windows), FPDF_WIDESTRING = UTF-16LE null-terminated.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Kuronuri.Core.Pdfium
{
    public static class PdfiumNative
    {
        private const string Dll = "pdfium";

        // ---- constants
        public const int FPDFBitmap_Gray = 1, FPDFBitmap_BGR = 2, FPDFBitmap_BGRx = 3, FPDFBitmap_BGRA = 4;
        public const int FPDF_ANNOT = 0x01, FPDF_LCD_TEXT = 0x02, FPDF_GRAYSCALE = 0x08, FPDF_PRINTING = 0x800;
        public const uint FPDF_INCREMENTAL = 1, FPDF_NO_INCREMENTAL = 2;
        public const int FPDF_PAGEOBJ_TEXT = 1, FPDF_PAGEOBJ_PATH = 2, FPDF_PAGEOBJ_IMAGE = 3, FPDF_PAGEOBJ_SHADING = 4, FPDF_PAGEOBJ_FORM = 5;
        public const int FPDF_FILLMODE_NONE = 0, FPDF_FILLMODE_ALTERNATE = 1, FPDF_FILLMODE_WINDING = 2;
        public const int FPDF_LINECAP_ROUND = 1, FPDF_LINEJOIN_ROUND = 1;
        public const uint FPDF_ERR_SUCCESS = 0, FPDF_ERR_UNKNOWN = 1, FPDF_ERR_FILE = 2, FPDF_ERR_FORMAT = 3, FPDF_ERR_PASSWORD = 4, FPDF_ERR_SECURITY = 5, FPDF_ERR_PAGE = 6;

        // ---- structs / callbacks
        [StructLayout(LayoutKind.Sequential)]
        public struct FS_RECTF { public float Left, Top, Right, Bottom; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetBlockCallback(IntPtr param, uint position, IntPtr buffer, uint size);

        [StructLayout(LayoutKind.Sequential)]
        public struct FPDF_FILEACCESS { public uint FileLen; public IntPtr GetBlock; public IntPtr Param; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int WriteBlockCallback(IntPtr self, IntPtr data, uint size);

        [StructLayout(LayoutKind.Sequential)]
        public struct FPDF_FILEWRITE { public int Version; public IntPtr WriteBlock; }

        // ---- kernel32
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        public const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        // ---- fpdfview.h
        [DllImport(Dll)] public static extern void FPDF_InitLibrary();
        [DllImport(Dll)] public static extern void FPDF_DestroyLibrary();
        [DllImport(Dll)] public static extern uint FPDF_GetLastError();
        [DllImport(Dll)] public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string? password);
        [DllImport(Dll)] public static extern IntPtr FPDF_LoadCustomDocument(IntPtr fileAccess, [MarshalAs(UnmanagedType.LPStr)] string? password);
        [DllImport(Dll)] public static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Dll)] public static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Dll)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
        [DllImport(Dll)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Dll)] public static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_GetPageBoundingBox(IntPtr page, out FS_RECTF rect);
        [DllImport(Dll)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, double pageX, double pageY, out int deviceX, out int deviceY);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_DeviceToPage(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int deviceX, int deviceY, out double pageX, out double pageY);
        [DllImport(Dll)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(Dll)] public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Dll)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        // ---- fpdf_formfill.h
        public const int FORMTYPE_NONE = 0, FORMTYPE_ACRO_FORM = 1, FORMTYPE_XFA_FULL = 2, FORMTYPE_XFA_FOREGROUND = 3;
        [DllImport(Dll)] public static extern int FPDF_GetFormType(IntPtr document);

        // ---- fpdf_doc.h / fpdf_text.h / fpdf_annot.h
        [DllImport(Dll)] public static extern uint FPDF_GetMetaText(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string tag, [Out] byte[]? buffer, uint buflen);
        [DllImport(Dll)] public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);
        [DllImport(Dll)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Dll)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Dll)] public static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Dll)] public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, [Out] byte[] result);
        [DllImport(Dll)] public static extern int FPDFPage_GetAnnotCount(IntPtr page);

        // ---- fpdf_edit.h
        [DllImport(Dll)] public static extern IntPtr FPDF_CreateNewDocument();
        [DllImport(Dll)] public static extern int FPDFPage_GetRotation(IntPtr page);   // 0,1,2,3 = 0,90,180,270 (clockwise)
        [DllImport(Dll)] public static extern int FPDFPage_CountObjects(IntPtr page);
        [DllImport(Dll)] public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
        [DllImport(Dll)] public static extern int FPDFPageObj_GetType(IntPtr pageObject);
        [DllImport(Dll)] public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPage_GenerateContent(IntPtr page);
        [DllImport(Dll)] public static extern IntPtr FPDFPageObj_CreateNewPath(float x, float y);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPath_MoveTo(IntPtr path, float x, float y);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPath_LineTo(IntPtr path, float x, float y);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPath_BezierTo(IntPtr path, float x1, float y1, float x2, float y2, float x3, float y3);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPath_Close(IntPtr path);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPath_SetDrawMode(IntPtr path, int fillMode, [MarshalAs(UnmanagedType.Bool)] bool stroke);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetFillColor(IntPtr pageObject, uint r, uint g, uint b, uint a);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetStrokeColor(IntPtr pageObject, uint r, uint g, uint b, uint a);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetStrokeWidth(IntPtr pageObject, float width);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetLineCap(IntPtr pageObject, int lineCap);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFPageObj_SetLineJoin(IntPtr pageObject, int lineJoin);

        [DllImport(Dll)] public static extern IntPtr FPDFPage_New(IntPtr document, int pageIndex, double width, double height);
        [DllImport(Dll)] public static extern void FPDFPageObj_Transform(IntPtr pageObject, double a, double b, double c, double d, double e, double f);
        [DllImport(Dll)] public static extern IntPtr FPDF_NewXObjectFromPage(IntPtr destDoc, IntPtr srcDoc, int srcPageIndex);
        [DllImport(Dll)] public static extern void FPDF_CloseXObject(IntPtr xobject);
        [DllImport(Dll)] public static extern IntPtr FPDF_NewFormObjectFromXObject(IntPtr xobject);

        // ---- fpdf_ppo.h / fpdf_save.h / fpdf_attachment.h
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_ImportPages(IntPtr destDoc, IntPtr srcDoc, [MarshalAs(UnmanagedType.LPStr)] string? pageRange, int index);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDF_SaveAsCopy(IntPtr document, ref FPDF_FILEWRITE fileWrite, uint flags);
        [DllImport(Dll)] public static extern int FPDFDoc_GetAttachmentCount(IntPtr document);
        [DllImport(Dll)] public static extern IntPtr FPDFDoc_AddAttachment(IntPtr document, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [DllImport(Dll)] public static extern IntPtr FPDFDoc_GetAttachment(IntPtr document, int index);
        [DllImport(Dll)] public static extern uint FPDFAttachment_GetName(IntPtr attachment, [Out] byte[]? buffer, uint buflen);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFAttachment_SetFile(IntPtr attachment, IntPtr document, IntPtr contents, uint len);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FPDFAttachment_GetFile(IntPtr attachment, IntPtr buffer, uint buflen, out uint outBuflen);
    }
}
