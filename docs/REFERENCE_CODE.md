# 実証済み参照コード

このファイルは技術検証（PoC）で**実際にビルド・実行して全項目合格したソース**を、そのまま機械的に貼り付けたもの。手で書き写していないので、1文字単位で動作確認済みのコードと一致する。

- 環境: .NET Framework 4.8（x64）、`bblanchon.PDFium.Win32` 159.0.8152、.NET SDK 10.0.404 でビルド、2026-11-13 実行。
- 使い方: 各ファイルを表の移設先へコピーして実装を始める。名前空間は製品用（`Kuronuri.Core.*`）にしてある。
- 設計の説明は `ARCHITECTURE.md` を参照。

| ファイル | 移設先 | 内容 |
|---|---|---|
| `Kuronuri.Poc.csproj` | 各 csproj の雛形 | プロジェクト設定と参照 |
| `PdfiumNative.cs` | src/Kuronuri.Core/Pdfium/ | P/Invoke 宣言、DLL ロード、`PdfDoc`（メモリ / ストリーム読込、保存） |
| `Model.cs` | src/Kuronuri.Core/Model/ | record 群、`ShapeMath`、JSON の DTO と `ProjectJson` |
| `Geometry.cs` | src/Kuronuri.Core/Geometry/ | `PageGeometry`、`CoordMap`、`Burner` |
| `MiniPdfWriter.cs` | src/Kuronuri.Core/Export/ | 最小 PDF ライター、zlib 圧縮 |
| `FinalExporter.cs` | src/Kuronuri.Core/Export/ | `NativeBuffer`、`PageRenderer`、`Raster`、`JpegCodec`、`LosslessCodec`、`FinalExporter` |
| `OutputVerifier.cs` | src/Kuronuri.Core/Export/ | 確定出力の構造検証と PDFium 再オープン検証 |
| `Hybrid.cs` | src/Kuronuri.Core/Hybrid/ | `TextOutline`、`HybridWriter`、`HybridReader`、`HybridRasterWriter` |
| `PrintPaginator.cs` | src/Kuronuri.Core/Printing/ | `RedactedPaginator` |
| `TestPdfFactory.cs` | tests/Kuronuri.Core.Tests/ | テスト用 PDF の生成 |
| `Program.cs` | tests/Kuronuri.Core.Tests/（xUnit に分割） | 検証シナリオ一式。各 `Check` がテスト1件に対応する |

## PoC 実行ログ（最終実行・全項目合格）

`P10` と `P12` は採否を決めるための実験で、合否判定はない。サイズと時間は合成データ・開発機での値。

```text
P0   PASS pdfium.dll absolute-path load + init
P0   INFO pdfium.dll  | 7830016 bytes, fileVersion=159.0.8152.0, sha256=ba884cc5c6f2c3cd7c2ecca9dce36c2e341f78eb586abd691a2bbc017aa9871e
P0   INFO runtime  | CLR 4.0.30319.42000, x64=True
P0   INFO app folder  | 3 files, 7757 KB: Kuronuri.Poc.exe=110KB, Kuronuri.Poc.exe.config=0KB, pdfium.dll=7646KB
P1   PASS test PDF opens (memory loader), 5 pages  | pages=5
P1   PASS secrets are present in the original (text/meta/bookmark/attachment/annotation)  | text=True meta=True bookmark=True attachments=1 annots=1 rawSECRET=11
P1   PASS FPDF_LoadCustomDocument via read-only FileStream callback
P2   INFO page 1 geometry  | rot=0 box=[0 0 595 842] display=595x842
P2   PASS page 1 display size
P2   PASS page 1 marker pixels at formula positions
P2   PASS page 1 formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse
P2   INFO page 2 geometry  | rot=90 box=[0 0 595 842] display=842x595
P2   PASS page 2 display size
P2   PASS page 2 marker pixels at formula positions
P2   PASS page 2 formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse
P2   INFO page 3 geometry  | rot=180 box=[0 0 1191 842] display=1191x842
P2   PASS page 3 display size
P2   PASS page 3 marker pixels at formula positions
P2   PASS page 3 formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse
P2   INFO page 4 geometry  | rot=270 box=[120 80 675 862] display=782x555
P2   PASS page 4 display size
P2   PASS page 4 marker pixels at formula positions
P2   PASS page 4 formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse
P2   INFO page 5 geometry  | rot=0 box=[0 0 595 842] display=595x842
P2   PASS page 5 display size
P2   PASS page 5 marker pixels at formula positions
P2   PASS page 5 formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse
P4   INFO [Black, JPEG only] export 5 pages @200dpi q85  | 391 KB, 182 ms, 1653x2339c/jpeg:69KB 2339x1653c/jpeg:66KB 3309x2339c/jpeg:125KB 2173x1542c/jpeg:59KB 1653x2339c/jpeg:67KB
P4   PASS [Black, JPEG only] codec choice as expected
P3   PASS [Black, JPEG only] burn verified on the pixel buffer (export did not throw)
P5   PASS [Black, JPEG only] structure whitelist (objects = 2+3N, contiguous, nothing else)
P5   PASS [Black, JPEG only] PDFium re-open: no text/annots/attachments/bookmarks/metadata, 1 image per page, burned pixels
P5   PASS [Black, JPEG only] raw byte search for SECRET (ASCII/UTF-16)  | count=0
P4   PASS [Black, JPEG only] colour pages detected as colour
P5   PASS final output: unredacted markers kept at the same display positions (all rotations)
P5   PASS final output: stroke burned over the blue marker, white just outside the rectangle
P5   PASS verifier rejects bytes appended after EOF
P5   PASS verifier rejects an output whose redactions were not burned
P4   INFO [Black, auto codec] export 5 pages @200dpi q85  | 87 KB, 875 ms, 1653x2339c/flate:13KB 2339x1653c/flate:17KB 3309x2339c/flate:24KB 2173x1542c/flate:15KB 1653x2339c/flate:13KB
P4   PASS [Black, auto codec] codec choice as expected
P3   PASS [Black, auto codec] burn verified on the pixel buffer (export did not throw)
P5   PASS [Black, auto codec] structure whitelist (objects = 2+3N, contiguous, nothing else)
P5   PASS [Black, auto codec] PDFium re-open: no text/annots/attachments/bookmarks/metadata, 1 image per page, burned pixels
P5   PASS [Black, auto codec] raw byte search for SECRET (ASCII/UTF-16)  | count=0
P4   PASS [Black, auto codec] colour pages detected as colour
P5   PASS final output: unredacted markers kept at the same display positions (all rotations)
P5   PASS final output: stroke burned over the blue marker, white just outside the rectangle
P5   PASS verifier rejects bytes appended after EOF
P5   PASS verifier rejects an output whose redactions were not burned
P4   INFO [WhiteWithBorder, auto codec] export 5 pages @200dpi q85  | 84 KB, 849 ms, 1653x2339c/flate:13KB 2339x1653c/flate:16KB 3309x2339c/flate:24KB 2173x1542c/flate:14KB 1653x2339c/flate:13KB
P4   PASS [WhiteWithBorder, auto codec] codec choice as expected
P3   PASS [WhiteWithBorder, auto codec] burn verified on the pixel buffer (export did not throw)
P5   PASS [WhiteWithBorder, auto codec] structure whitelist (objects = 2+3N, contiguous, nothing else)
P5   PASS [WhiteWithBorder, auto codec] PDFium re-open: no text/annots/attachments/bookmarks/metadata, 1 image per page, burned pixels
P5   PASS [WhiteWithBorder, auto codec] raw byte search for SECRET (ASCII/UTF-16)  | count=0
P4   PASS [WhiteWithBorder, auto codec] colour pages detected as colour
P4   INFO dense text page (A4, black text) @150dpi, auto codec  | 1240x1755 gray flate, 43 KB, 64 ms
P4   INFO dense text page (A4, black text) @200dpi, auto codec  | 1653x2339 gray flate, 671 KB, 187 ms
P4   PASS dense text page output (gray, lossless) passes structure + PDFium verification
P4   INFO dense text page (A4, black text) @300dpi, auto codec  | 2480x3509 gray flate, 1080 KB, 352 ms
P4   PASS black-text page detected as gray (/DeviceGray)
P4   PASS A0 page @300dpi is reduced under 60 MP automatically  | 6514x9209 = 59 MP, effective 196.753 dpi, 579 ms
P6   PASS attachments read back
P6   PASS embedded original is byte-identical (SHA-256)  | 3171 bytes
P6   PASS project JSON is byte-identical and deserializes to the same data
P6   INFO size  | original 3171 B, hybrid 7378 B (x2.327)
P6   PASS hybrid page count
P6   PASS imported pages keep size / rotation; text still extractable (searchable for reviewers)
P6   PASS translucent red overlays at the right display positions on all rotations (incl. unbalanced CTM page)
P6   PASS original markers unchanged in the hybrid
P6   PASS 3 re-save cycles: original hash stable, size does not grow  | 7378 -> 7378 -> 7378 -> 7378
P7   PASS own-writer EmbeddedFiles readable through PDFium; original byte-identical; JSON identical  | hybrid 264153 B (x83.303)
P7   PASS raster hybrid: overlays at the right positions on all pages
P8   PASS Japanese watermark + labels drawn as outline paths (pixels changed, no text objects, no fonts added)  | changed pixels=18041, chars 17->17, /Font 6->6
P8   INFO size cost of outlines  | 5922 bytes per page (watermark + 2 labels)
P8   PASS hybrid with text still round-trips the original
P8   PASS shared Form XObject watermark renders the same as per-page paths on all rotations  | differing pixels per page: 0 0 0 0 0 
P8   INFO watermark size  | per-page paths 24826 B/page, shared XObject 5922 B/page (5 pages); /FlateDecode count=13
P9   INFO 300 pages @200dpi, GcEveryPages=0  | 57 ms/page, output 183 MB, working set MB @20/150/300 = 188/131/155, peak 526
P9   INFO memory flat without forced GC  | yes
P9   INFO 300 pages @200dpi, GcEveryPages=8  | 53 ms/page, output 183 MB, working set MB @20/150/300 = 142/114/144, peak 159
P9   PASS memory stays flat over 300 pages (with GcEveryPages=8)
P9   PASS 300-page output passes the structure whitelist
P10  INFO experiment: FPDF_INCREMENTAL save (classic-xref original only)  | original bytes kept as prefix=True, re-opens=True, appended 3511 bytes
P11  PASS DataContractJsonSerializer round-trip (doubles exact, Japanese, null reason)
P11  INFO JSON sample  | {"app":"kuronuri","schema":1,"appVersion":"1.0.0","original":{"sha256":"ab","length":1234567890123,"fileName":"名前 \"引用\" \\ \/.pdf"},"pages":[{"w":595.275573730469,"h":841.889770507813,"rot":90}],"style":"whiteBorder","items":[{"id":"e5622881-7f88-4ae4-b186-d7626a246259","page":0,"reason":{"code":"2号","label":"法人等情報","text":"自由記述\n改行"},"shapes":[{"t":"rect","x":0.30000000000000004,"y":1E-07,"w":123.45678901234568,"h":0.33333333333333331}]},{"id":"5d6b35f7-ee1b-4070-844b-e49bf2c2c99c","page":0,"reason":null,"shapes":[{"t":"stroke","sw":14.173228346456693,"pts":[1.5,2.25,3.1415926535897931,2.7182818284590451]}]}]}
P11  PASS records / with-expressions / value equality compile and work on net48
P11  PASS in-box Ribbon assembly resolves  | System.Windows.Controls.Ribbon 4.0.0.0
P12  INFO clean text page @200dpi  | JPEG q85 1021KB/23ms, Flate8 671KB/85ms, Flate8-fastest 727KB/45ms, Flate8-Up 784KB/92ms, Flate4bpp 37KB/22ms, Flate1bpp 11KB/7ms
P12  INFO noisy (scan-like) text page @200dpi  | JPEG q85 1219KB/22ms, Flate8 2130KB/516ms, Flate8-fastest 2277KB/114ms, Flate8-Up 2621KB/296ms, Flate4bpp 374KB/71ms, Flate1bpp 28KB/7ms
P13  PASS DocumentPaginator renders + burns each page on demand into an XPS (same path PrintDialog.PrintDocument uses)  | GetPage calls=5, XPS 62 KB
```

## Kuronuri.Poc.csproj

移設先: 各 csproj の雛形

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="bblanchon.PDFium.Win32" Version="159.0.8152" />
    <Reference Include="System.Runtime.Serialization" />
    <Reference Include="System.Xaml" />
    <Reference Include="WindowsBase" />
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System.Windows.Controls.Ribbon" />
    <Reference Include="ReachFramework" />
    <Reference Include="System.Printing" />
  </ItemGroup>
</Project>
```

## PdfiumNative.cs

移設先: src/Kuronuri.Core/Pdfium/

```csharp
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

    /// <summary>Loads pdfium.dll by absolute path from the application folder, then initializes it.</summary>
    public static class PdfiumLibrary
    {
        public static string DllPath { get; private set; } = "";

        public static void Load()
        {
            string dir = Path.GetDirectoryName(typeof(PdfiumLibrary).Assembly.Location)!;
            DllPath = Path.Combine(dir, "pdfium.dll");
            IntPtr h = PdfiumNative.LoadLibraryEx(DllPath, IntPtr.Zero, PdfiumNative.LOAD_WITH_ALTERED_SEARCH_PATH);
            if (h == IntPtr.Zero) throw new InvalidOperationException("pdfium.dll load failed: " + Marshal.GetLastWin32Error());
            PdfiumNative.FPDF_InitLibrary();
        }
    }

    /// <summary>Owns a PDFium document handle together with the memory / stream / delegate it depends on.</summary>
    public sealed class PdfDoc : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private GCHandle _pin;
        private IntPtr _fileAccess;
        private PdfiumNative.GetBlockCallback? _callback;   // must stay referenced while the document is open
        private Stream? _stream;

        private PdfDoc() { }

        public int PageCount => PdfiumNative.FPDF_GetPageCount(Handle);

        public static PdfDoc CreateNew()
        {
            var d = new PdfDoc { Handle = PdfiumNative.FPDF_CreateNewDocument() };
            if (d.Handle == IntPtr.Zero) throw new InvalidOperationException("FPDF_CreateNewDocument failed");
            return d;
        }

        /// <summary>The byte array is pinned until Dispose.</summary>
        public static PdfDoc FromBytes(byte[] data)
        {
            var d = new PdfDoc();
            d._pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            d.Handle = PdfiumNative.FPDF_LoadMemDocument64(d._pin.AddrOfPinnedObject(), (UIntPtr)(ulong)data.LongLength, null);
            if (d.Handle == IntPtr.Zero) { uint e = PdfiumNative.FPDF_GetLastError(); d.Dispose(); throw new InvalidOperationException("PDF load failed: error " + e); }
            return d;
        }

        /// <summary>Reads on demand through a callback. The stream is owned and closed by this object.</summary>
        public static PdfDoc FromStream(Stream stream)
        {
            var d = new PdfDoc { _stream = stream };
            byte[] chunk = new byte[64 * 1024];
            d._callback = (param, position, buffer, size) =>
            {
                try
                {
                    stream.Position = position;
                    uint done = 0;
                    while (done < size)
                    {
                        int n = stream.Read(chunk, 0, (int)Math.Min((uint)chunk.Length, size - done));
                        if (n <= 0) return 0;
                        Marshal.Copy(chunk, 0, IntPtr.Add(buffer, (int)done), n);
                        done += (uint)n;
                    }
                    return 1;
                }
                catch { return 0; }
            };
            var fa = new PdfiumNative.FPDF_FILEACCESS
            {
                FileLen = checked((uint)stream.Length),
                GetBlock = Marshal.GetFunctionPointerForDelegate(d._callback),
                Param = IntPtr.Zero,
            };
            d._fileAccess = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PdfiumNative.FPDF_FILEACCESS)));
            Marshal.StructureToPtr(fa, d._fileAccess, false);
            d.Handle = PdfiumNative.FPDF_LoadCustomDocument(d._fileAccess, null);
            if (d.Handle == IntPtr.Zero) { uint e = PdfiumNative.FPDF_GetLastError(); d.Dispose(); throw new InvalidOperationException("PDF load failed: error " + e); }
            return d;
        }

        public void SaveTo(Stream output, uint flags)
        {
            byte[] chunk = new byte[64 * 1024];
            PdfiumNative.WriteBlockCallback cb = (self, data, size) =>
            {
                uint done = 0;
                while (done < size)
                {
                    int n = (int)Math.Min((uint)chunk.Length, size - done);
                    Marshal.Copy(IntPtr.Add(data, (int)done), chunk, 0, n);
                    output.Write(chunk, 0, n);
                    done += (uint)n;
                }
                return 1;
            };
            var fw = new PdfiumNative.FPDF_FILEWRITE { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(cb) };
            bool ok = PdfiumNative.FPDF_SaveAsCopy(Handle, ref fw, flags);
            GC.KeepAlive(cb);
            if (!ok) throw new InvalidOperationException("FPDF_SaveAsCopy failed");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { PdfiumNative.FPDF_CloseDocument(Handle); Handle = IntPtr.Zero; }
            if (_pin.IsAllocated) _pin.Free();
            if (_fileAccess != IntPtr.Zero) { Marshal.FreeHGlobal(_fileAccess); _fileAccess = IntPtr.Zero; }
            _stream?.Dispose(); _stream = null;
            _callback = null;
        }
    }
}
```

## Model.cs

移設先: src/Kuronuri.Core/Model/

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

// Polyfill so that records / init accessors compile on net48.
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

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
    public sealed record RedactionProject(OriginalInfo Original, IReadOnlyList<PageInfo> Pages, IReadOnlyList<RedactionItem> Items, RedactionStyle Style);

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
        [DataMember(Name = "schema", Order = 1)] public int Schema = 1;
        [DataMember(Name = "appVersion", Order = 2)] public string AppVersion = "";
        [DataMember(Name = "original", Order = 3)] public OriginalDto Original = new OriginalDto();
        [DataMember(Name = "pages", Order = 4)] public List<PageDto> Pages = new List<PageDto>();
        [DataMember(Name = "style", Order = 5)] public string Style = "black";
        [DataMember(Name = "items", Order = 6)] public List<ItemDto> Items = new List<ItemDto>();
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
        public const int CurrentSchema = 1;

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
            if (dto.App != "kuronuri" || dto.Schema != CurrentSchema) throw new InvalidDataException("unsupported project data");
            return new RedactionProject(
                new OriginalInfo(dto.Original.Sha256, dto.Original.Length, dto.Original.FileName),
                dto.Pages.Select(x => new PageInfo(x.W, x.H, x.Rot)).ToList(),
                dto.Items.Select(i => new RedactionItem(
                    Guid.Parse(i.Id), i.Page, i.Shapes.Select(FromDto).ToList(),
                    i.Reason == null ? null : new Reason(i.Reason.Code, i.Reason.Label, i.Reason.Text))).ToList(),
                dto.Style == "whiteBorder" ? RedactionStyle.WhiteWithBorder : RedactionStyle.Black);
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
```

## Geometry.cs

移設先: src/Kuronuri.Core/Geometry/

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Geometry
{
    /// <summary>Effective page box in PDF user space, rotation (0..3), and displayed size in pt.</summary>
    public readonly struct PageGeometry
    {
        public readonly double X0, Y0, X1, Y1, Wd, Hd;
        public readonly int Rotation;

        public PageGeometry(double x0, double y0, double x1, double y1, int rotation, double wd, double hd)
        { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Rotation = rotation; Wd = wd; Hd = hd; }

        public static PageGeometry FromPage(IntPtr page)
        {
            if (!PdfiumNative.FPDF_GetPageBoundingBox(page, out var r)) throw new InvalidOperationException("FPDF_GetPageBoundingBox failed");
            return new PageGeometry(r.Left, r.Bottom, r.Right, r.Top, PdfiumNative.FPDFPage_GetRotation(page),
                PdfiumNative.FPDF_GetPageWidthF(page), PdfiumNative.FPDF_GetPageHeightF(page));
        }
    }

    /// <summary>The only place that knows about /Rotate. Display page space: top-left origin, y down, pt.</summary>
    public static class CoordMap
    {
        /// <summary>Matrix (a b c d e f): X = a*xd + c*yd + e, Y = b*xd + d*yd + f.</summary>
        public static double[] DisplayToUserMatrix(PageGeometry g)
        {
            switch (g.Rotation & 3)
            {
                case 0: return new double[] { 1, 0, 0, -1, g.X0, g.Y1 };
                case 1: return new double[] { 0, 1, 1, 0, g.X0, g.Y0 };
                case 2: return new double[] { -1, 0, 0, 1, g.X1, g.Y0 };
                default: return new double[] { 0, -1, -1, 0, g.X1, g.Y1 };
            }
        }

        /// <summary>Matrix that applies <paramref name="first"/> and then <paramref name="then"/>.</summary>
        public static double[] Compose(double[] first, double[] then) => new[]
        {
            first[0] * then[0] + first[1] * then[2],
            first[0] * then[1] + first[1] * then[3],
            first[2] * then[0] + first[3] * then[2],
            first[2] * then[1] + first[3] * then[3],
            first[4] * then[0] + first[5] * then[2] + then[4],
            first[4] * then[1] + first[5] * then[3] + then[5],
        };

        public static void DisplayToUser(double[] m, double xd, double yd, out double x, out double y)
        {
            x = m[0] * xd + m[2] * yd + m[4];
            y = m[1] * xd + m[3] * yd + m[5];
        }

        public static void UserToDisplay(PageGeometry g, double x, double y, out double xd, out double yd)
        {
            switch (g.Rotation & 3)
            {
                case 0: xd = x - g.X0; yd = g.Y1 - y; break;
                case 1: xd = y - g.Y0; yd = x - g.X0; break;
                case 2: xd = g.X1 - x; yd = y - g.Y0; break;
                default: xd = g.Y1 - y; yd = g.X1 - x; break;
            }
        }
    }

    /// <summary>
    /// Burns redactions into a BGRx buffer (stride = width * 4). No anti-aliasing; every edge is rounded outward
    /// and grown by 1 px so the burned area is always a superset of what the screen shows.
    /// </summary>
    public static unsafe class Burner
    {
        public const uint Black = 0xFF000000, White = 0xFFFFFFFF;

        /// <returns>Number of pixels inside the true geometry that were NOT black after the black pass (must be 0).</returns>
        public static long Burn(IntPtr buffer, int w, int h, double sx, double sy, IReadOnlyList<Shape> shapes, RedactionStyle style)
        {
            uint* p = (uint*)buffer;
            foreach (var s in shapes) Fill(p, w, h, sx, sy, s, 1, Black, false);
            long unburned = 0;
            foreach (var s in shapes) unburned += Fill(p, w, h, sx, sy, s, 0, Black, true);
            if (style == RedactionStyle.WhiteWithBorder)
            {
                int border = Math.Max(2, (int)Math.Ceiling(0.75 * Math.Max(sx, sy)));
                foreach (var s in shapes) Fill(p, w, h, sx, sy, s, 1 - border, White, false);
            }
            return unburned;
        }

        // checkOnly = true: counts pixels that differ from color instead of writing.
        private static long Fill(uint* p, int w, int h, double sx, double sy, Shape s, int grow, uint color, bool checkOnly)
        {
            long bad = 0;
            if (s is RectShape r)
            {
                int l = Math.Max(0, (int)Math.Floor(r.X * sx) - grow), t = Math.Max(0, (int)Math.Floor(r.Y * sy) - grow);
                int rr = Math.Min(w, (int)Math.Ceiling((r.X + r.W) * sx) + grow), b = Math.Min(h, (int)Math.Ceiling((r.Y + r.H) * sy) + grow);
                for (int y = t; y < b; y++)
                {
                    uint* row = p + (long)y * w;
                    for (int x = l; x < rr; x++) { if (checkOnly) { if (row[x] != color) bad++; } else row[x] = color; }
                }
                return bad;
            }
            var st = (StrokeShape)s;
            double scale = Math.Max(sx, sy);
            double rad = checkOnly ? st.WidthPt / 2 * scale : Math.Ceiling(st.WidthPt / 2 * scale) + grow;
            if (rad <= 0) return 0;
            double r2 = rad * rad;
            int n = st.Points.Count;
            for (int i = 0; i < Math.Max(1, n - 1); i++)
            {
                var a = st.Points[i]; var c = st.Points[Math.Min(i + 1, n - 1)];
                double ax = a.X * sx, ay = a.Y * sy, bx = c.X * sx, by = c.Y * sy;
                int l = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - rad)), t = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - rad));
                int rr = Math.Min(w, (int)Math.Ceiling(Math.Max(ax, bx) + rad) + 1), b = Math.Min(h, (int)Math.Ceiling(Math.Max(ay, by) + rad) + 1);
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                for (int y = t; y < b; y++)
                {
                    uint* row = p + (long)y * w;
                    double py = y + 0.5;
                    for (int x = l; x < rr; x++)
                    {
                        double px = x + 0.5;
                        double u = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
                        if (u < 0) u = 0; else if (u > 1) u = 1;
                        double ex = ax + u * dx - px, ey = ay + u * dy - py;
                        if (ex * ex + ey * ey <= r2) { if (checkOnly) { if (row[x] != color) bad++; } else row[x] = color; }
                    }
                }
            }
            return bad;
        }
    }
}
```

## MiniPdfWriter.cs

移設先: src/Kuronuri.Core/Export/

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Kuronuri.Core.Export
{
    /// <summary>
    /// Minimal streaming PDF writer. Every object is written on the spot; only the offsets are kept in memory.
    /// Dictionaries are always written on a single line (the verifier relies on this).
    /// </summary>
    public sealed class MiniPdfWriter
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        public const string Header = "%PDF-1.7\n%âãÏÓ\n";   // 15 bytes

        private readonly Stream _out;
        private readonly List<long> _offsets = new List<long> { 0 };   // index = object number; [0] is the free entry
        private long _pos;

        public MiniPdfWriter(Stream output) { _out = output; Raw(Header); }

        public int ObjectCount => _offsets.Count - 1;

        public static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Allocates the next object number.</summary>
        public int Reserve() { _offsets.Add(-1); return _offsets.Count - 1; }

        public void WriteObject(int num, string dict)
        {
            Begin(num);
            Raw(num.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + dict + "\nendobj\n");
        }

        /// <param name="dictEntries">Entries without the surrounding brackets and without /Length (may be empty).</param>
        public void WriteStream(int num, string dictEntries, byte[] data, int offset, int count)
        {
            Begin(num);
            string head = dictEntries.Length == 0 ? "" : dictEntries + " ";
            Raw(num.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< " + head + "/Length " + count.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            _out.Write(data, offset, count); _pos += count;
            Raw("\nendstream\nendobj\n");
        }

        public void WriteStream(int num, string dictEntries, byte[] data) => WriteStream(num, dictEntries, data, 0, data.Length);

        /// <summary>Writes xref, trailer and EOF. extraTrailer is appended inside the trailer dictionary (e.g. " /Info 5 0 R").</summary>
        public void Finish(int rootNum, string extraTrailer = "")
        {
            long xref = _pos;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(_offsets.Count).Append("\n0000000000 65535 f \n");
            for (int i = 1; i < _offsets.Count; i++)
            {
                if (_offsets[i] < 0) throw new InvalidOperationException("object " + i + " was reserved but never written");
                sb.Append(_offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");   // exactly 20 bytes
            }
            sb.Append("trailer\n<< /Size ").Append(_offsets.Count).Append(" /Root ").Append(rootNum).Append(" 0 R").Append(extraTrailer)
              .Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            Raw(sb.ToString());
            _out.Flush();
        }

        private void Begin(int num)
        {
            if (_offsets[num] >= 0) throw new InvalidOperationException("object " + num + " written twice");
            _offsets[num] = _pos;
        }

        private void Raw(string s) { byte[] b = Latin1.GetBytes(s); _out.Write(b, 0, b.Length); _pos += b.Length; }

        /// <summary>zlib (RFC 1950) wrapper around DeflateStream, for /FlateDecode. net48 has no ZLibStream.</summary>
        public static byte[] ZlibCompress(byte[] data, CompressionLevel level = CompressionLevel.Optimal) => ZlibCompress(data, 0, data.Length, level);

        public static byte[] ZlibCompress(byte[] data, int offset, int count, CompressionLevel level)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, level, true)) ds.Write(data, offset, count);
                uint a = 1, b = 0;
                for (int i = offset; i < offset + count; i++) { a = (a + data[i]) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }
    }
}
```

## FinalExporter.cs

移設先: src/Kuronuri.Core/Export/

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Export
{
    /// <summary>Unmanaged, reusable pixel buffer. Zero-cleared before it is freed.</summary>
    public sealed unsafe class NativeBuffer : IDisposable
    {
        public IntPtr Ptr { get; private set; }
        public long Capacity { get; private set; }

        public void Ensure(long bytes)
        {
            if (bytes <= Capacity) return;
            Release();
            Ptr = Marshal.AllocHGlobal(new IntPtr(bytes));
            Capacity = bytes;
        }

        public void Clear()
        {
            if (Ptr == IntPtr.Zero) return;
            ulong* q = (ulong*)Ptr; long n = Capacity / 8;
            for (long i = 0; i < n; i++) q[i] = 0;
            byte* b = (byte*)Ptr; for (long i = n * 8; i < Capacity; i++) b[i] = 0;
        }

        private void Release()
        {
            if (Ptr == IntPtr.Zero) return;
            Clear(); Marshal.FreeHGlobal(Ptr); Ptr = IntPtr.Zero; Capacity = 0;
        }

        public void Dispose() => Release();
    }

    public static class PageRenderer
    {
        /// <summary>The same flags are used for screen, final output and printing.</summary>
        public const int RenderFlags = PdfiumNative.FPDF_ANNOT;

        public static void PixelSize(double wd, double hd, double dpi, long maxPixels, out int wpx, out int hpx, out double effectiveDpi)
        {
            double scale = dpi / 72.0;
            wpx = Math.Max(1, (int)Math.Ceiling(wd * scale - 1e-6));
            hpx = Math.Max(1, (int)Math.Ceiling(hd * scale - 1e-6));
            if ((long)wpx * hpx > maxPixels)
            {
                scale *= Math.Sqrt((double)maxPixels / ((double)wpx * hpx));
                wpx = Math.Max(1, (int)Math.Floor(wd * scale));
                hpx = Math.Max(1, (int)Math.Floor(hd * scale));
            }
            effectiveDpi = scale * 72.0;
        }

        /// <summary>Renders the whole page into a BGRx buffer of wpx*hpx*4 bytes (white background).</summary>
        public static void RenderFull(IntPtr page, IntPtr buffer, int wpx, int hpx)
        {
            IntPtr bmp = PdfiumNative.FPDFBitmap_CreateEx(wpx, hpx, PdfiumNative.FPDFBitmap_BGRx, buffer, wpx * 4);
            if (bmp == IntPtr.Zero) throw new InvalidOperationException("FPDFBitmap_CreateEx failed");
            try
            {
                PdfiumNative.FPDFBitmap_FillRect(bmp, 0, 0, wpx, hpx, 0xFFFFFFFF);
                PdfiumNative.FPDF_RenderPageBitmap(bmp, page, 0, 0, wpx, hpx, 0, RenderFlags);
            }
            finally { PdfiumNative.FPDFBitmap_Destroy(bmp); }
        }
    }

    /// <summary>A rendered page with pixel access in display page coordinates (pt).</summary>
    public sealed unsafe class Raster : IDisposable
    {
        private readonly NativeBuffer _buf = new NativeBuffer();
        public int W, H;
        public double Sx, Sy;
        public IntPtr Ptr => _buf.Ptr;

        public static Raster Render(IntPtr page, double dpi)
        {
            var r = new Raster();
            double wd = PdfiumNative.FPDF_GetPageWidthF(page), hd = PdfiumNative.FPDF_GetPageHeightF(page);
            PageRenderer.PixelSize(wd, hd, dpi, long.MaxValue, out r.W, out r.H, out _);
            r.Sx = r.W / wd; r.Sy = r.H / hd;
            r._buf.Ensure((long)r.W * r.H * 4);
            PageRenderer.RenderFull(page, r._buf.Ptr, r.W, r.H);
            return r;
        }

        public void Rgb(double xd, double yd, out int r, out int g, out int b)
        {
            int x = Math.Min(W - 1, Math.Max(0, (int)(xd * Sx))), y = Math.Min(H - 1, Math.Max(0, (int)(yd * Sy)));
            uint v = ((uint*)_buf.Ptr)[(long)y * W + x];
            b = (int)(v & 0xFF); g = (int)((v >> 8) & 0xFF); r = (int)((v >> 16) & 0xFF);
        }

        public void Dispose() => _buf.Dispose();
    }

    public static unsafe class JpegCodec
    {
        /// <summary>True when the page has a meaningful amount of saturated pixels (e.g. a red seal).</summary>
        public static bool DetectColor(IntPtr bgrx, long pixelCount)
        {
            uint* p = (uint*)bgrx;
            long limit = Math.Max(50, pixelCount / 50000), colored = 0;
            for (long i = 0; i < pixelCount; i++)
            {
                uint v = p[i];
                int b = (int)(v & 0xFF), g = (int)((v >> 8) & 0xFF), r = (int)((v >> 16) & 0xFF);
                int mx = r > g ? (r > b ? r : b) : (g > b ? g : b);
                int mn = r < g ? (r < b ? r : b) : (g < b ? g : b);
                if (mx - mn > 40 && ++colored >= limit) return true;
            }
            return false;
        }

        /// <summary>Encodes with the in-box WIC encoder. Gray pages go through a Gray8 copy held in grayScratch.</summary>
        public static void Encode(IntPtr bgrx, NativeBuffer grayScratch, int w, int h, bool color, int quality, MemoryStream output)
        {
            BitmapSource src;
            if (color)
            {
                src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgrx, checked(w * h * 4), w * 4);
            }
            else
            {
                long n = (long)w * h;
                grayScratch.Ensure(n);
                uint* p = (uint*)bgrx; byte* q = (byte*)grayScratch.Ptr;
                for (long i = 0; i < n; i++)
                {
                    uint v = p[i];
                    q[i] = (byte)((((v >> 16) & 0xFF) * 77 + ((v >> 8) & 0xFF) * 150 + (v & 0xFF) * 29 + 128) >> 8);
                }
                src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, grayScratch.Ptr, checked(w * h), w);
            }
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(src));
            output.SetLength(0);
            enc.Save(output);
        }

        /// <summary>Number of components from the SOF marker (1 = gray, 3 = color), or 0 when not a JPEG.</summary>
        public static int Components(byte[] jpeg, int length)
        {
            if (length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return 0;
            int i = 2;
            while (i + 9 < length && jpeg[i] == 0xFF)
            {
                int marker = jpeg[i + 1];
                int seg = (jpeg[i + 2] << 8) | jpeg[i + 3];
                bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (sof) return jpeg[i + 9];
                i += 2 + seg;
            }
            return 0;
        }
    }

    /// <summary>
    /// Lossless alternative to JPEG (zlib over raw Gray8 / RGB24, no predictor). Clean born-digital pages compress
    /// better this way; scanned pages do not, so a cheap probe on sample bands decides whether the full pass is worth it.
    /// </summary>
    public sealed unsafe class LosslessCodec
    {
        private byte[] _raw = new byte[0];

        /// <returns>zlib data when it is smaller than the JPEG, otherwise null.</returns>
        public byte[]? TryEncode(IntPtr bgrx, IntPtr gray8, int w, int h, bool color, int jpegBytes)
        {
            int rowBytes = w * (color ? 3 : 1);
            long total = (long)rowBytes * h;
            if (total > int.MaxValue - 64) return null;
            if (_raw.Length < total) _raw = new byte[total];
            fixed (byte* dst = _raw)
            {
                if (color)
                {
                    uint* p = (uint*)bgrx; byte* d = dst;
                    for (long i = 0; i < (long)w * h; i++) { uint v = p[i]; *d++ = (byte)(v >> 16); *d++ = (byte)(v >> 8); *d++ = (byte)v; }
                }
                else Buffer.MemoryCopy((void*)gray8, dst, total, total);
            }

            const int bands = 8, bandRows = 64;
            if (h > bands * bandRows)
            {
                using (var ms = new MemoryStream())
                {
                    using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                        for (int k = 0; k < bands; k++)
                        {
                            int row = Math.Min(h - bandRows, Math.Max(0, h * (2 * k + 1) / (2 * bands) - bandRows / 2));
                            ds.Write(_raw, row * rowBytes, bandRows * rowBytes);
                        }
                    double estimate = ms.Length * (double)h / (bands * bandRows);
                    if (estimate >= 0.9 * jpegBytes) return null;
                }
            }
            byte[] z = MiniPdfWriter.ZlibCompress(_raw, 0, (int)total, System.IO.Compression.CompressionLevel.Optimal);
            return z.Length < jpegBytes ? z : null;
        }
    }

    public sealed class FinalExportOptions
    {
        public double Dpi = 200;
        public int JpegQuality = 85;
        public long MaxPixelsPerPage = 60_000_000;
        public bool TryLossless = true;   // keep the smaller of JPEG and lossless per page
        public int GcEveryPages = 8;      // WIC bitmaps are only released by the GC; without this the working set balloons
    }

    public sealed class FinalPageReport
    {
        public int WidthPx, HeightPx, ImageBytes;
        public double EffectiveDpi;
        public bool Color, Lossless;
    }

    /// <summary>
    /// Final (public) output: every page is rendered, burned, JPEG-encoded and written as an image-only page.
    /// Object layout is fixed: 1 = Catalog, 2 = Pages, then (image, contents, page) per page. Nothing else is written.
    /// </summary>
    public static class FinalExporter
    {
        public static List<FinalPageReport> Export(PdfDoc src, IReadOnlyList<RedactionItem> items, RedactionStyle style,
            Stream output, FinalExportOptions opt, Action<int, int>? progress = null)
        {
            int n = src.PageCount;
            var w = new MiniPdfWriter(output);
            int catalog = w.Reserve(), pages = w.Reserve();
            var kids = new StringBuilder();
            var reports = new List<FinalPageReport>();
            var byPage = items.GroupBy(i => i.PageIndex)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Shape>)g.SelectMany(i => i.Shapes).ToList());
            var lossless = new LosslessCodec();

            using (var buf = new NativeBuffer())
            using (var gray = new NativeBuffer())
            using (var jpeg = new MemoryStream())
            {
                for (int i = 0; i < n; i++)
                {
                    IntPtr page = PdfiumNative.FPDF_LoadPage(src.Handle, i);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed: " + (i + 1));
                    try
                    {
                        double wd = PdfiumNative.FPDF_GetPageWidthF(page), hd = PdfiumNative.FPDF_GetPageHeightF(page);
                        PageRenderer.PixelSize(wd, hd, opt.Dpi, opt.MaxPixelsPerPage, out int wpx, out int hpx, out double eff);
                        buf.Ensure((long)wpx * hpx * 4);
                        PageRenderer.RenderFull(page, buf.Ptr, wpx, hpx);

                        if (byPage.TryGetValue(i, out var shapes))
                        {
                            long unburned = Burner.Burn(buf.Ptr, wpx, hpx, wpx / wd, hpx / hd, shapes, style);
                            if (unburned != 0) throw new InvalidOperationException("burn verification failed on page " + (i + 1));
                        }

                        bool color = JpegCodec.DetectColor(buf.Ptr, (long)wpx * hpx);
                        JpegCodec.Encode(buf.Ptr, gray, wpx, hpx, color, opt.JpegQuality, jpeg);
                        int len = (int)jpeg.Length;
                        if (JpegCodec.Components(jpeg.GetBuffer(), len) != (color ? 3 : 1)) throw new InvalidOperationException("unexpected JPEG component count");
                        byte[]? z = opt.TryLossless ? lossless.TryEncode(buf.Ptr, gray.Ptr, wpx, hpx, color, len) : null;

                        int img = w.Reserve(), cont = w.Reserve(), pg = w.Reserve();
                        string head = "/Type /XObject /Subtype /Image /Width " + wpx + " /Height " + hpx +
                            " /ColorSpace " + (color ? "/DeviceRGB" : "/DeviceGray") + " /BitsPerComponent 8 /Filter ";
                        if (z != null) w.WriteStream(img, head + "/FlateDecode", z);
                        else w.WriteStream(img, head + "/DCTDecode", jpeg.GetBuffer(), 0, len);
                        string ws = MiniPdfWriter.F(wd), hs = MiniPdfWriter.F(hd);
                        w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes("q " + ws + " 0 0 " + hs + " 0 0 cm /Im0 Do Q"));
                        w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + ws + " " + hs + "] /Resources << /XObject << /Im0 " +
                            img + " 0 R >> >> /Contents " + cont + " 0 R >>");
                        kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
                        reports.Add(new FinalPageReport { WidthPx = wpx, HeightPx = hpx, ImageBytes = z?.Length ?? len, EffectiveDpi = eff, Color = color, Lossless = z != null });
                    }
                    finally { PdfiumNative.FPDF_ClosePage(page); }

                    if (opt.GcEveryPages > 0 && (i + 1) % opt.GcEveryPages == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    progress?.Invoke(i + 1, n);
                }
            }
            w.WriteObject(pages, "<< /Type /Pages /Count " + n + " /Kids [" + kids + "] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.Finish(catalog);
            return reports;
        }
    }
}
```

## OutputVerifier.cs

移設先: src/Kuronuri.Core/Export/

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Export
{
    /// <summary>Verifies a final output file. An empty list means it passed.</summary>
    public static class OutputVerifier
    {
        private static readonly Regex RxTail = new Regex(@"\nstartxref\n(\d+)\n%%EOF\n$");
        private static readonly Regex RxXref = new Regex(@"^xref\n0 (\d+)\n((?:\d{10} \d{5} [nf] \n)+)trailer\n<< /Size (\d+) /Root 1 0 R >>\nstartxref\n\d+\n%%EOF\n$");
        private static readonly Regex RxCatalog = new Regex(@"^<< /Type /Catalog /Pages 2 0 R >>$");
        private static readonly Regex RxPages = new Regex(@"^<< /Type /Pages /Count (\d+) /Kids \[\d+ 0 R(?: \d+ 0 R)*\] >>$");
        private static readonly Regex RxPage = new Regex(@"^<< /Type /Page /Parent 2 0 R /MediaBox \[0 0 [\d.]+ [\d.]+\] /Resources << /XObject << /Im0 \d+ 0 R >> >> /Contents \d+ 0 R >>$");
        private static readonly Regex RxContents = new Regex(@"^<< /Length (\d+) >>$");
        private static readonly Regex RxContentBody = new Regex(@"^q [\d.]+ 0 0 [\d.]+ 0 0 cm /Im0 Do Q$");
        private static readonly Regex RxImage = new Regex(@"^<< /Type /XObject /Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace /Device(RGB|Gray) /BitsPerComponent 8 /Filter /(DCTDecode|FlateDecode) /Length (\d+) >>$");

        /// <summary>Inflates a zlib stream and checks both the decoded length and the Adler-32 trailer.</summary>
        private static bool ZlibOk(byte[] pdf, int pos, int len, long expected)
        {
            if (len < 6 || pdf[pos] != 0x78) return false;
            uint a = 1, b = 0; long total = 0;
            try
            {
                using (var ms = new System.IO.MemoryStream(pdf, pos + 2, len - 6))
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                {
                    var chunk = new byte[64 * 1024]; int n;
                    while ((n = ds.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        total += n;
                        for (int i = 0; i < n; i++) { a = (a + chunk[i]) % 65521; b = (b + a) % 65521; }
                    }
                }
            }
            catch (System.IO.InvalidDataException) { return false; }
            uint stored = ((uint)pdf[pos + len - 4] << 24) | ((uint)pdf[pos + len - 3] << 16) | ((uint)pdf[pos + len - 2] << 8) | pdf[pos + len - 1];
            return total == expected && ((b << 16) | a) == stored;
        }

        /// <summary>
        /// Whitelist check: walks every object through the xref table. Each object must be exactly one of the five
        /// known shapes, objects must be contiguous (no hidden bytes), and nothing may follow %%EOF.
        /// </summary>
        public static List<string> VerifyStructure(byte[] pdf, int pageCount)
        {
            var fails = new List<string>();
            var L = MiniPdfWriter.Latin1;
            int tailLen = Math.Min(64, pdf.Length);
            var mt = RxTail.Match(L.GetString(pdf, pdf.Length - tailLen, tailLen));
            if (!mt.Success) { fails.Add("tail: startxref/EOF not found at the end"); return fails; }
            long xref = long.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
            if (xref <= 0 || xref >= pdf.Length) { fails.Add("xref offset out of range"); return fails; }
            var mx = RxXref.Match(L.GetString(pdf, (int)xref, pdf.Length - (int)xref));
            if (!mx.Success) { fails.Add("xref/trailer is not in the expected form"); return fails; }
            int size = int.Parse(mx.Groups[1].Value, CultureInfo.InvariantCulture);
            if (mx.Groups[3].Value != mx.Groups[1].Value) fails.Add("trailer /Size mismatch");
            if (size != 3 + 3 * pageCount) fails.Add("object count is " + (size - 1) + ", expected " + (2 + 3 * pageCount));
            string entries = mx.Groups[2].Value;
            if (entries.Length != size * 20) { fails.Add("xref entry count mismatch"); return fails; }

            var byOffset = new SortedDictionary<long, int>();
            for (int i = 1; i < size; i++)
            {
                string e = entries.Substring(i * 20, 20);
                if (e[17] != 'n') { fails.Add("free entry " + i); continue; }
                byOffset[long.Parse(e.Substring(0, 10), CultureInfo.InvariantCulture)] = i;
            }
            if (byOffset.Count != size - 1) { fails.Add("duplicate or free object offsets"); return fails; }
            if (byOffset.Keys.First() != MiniPdfWriter.Header.Length) fails.Add("unexpected bytes between header and first object");

            int nCatalog = 0, nPages = 0, nPage = 0, nContents = 0, nImage = 0;
            var offsets = byOffset.Keys.ToList();
            for (int k = 0; k < offsets.Count; k++)
            {
                int pos = (int)offsets[k];
                long end = k + 1 < offsets.Count ? offsets[k + 1] : xref;
                int num = byOffset[offsets[k]];
                string head = ReadLine(pdf, ref pos);
                if (head != num.ToString(CultureInfo.InvariantCulture) + " 0 obj") { fails.Add("object header mismatch at " + offsets[k]); continue; }
                string dict = ReadLine(pdf, ref pos);
                string next = ReadLine(pdf, ref pos);
                Match m;
                if (next == "endobj")
                {
                    if (RxCatalog.IsMatch(dict)) nCatalog++;
                    else if ((m = RxPages.Match(dict)).Success) { nPages++; if (m.Groups[1].Value != pageCount.ToString(CultureInfo.InvariantCulture)) fails.Add("/Count mismatch"); }
                    else if (RxPage.IsMatch(dict)) nPage++;
                    else fails.Add("unknown object " + num);
                }
                else if (next == "stream")
                {
                    bool isImage = (m = RxImage.Match(dict)).Success;
                    if (!isImage) m = RxContents.Match(dict);
                    if (!m.Success) { fails.Add("unknown stream object " + num); continue; }
                    int len = int.Parse(m.Groups[isImage ? 5 : 1].Value, CultureInfo.InvariantCulture);
                    if (pos + len + 18 > pdf.Length) { fails.Add("stream length out of range in " + num); continue; }
                    if (isImage)
                    {
                        nImage++;
                        if (m.Groups[4].Value == "DCTDecode")
                        {
                            if (len < 4 || pdf[pos] != 0xFF || pdf[pos + 1] != 0xD8 || pdf[pos + len - 2] != 0xFF || pdf[pos + len - 1] != 0xD9) fails.Add("image " + num + " is not a complete JPEG");
                        }
                        else
                        {
                            long expected = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * (m.Groups[3].Value == "RGB" ? 3 : 1);
                            if (!ZlibOk(pdf, pos, len, expected)) fails.Add("image " + num + " is not a valid zlib stream of the declared size");
                        }
                    }
                    else
                    {
                        nContents++;
                        if (!RxContentBody.IsMatch(L.GetString(pdf, pos, len))) fails.Add("unexpected content stream in " + num);
                    }
                    pos += len;
                    if (L.GetString(pdf, pos, 18) != "\nendstream\nendobj\n") { fails.Add("stream terminator missing in " + num); continue; }
                    pos += 18;
                }
                else { fails.Add("unexpected token after dictionary in " + num); continue; }
                if (pos != end) fails.Add("gap or overlap after object " + num);
            }
            if (nCatalog != 1 || nPages != 1 || nPage != pageCount || nContents != pageCount || nImage != pageCount)
                fails.Add("object kinds: catalog=" + nCatalog + " pages=" + nPages + " page=" + nPage + " contents=" + nContents + " image=" + nImage);
            return fails;
        }

        private static string ReadLine(byte[] b, ref int pos)
        {
            int s = pos;
            while (pos < b.Length && b[pos] != (byte)'\n') pos++;
            string line = MiniPdfWriter.Latin1.GetString(b, s, pos - s);
            if (pos < b.Length) pos++;
            return line;
        }

        /// <summary>Re-opens the output with PDFium: no text, one image per page, no annotations / attachments / bookmarks / metadata, and burned pixels.</summary>
        public static List<string> VerifyWithPdfium(byte[] pdf, IReadOnlyList<PageInfo> pages, IReadOnlyList<RedactionItem> items, RedactionStyle style)
        {
            var fails = new List<string>();
            using (var doc = PdfDoc.FromBytes(pdf))
            {
                if (doc.PageCount != pages.Count) { fails.Add("page count"); return fails; }
                if (PdfiumNative.FPDFDoc_GetAttachmentCount(doc.Handle) != 0) fails.Add("attachments present");
                if (PdfiumNative.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero) != IntPtr.Zero) fails.Add("bookmarks present");
                foreach (string tag in new[] { "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate" })
                    if (PdfiumNative.FPDF_GetMetaText(doc.Handle, tag, null, 0) > 2) fails.Add("metadata present: " + tag);

                for (int i = 0; i < pages.Count; i++)
                {
                    IntPtr page = PdfiumNative.FPDF_LoadPage(doc.Handle, i);
                    if (page == IntPtr.Zero) { fails.Add("page " + (i + 1) + " load"); continue; }
                    try
                    {
                        string P = "page " + (i + 1) + ": ";
                        if (Math.Abs(PdfiumNative.FPDF_GetPageWidthF(page) - pages[i].Width) > 0.01 || Math.Abs(PdfiumNative.FPDF_GetPageHeightF(page) - pages[i].Height) > 0.01) fails.Add(P + "size");
                        if (PdfiumNative.FPDFPage_GetRotation(page) != 0) fails.Add(P + "rotation");
                        if (PdfiumNative.FPDFPage_GetAnnotCount(page) != 0) fails.Add(P + "annotations");
                        if (PdfiumNative.FPDFPage_CountObjects(page) != 1 ||
                            PdfiumNative.FPDFPageObj_GetType(PdfiumNative.FPDFPage_GetObject(page, 0)) != PdfiumNative.FPDF_PAGEOBJ_IMAGE) fails.Add(P + "objects");
                        IntPtr tp = PdfiumNative.FPDFText_LoadPage(page);
                        if (tp != IntPtr.Zero) { if (PdfiumNative.FPDFText_CountChars(tp) != 0) fails.Add(P + "text"); PdfiumNative.FPDFText_ClosePage(tp); }

                        using (var r = Raster.Render(page, 100))
                        {
                            double border = style == RedactionStyle.WhiteWithBorder ? Math.Max(2 / r.Sx, 0.75) + 2.5 : 1.5;
                            foreach (var item in items.Where(x => x.PageIndex == i))
                                foreach (var s in item.Shapes)
                                    foreach (var pt in InteriorSamples(s, border))
                                    {
                                        r.Rgb(pt.X, pt.Y, out int cr, out int cg, out int cb);
                                        bool ok = style == RedactionStyle.Black ? (cr <= 48 && cg <= 48 && cb <= 48) : (cr >= 207 && cg >= 207 && cb >= 207);
                                        if (!ok) { fails.Add(P + "pixel not burned at " + pt.X.ToString("0.#") + "," + pt.Y.ToString("0.#")); break; }
                                    }
                        }
                    }
                    finally { PdfiumNative.FPDF_ClosePage(page); }
                }
            }
            return fails;
        }

        private static IEnumerable<Point2> InteriorSamples(Shape s, double inset)
        {
            if (s is RectShape r)
            {
                if (r.W <= 2 * inset || r.H <= 2 * inset) { yield return new Point2(r.X + r.W / 2, r.Y + r.H / 2); yield break; }
                for (int iy = 0; iy <= 4; iy++)
                    for (int ix = 0; ix <= 6; ix++)
                        yield return new Point2(r.X + inset + (r.W - 2 * inset) * ix / 6.0, r.Y + inset + (r.H - 2 * inset) * iy / 4.0);
            }
            else foreach (var p in ((StrokeShape)s).Points) yield return p;
        }
    }
}
```

## Hybrid.cs

移設先: src/Kuronuri.Core/Hybrid/

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Hybrid
{
    /// <summary>Turns text into outline paths so that no font is ever embedded or referenced in the PDF.</summary>
    public static class TextOutline
    {
        public static System.Windows.Media.Geometry Build(string text, double emSize, bool bold)
        {
            var tf = new Typeface(new FontFamily("Yu Gothic UI, Meiryo, MS Gothic"), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, tf, emSize, Brushes.Black, 1.0);
            return ft.BuildGeometry(new Point(0, 0));
        }

        /// <param name="map">Maps a geometry point to PDF user space.</param>
        /// <returns>A PDFium path object (fill mode not yet set), or IntPtr.Zero when the geometry is empty.</returns>
        public static IntPtr ToPdfiumPath(System.Windows.Media.Geometry geo, Func<Point, Point> map)
        {
            PathGeometry pg = PathGeometry.CreateFromGeometry(geo);
            if (!Supported(pg)) pg = geo.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
            Matrix t = pg.Transform == null ? Matrix.Identity : pg.Transform.Value;
            Func<Point, Point> m = p => map(t.Transform(p));
            IntPtr path = IntPtr.Zero;
            foreach (PathFigure fig in pg.Figures)
            {
                Point cur = fig.StartPoint, q = m(cur);
                if (path == IntPtr.Zero) path = N.FPDFPageObj_CreateNewPath((float)q.X, (float)q.Y);
                else N.FPDFPath_MoveTo(path, (float)q.X, (float)q.Y);
                foreach (PathSegment seg in fig.Segments)
                {
                    if (seg is LineSegment ls) { Line(path, m(ls.Point)); cur = ls.Point; }
                    else if (seg is PolyLineSegment pls) { foreach (Point p in pls.Points) { Line(path, m(p)); cur = p; } }
                    else if (seg is BezierSegment bs) { Cubic(path, m(bs.Point1), m(bs.Point2), m(bs.Point3)); cur = bs.Point3; }
                    else if (seg is PolyBezierSegment pbs)
                    {
                        for (int i = 0; i + 2 < pbs.Points.Count; i += 3) { Cubic(path, m(pbs.Points[i]), m(pbs.Points[i + 1]), m(pbs.Points[i + 2])); cur = pbs.Points[i + 2]; }
                    }
                    else if (seg is QuadraticBezierSegment qs) { Quad(path, m, cur, qs.Point1, qs.Point2); cur = qs.Point2; }
                    else if (seg is PolyQuadraticBezierSegment pqs)
                    {
                        for (int i = 0; i + 1 < pqs.Points.Count; i += 2) { Quad(path, m, cur, pqs.Points[i], pqs.Points[i + 1]); cur = pqs.Points[i + 1]; }
                    }
                }
                if (fig.IsClosed) N.FPDFPath_Close(path);
            }
            return path;
        }

        private static bool Supported(PathGeometry pg) => pg.Figures.All(f => f.Segments.All(s =>
            s is LineSegment || s is PolyLineSegment || s is BezierSegment || s is PolyBezierSegment ||
            s is QuadraticBezierSegment || s is PolyQuadraticBezierSegment));

        private static void Line(IntPtr path, Point p) => N.FPDFPath_LineTo(path, (float)p.X, (float)p.Y);
        private static void Cubic(IntPtr path, Point a, Point b, Point c) =>
            N.FPDFPath_BezierTo(path, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y);
        private static void Quad(IntPtr path, Func<Point, Point> m, Point p0, Point ctrl, Point p2)   // quadratic -> cubic
        {
            var c1 = new Point(p0.X + 2.0 / 3 * (ctrl.X - p0.X), p0.Y + 2.0 / 3 * (ctrl.Y - p0.Y));
            var c2 = new Point(p2.X + 2.0 / 3 * (ctrl.X - p2.X), p2.Y + 2.0 / 3 * (ctrl.Y - p2.Y));
            Cubic(path, m(c1), m(c2), m(p2));
        }
    }

    public sealed class HybridOptions
    {
        public bool DrawText = true;
        public string Watermark = "【庁内確認用・外部交付厳禁】";
        public bool SharedWatermark = true;   // one Form XObject referenced by every page instead of per-page paths
    }

    /// <summary>
    /// Internal-review PDF: the original pages are imported as vectors, overlays are added as page content,
    /// and the untouched original bytes plus the project JSON are stored as attachments.
    /// </summary>
    public static class HybridWriter
    {
        public const string OriginalName = "original.pdf";
        public const string ProjectName = "kuronuri-project.json";

        public static byte[] Build(byte[] original, byte[] projectJson, IReadOnlyList<RedactionItem> items, HybridOptions opt)
        {
            using (var src = PdfDoc.FromBytes(original))
            using (var dst = PdfDoc.CreateNew())
            {
                if (!N.FPDF_ImportPages(dst.Handle, src.Handle, null, 0)) throw new InvalidOperationException("FPDF_ImportPages failed");
                int n = dst.PageCount;
                if (n != src.PageCount) throw new InvalidOperationException("imported page count mismatch");
                var ordered = ShapeMath.InDisplayOrder(items);
                var wm = opt.DrawText ? TextOutline.Build(opt.Watermark, 100, true) : null;
                IntPtr wmX = IntPtr.Zero; double wmW = 0, wmH = 0;
                if (wm != null && opt.SharedWatermark) wmX = CreateWatermarkXObject(dst, wm, out wmW, out wmH);

                try
                {
                for (int i = 0; i < n; i++)
                {
                    IntPtr page = N.FPDF_LoadPage(dst.Handle, i);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed");
                    try
                    {
                        var g = PageGeometry.FromPage(page);
                        double[] m = CoordMap.DisplayToUserMatrix(g);
                        for (int k = 0; k < ordered.Count; k++)
                        {
                            var item = ordered[k];
                            if (item.PageIndex != i) continue;
                            foreach (var s in item.Shapes) N.FPDFPage_InsertObject(page, ShapePath(m, s));
                            if (opt.DrawText)
                            {
                                var b = ShapeMath.Bounds(item);
                                string label = (k + 1).ToString(CultureInfo.InvariantCulture) + (item.Reason == null ? "" : " " + item.Reason.Code);
                                double top = b.T >= 12 ? b.T - 11 : b.T + 1;
                                IntPtr lp = TextOutline.ToPdfiumPath(TextOutline.Build(label, 8, true), p => ToUser(m, b.L + p.X, top + p.Y));
                                if (lp != IntPtr.Zero)
                                {
                                    N.FPDFPath_SetDrawMode(lp, N.FPDF_FILLMODE_WINDING, false);
                                    N.FPDFPageObj_SetFillColor(lp, 200, 0, 0, 255);
                                    N.FPDFPage_InsertObject(page, lp);
                                }
                            }
                        }
                        if (wm != null && wmX != IntPtr.Zero)
                        {
                            // Shared form: XObject space (y up) -> centred display space -> rotate -30deg -> page user space.
                            double s = 0.85 * Math.Min(g.Wd, g.Hd) / wm.Bounds.Width;
                            double cos = Math.Cos(-Math.PI / 6), sin = Math.Sin(-Math.PI / 6);
                            double[] a = { s, 0, 0, -s, -s * wmW / 2, s * wmH / 2 };
                            double[] b = { cos, sin, -sin, cos, g.Wd / 2, g.Hd / 2 };
                            double[] t = CoordMap.Compose(CoordMap.Compose(a, b), m);
                            IntPtr form = N.FPDF_NewFormObjectFromXObject(wmX);
                            if (form == IntPtr.Zero) throw new InvalidOperationException("FPDF_NewFormObjectFromXObject failed");
                            N.FPDFPageObj_Transform(form, t[0], t[1], t[2], t[3], t[4], t[5]);
                            N.FPDFPage_InsertObject(page, form);
                        }
                        else if (wm != null)
                        {
                            Rect wb = wm.Bounds;
                            double scale = 0.85 * Math.Min(g.Wd, g.Hd) / wb.Width;
                            double cos = Math.Cos(-Math.PI / 6), sin = Math.Sin(-Math.PI / 6);
                            double cx = wb.X + wb.Width / 2, cy = wb.Y + wb.Height / 2;
                            IntPtr wp = TextOutline.ToPdfiumPath(wm, p =>
                            {
                                double qx = (p.X - cx) * scale, qy = (p.Y - cy) * scale;
                                return ToUser(m, g.Wd / 2 + qx * cos - qy * sin, g.Hd / 2 + qx * sin + qy * cos);
                            });
                            if (wp != IntPtr.Zero)
                            {
                                N.FPDFPath_SetDrawMode(wp, N.FPDF_FILLMODE_WINDING, false);
                                N.FPDFPageObj_SetFillColor(wp, 255, 0, 0, 48);
                                N.FPDFPage_InsertObject(page, wp);
                            }
                        }
                        if (!N.FPDFPage_GenerateContent(page)) throw new InvalidOperationException("FPDFPage_GenerateContent failed");
                    }
                    finally { N.FPDF_ClosePage(page); }
                }
                }
                finally { if (wmX != IntPtr.Zero) N.FPDF_CloseXObject(wmX); }

                AddAttachment(dst.Handle, OriginalName, original);
                AddAttachment(dst.Handle, ProjectName, projectJson);
                using (var ms = new MemoryStream()) { dst.SaveTo(ms, N.FPDF_NO_INCREMENTAL); return ms.ToArray(); }
            }
        }

        /// <summary>Draws the watermark once on a temporary one-page document and imports that page into dst as a Form XObject.</summary>
        private static IntPtr CreateWatermarkXObject(PdfDoc dst, System.Windows.Media.Geometry wm, out double w, out double h)
        {
            Rect b = wm.Bounds;
            const double pad = 2;
            double pw = b.Width + 2 * pad, ph = b.Height + 2 * pad;
            w = pw; h = ph;
            using (var tmp = PdfDoc.CreateNew())
            {
                IntPtr page = N.FPDFPage_New(tmp.Handle, 0, pw, ph);
                if (page == IntPtr.Zero) throw new InvalidOperationException("FPDFPage_New failed");
                try
                {
                    IntPtr path = TextOutline.ToPdfiumPath(wm, p => new Point(p.X - b.X + pad, ph - (p.Y - b.Y + pad)));
                    if (path == IntPtr.Zero) return IntPtr.Zero;
                    N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_WINDING, false);
                    N.FPDFPageObj_SetFillColor(path, 255, 0, 0, 48);
                    N.FPDFPage_InsertObject(page, path);
                    if (!N.FPDFPage_GenerateContent(page)) throw new InvalidOperationException("FPDFPage_GenerateContent failed");
                }
                finally { N.FPDF_ClosePage(page); }
                IntPtr x = N.FPDF_NewXObjectFromPage(dst.Handle, tmp.Handle, 0);
                if (x == IntPtr.Zero) throw new InvalidOperationException("FPDF_NewXObjectFromPage failed");
                return x;
            }
        }

        private static Point ToUser(double[] m, double xd, double yd)
        {
            CoordMap.DisplayToUser(m, xd, yd, out double x, out double y);
            return new Point(x, y);
        }

        /// <summary>Translucent red overlay for one shape, in the user space of the imported page.</summary>
        public static IntPtr ShapePath(double[] m, Shape s)
        {
            if (s is RectShape r)
            {
                Point a = ToUser(m, r.X, r.Y), b = ToUser(m, r.X + r.W, r.Y), c = ToUser(m, r.X + r.W, r.Y + r.H), d = ToUser(m, r.X, r.Y + r.H);
                IntPtr path = N.FPDFPageObj_CreateNewPath((float)a.X, (float)a.Y);
                N.FPDFPath_LineTo(path, (float)b.X, (float)b.Y);
                N.FPDFPath_LineTo(path, (float)c.X, (float)c.Y);
                N.FPDFPath_LineTo(path, (float)d.X, (float)d.Y);
                N.FPDFPath_Close(path);
                N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_ALTERNATE, true);
                N.FPDFPageObj_SetFillColor(path, 255, 0, 0, 64);
                N.FPDFPageObj_SetStrokeColor(path, 255, 0, 0, 255);
                N.FPDFPageObj_SetStrokeWidth(path, 1f);
                return path;
            }
            else
            {
                var st = (StrokeShape)s;
                Point p0 = ToUser(m, st.Points[0].X, st.Points[0].Y);
                IntPtr path = N.FPDFPageObj_CreateNewPath((float)p0.X, (float)p0.Y);
                if (st.Points.Count == 1) N.FPDFPath_LineTo(path, (float)p0.X, (float)p0.Y);
                for (int i = 1; i < st.Points.Count; i++) { Point p = ToUser(m, st.Points[i].X, st.Points[i].Y); N.FPDFPath_LineTo(path, (float)p.X, (float)p.Y); }
                N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_NONE, true);
                N.FPDFPageObj_SetStrokeColor(path, 255, 0, 0, 96);
                N.FPDFPageObj_SetStrokeWidth(path, (float)st.WidthPt);
                N.FPDFPageObj_SetLineCap(path, N.FPDF_LINECAP_ROUND);
                N.FPDFPageObj_SetLineJoin(path, N.FPDF_LINEJOIN_ROUND);
                return path;
            }
        }

        private static void AddAttachment(IntPtr doc, string name, byte[] data)
        {
            IntPtr att = N.FPDFDoc_AddAttachment(doc, name);
            if (att == IntPtr.Zero) throw new InvalidOperationException("FPDFDoc_AddAttachment failed");
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { if (!N.FPDFAttachment_SetFile(att, doc, h.AddrOfPinnedObject(), checked((uint)data.Length))) throw new InvalidOperationException("FPDFAttachment_SetFile failed"); }
            finally { h.Free(); }
        }
    }

    public static class HybridReader
    {
        /// <summary>Extracts the embedded original and project JSON. Returns false when the PDF is not a review file.</summary>
        public static bool TryRead(byte[] pdf, out byte[]? original, out byte[]? projectJson)
        {
            original = null; projectJson = null;
            using (var doc = PdfDoc.FromBytes(pdf))
            {
                int n = N.FPDFDoc_GetAttachmentCount(doc.Handle);
                for (int i = 0; i < n; i++)
                {
                    IntPtr att = N.FPDFDoc_GetAttachment(doc.Handle, i);
                    if (att == IntPtr.Zero) continue;
                    string name = Name(att);
                    if (name == HybridWriter.OriginalName) original = Data(att);
                    else if (name == HybridWriter.ProjectName) projectJson = Data(att);
                }
            }
            return original != null && projectJson != null;
        }

        private static string Name(IntPtr att)
        {
            uint len = N.FPDFAttachment_GetName(att, null, 0);
            if (len < 2) return "";
            var b = new byte[len];
            N.FPDFAttachment_GetName(att, b, len);
            return Encoding.Unicode.GetString(b, 0, (int)len - 2);
        }

        private static byte[] Data(IntPtr att)
        {
            N.FPDFAttachment_GetFile(att, IntPtr.Zero, 0, out uint len);
            var b = new byte[len];
            if (len == 0) return b;
            var h = GCHandle.Alloc(b, GCHandleType.Pinned);
            try { if (!N.FPDFAttachment_GetFile(att, h.AddrOfPinnedObject(), len, out _)) throw new InvalidOperationException("FPDFAttachment_GetFile failed"); }
            finally { h.Free(); }
            return b;
        }
    }

    /// <summary>
    /// Fallback review PDF written with MiniPdfWriter: pages are images (display orientation), overlays are vector
    /// operators, and the original + project JSON are embedded files stored without compression.
    /// </summary>
    public static class HybridRasterWriter
    {
        public static void Build(PdfDoc src, byte[] original, byte[] projectJson, IReadOnlyList<RedactionItem> items, Stream output, double dpi, int quality)
        {
            string F(double v) => MiniPdfWriter.F(v);
            int n = src.PageCount;
            var w = new MiniPdfWriter(output);
            int catalog = w.Reserve(), pages = w.Reserve();
            var kids = new StringBuilder();
            using (var buf = new NativeBuffer())
            using (var gray = new NativeBuffer())
            using (var jpeg = new MemoryStream())
            {
                for (int i = 0; i < n; i++)
                {
                    IntPtr page = N.FPDF_LoadPage(src.Handle, i);
                    try
                    {
                        double wd = N.FPDF_GetPageWidthF(page), hd = N.FPDF_GetPageHeightF(page);
                        PageRenderer.PixelSize(wd, hd, dpi, 60_000_000, out int wpx, out int hpx, out _);
                        buf.Ensure((long)wpx * hpx * 4);
                        PageRenderer.RenderFull(page, buf.Ptr, wpx, hpx);
                        bool color = JpegCodec.DetectColor(buf.Ptr, (long)wpx * hpx);
                        JpegCodec.Encode(buf.Ptr, gray, wpx, hpx, color, quality, jpeg);
                        int len = (int)jpeg.Length, comps = JpegCodec.Components(jpeg.GetBuffer(), len);

                        var ops = new StringBuilder();
                        ops.Append("q ").Append(F(wd)).Append(" 0 0 ").Append(F(hd)).Append(" 0 0 cm /Im0 Do Q\n");
                        foreach (var item in items.Where(x => x.PageIndex == i))
                            foreach (var s in item.Shapes)
                            {
                                if (s is RectShape r)
                                    ops.Append("/GS1 gs 1 0 0 rg 1 0 0 RG 1 w ").Append(F(r.X)).Append(' ').Append(F(hd - r.Y - r.H)).Append(' ')
                                       .Append(F(r.W)).Append(' ').Append(F(r.H)).Append(" re B\n");
                                else
                                {
                                    var st = (StrokeShape)s;
                                    ops.Append("/GS2 gs 1 0 0 RG 1 J 1 j ").Append(F(st.WidthPt)).Append(" w ");
                                    for (int k = 0; k < st.Points.Count; k++)
                                        ops.Append(F(st.Points[k].X)).Append(' ').Append(F(hd - st.Points[k].Y)).Append(k == 0 ? " m " : " l ");
                                    if (st.Points.Count == 1) ops.Append(F(st.Points[0].X)).Append(' ').Append(F(hd - st.Points[0].Y)).Append(" l ");
                                    ops.Append("S\n");
                                }
                            }

                        int img = w.Reserve(), cont = w.Reserve(), pg = w.Reserve();
                        w.WriteStream(img, "/Type /XObject /Subtype /Image /Width " + wpx + " /Height " + hpx + " /ColorSpace " +
                            (comps == 1 ? "/DeviceGray" : "/DeviceRGB") + " /BitsPerComponent 8 /Filter /DCTDecode", jpeg.GetBuffer(), 0, len);
                        w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes(ops.ToString()));
                        w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + F(wd) + " " + F(hd) + "] /Resources << /XObject << /Im0 " + img +
                            " 0 R >> /ExtGState << /GS1 << /ca 0.25 /CA 1 >> /GS2 << /CA 0.38 >> >> >> /Contents " + cont + " 0 R >>");
                        kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
                    }
                    finally { N.FPDF_ClosePage(page); }
                }
            }
            // Embedded files. Name-tree keys must be sorted: "kuronuri-project.json" < "original.pdf".
            int efJson = w.Reserve(), fsJson = w.Reserve(), efOrig = w.Reserve(), fsOrig = w.Reserve();
            w.WriteStream(efJson, "/Type /EmbeddedFile /Params << /Size " + projectJson.Length + " >>", projectJson);
            w.WriteObject(fsJson, "<< /Type /Filespec /F (" + HybridWriter.ProjectName + ") /UF (" + HybridWriter.ProjectName + ") /EF << /F " + efJson + " 0 R >> >>");
            w.WriteStream(efOrig, "/Type /EmbeddedFile /Params << /Size " + original.Length + " >>", original);
            w.WriteObject(fsOrig, "<< /Type /Filespec /F (" + HybridWriter.OriginalName + ") /UF (" + HybridWriter.OriginalName + ") /EF << /F " + efOrig + " 0 R >> >>");
            w.WriteObject(pages, "<< /Type /Pages /Count " + n + " /Kids [" + kids + "] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(" + HybridWriter.ProjectName + ") " + fsJson +
                " 0 R (" + HybridWriter.OriginalName + ") " + fsOrig + " 0 R] >> >> >>");
            w.Finish(catalog);
        }
    }
}
```

## PrintPaginator.cs

移設先: src/Kuronuri.Core/Printing/

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Printing
{
    /// <summary>
    /// Print source for "print redacted": each page is rendered and burned on demand, so the spooler only ever
    /// receives already-redacted pixels. Landscape pages are rotated 90 degrees to fit the paper; pages are never enlarged.
    /// Must be used on an STA thread. Usage: new PrintDialog().PrintDocument(paginator, "Kuronuri") (never put a file name in the job name).
    /// </summary>
    public sealed class RedactedPaginator : DocumentPaginator
    {
        private readonly PdfDoc _doc;
        private readonly Dictionary<int, IReadOnlyList<Shape>> _byPage;
        private readonly RedactionStyle _style;
        private readonly double _dpi;
        private readonly NativeBuffer _buf = new NativeBuffer();
        private Size _paper;                 // device-independent units (1/96 inch)
        public int Calls;

        public RedactedPaginator(PdfDoc doc, IReadOnlyList<RedactionItem> items, RedactionStyle style, double dpi, Size paper)
        {
            _doc = doc; _style = style; _dpi = dpi; _paper = paper;
            _byPage = items.GroupBy(i => i.PageIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<Shape>)g.SelectMany(i => i.Shapes).ToList());
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _doc.PageCount;
        public override Size PageSize { get => _paper; set => _paper = value; }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            Calls++;
            IntPtr page = PdfiumNative.FPDF_LoadPage(_doc.Handle, pageNumber);
            if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed");
            BitmapSource bmp; double wd, hd;
            try
            {
                wd = PdfiumNative.FPDF_GetPageWidthF(page); hd = PdfiumNative.FPDF_GetPageHeightF(page);
                PageRenderer.PixelSize(wd, hd, _dpi, 60_000_000, out int wpx, out int hpx, out _);
                _buf.Ensure((long)wpx * hpx * 4);
                PageRenderer.RenderFull(page, _buf.Ptr, wpx, hpx);
                if (_byPage.TryGetValue(pageNumber, out var shapes) &&
                    Burner.Burn(_buf.Ptr, wpx, hpx, wpx / wd, hpx / hd, shapes, _style) != 0)
                    throw new InvalidOperationException("burn verification failed");
                bmp = BitmapSource.Create(wpx, hpx, 96, 96, PixelFormats.Bgr32, null, _buf.Ptr, checked(wpx * hpx * 4), wpx * 4);   // copies the buffer
                bmp.Freeze();
            }
            finally { PdfiumNative.FPDF_ClosePage(page); }

            double pw = wd * 96 / 72, ph = hd * 96 / 72;
            bool rotate = (pw > ph) != (_paper.Width > _paper.Height);
            double cw = rotate ? ph : pw, ch = rotate ? pw : ph;
            double k = Math.Min(1, Math.Min(_paper.Width / cw, _paper.Height / ch));
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform((_paper.Width - cw * k) / 2, (_paper.Height - ch * k) / 2));
                dc.PushTransform(new ScaleTransform(k, k));
                if (rotate) { dc.PushTransform(new TranslateTransform(ph, 0)); dc.PushTransform(new RotateTransform(90)); }
                dc.DrawImage(bmp, new Rect(0, 0, pw, ph));
            }
            return new DocumentPage(dv, _paper, new Rect(_paper), new Rect(_paper));
        }

        public void Release() => _buf.Dispose();
    }
}
```

## TestPdfFactory.cs

移設先: tests/Kuronuri.Core.Tests/

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kuronuri.Core.Export;

namespace Kuronuri.Poc
{
    /// <summary>Generates synthetic test PDFs without embedding fonts (standard Helvetica, ASCII only).</summary>
    public static class TestPdfFactory
    {
        public sealed class PageSpec
        {
            public double[] MediaBox = { 0, 0, 595, 842 };
            public double[]? CropBox;
            public int Rotate;
            public bool Unbalanced;     // leaves a modified CTM at the end of the content stream
            public bool NoMarkers;
            public int TextLines;
        }
        public sealed class Marker { public string Name = ""; public double UX, UY; public int R, G, B; }
        public sealed class Result
        {
            public byte[] Bytes = new byte[0];
            public List<Marker[]> Markers = new List<Marker[]>();
            public List<double[]> TextBox = new List<double[]>();   // x, y, w, h in user space
            public List<double[]> BlueBox = new List<double[]>();
        }

        public const string SecretText = "SECRET-TEXT-12345";

        public static Result Build(IList<PageSpec> specs, bool withJunk)
        {
            string F(double v) => MiniPdfWriter.F(v);
            var res = new Result();
            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pages = w.Reserve(), font = w.Reserve();
            w.WriteObject(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var kids = new StringBuilder();
            int firstPage = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                double[] box = s.CropBox ?? s.MediaBox;
                double x0 = box[0], y0 = box[1], x1 = box[2], y1 = box[3];
                var c = new StringBuilder();
                if (!s.NoMarkers)
                {
                    c.Append("1 0 0 rg ").Append(F(x0 + 50)).Append(' ').Append(F(y0 + 60)).Append(" 40 40 re f\n");
                    c.Append("0 0.6 0 rg ").Append(F(x1 - 130)).Append(' ').Append(F(y1 - 190)).Append(" 40 40 re f\n");
                    c.Append("0 0 1 rg ").Append(F(x0 + 200)).Append(' ').Append(F(y0 + 300)).Append(" 60 20 re f\n");
                }
                c.Append("0 g BT /F1 18 Tf ").Append(F(x0 + 100)).Append(' ').Append(F(y0 + 500)).Append(" Td (").Append(SecretText).Append(") Tj ET\n");
                if (s.TextLines > 0)
                {
                    c.Append("BT /F1 9 Tf 11 TL ").Append(F(x0 + 40)).Append(' ').Append(F(y1 - 40)).Append(" Td\n");
                    for (int k = 0; k < s.TextLines; k++)
                        c.Append("(Line ").Append(k).Append(": The quick brown fox jumps over the lazy dog 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ) Tj T*\n");
                    c.Append("ET\n");
                }
                if (s.Unbalanced) c.Append("0.5 0 0 0.5 200 200 cm\n");

                res.Markers.Add(s.NoMarkers ? new Marker[0] : new[]
                {
                    new Marker { Name = "red", UX = x0 + 70, UY = y0 + 80, R = 255, G = 0, B = 0 },
                    new Marker { Name = "green", UX = x1 - 110, UY = y1 - 170, R = 0, G = 153, B = 0 },
                    new Marker { Name = "blue", UX = x0 + 230, UY = y0 + 310, R = 0, G = 0, B = 255 },
                });
                res.TextBox.Add(new[] { x0 + 96, y0 + 494, 200.0, 26.0 });
                res.BlueBox.Add(new[] { x0 + 200, y0 + 300, 60.0, 20.0 });

                int cont = w.Reserve(), pg = w.Reserve();
                if (i == 0) firstPage = pg;
                w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes(c.ToString()));
                string annots = "";
                if (withJunk && i == 0)
                {
                    int an = w.Reserve();
                    w.WriteObject(an, "<< /Type /Annot /Subtype /Text /Rect [" + F(x0 + 400) + " " + F(y0 + 700) + " " + F(x0 + 420) + " " + F(y0 + 720) + "] /Contents (SECRET-ANNOT) >>");
                    annots = " /Annots [" + an + " 0 R]";
                }
                w.WriteObject(pg, "<< /Type /Page /Parent " + pages + " 0 R /MediaBox [" + F(s.MediaBox[0]) + " " + F(s.MediaBox[1]) + " " + F(s.MediaBox[2]) + " " + F(s.MediaBox[3]) + "]" +
                    (s.CropBox == null ? "" : " /CropBox [" + F(s.CropBox[0]) + " " + F(s.CropBox[1]) + " " + F(s.CropBox[2]) + " " + F(s.CropBox[3]) + "]") +
                    " /Rotate " + s.Rotate + " /Resources << /Font << /F1 " + font + " 0 R >> >> /Contents " + cont + " 0 R" + annots + " >>");
                kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
            }
            w.WriteObject(pages, "<< /Type /Pages /Count " + specs.Count + " /Kids [" + kids + "] >>");

            string extraCatalog = "", extraTrailer = "";
            if (withJunk)
            {
                int info = w.Reserve(), outlines = w.Reserve(), item = w.Reserve(), ef = w.Reserve(), fs = w.Reserve(), xmp = w.Reserve();
                w.WriteObject(info, "<< /Title (SECRET-META-TITLE) /Author (SECRET-AUTHOR) >>");
                w.WriteObject(outlines, "<< /Type /Outlines /First " + item + " 0 R /Last " + item + " 0 R /Count 1 >>");
                w.WriteObject(item, "<< /Title (SECRET-BOOKMARK) /Parent " + outlines + " 0 R /Dest [" + firstPage + " 0 R /Fit] >>");
                w.WriteStream(ef, "/Type /EmbeddedFile", MiniPdfWriter.Latin1.GetBytes("SECRET-ATTACH-PAYLOAD"));
                w.WriteObject(fs, "<< /Type /Filespec /F (secret.txt) /UF (secret.txt) /EF << /F " + ef + " 0 R >> >>");
                w.WriteStream(xmp, "/Type /Metadata /Subtype /XML", MiniPdfWriter.Latin1.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">SECRET-XMP</x:xmpmeta>"));
                extraCatalog = " /Outlines " + outlines + " 0 R /Names << /EmbeddedFiles << /Names [(secret.txt) " + fs + " 0 R] >> >> /Metadata " + xmp + " 0 R";
                extraTrailer = " /Info " + info + " 0 R";
            }
            w.WriteObject(catalog, "<< /Type /Catalog /Pages " + pages + " 0 R" + extraCatalog + " >>");
            w.Finish(catalog, extraTrailer);
            res.Bytes = ms.ToArray();
            return res;
        }

        public static List<PageSpec> StandardSpecs() => new List<PageSpec>
        {
            new PageSpec(),                                                                             // A4 portrait
            new PageSpec { Rotate = 90 },
            new PageSpec { MediaBox = new double[] { 0, 0, 1191, 842 }, Rotate = 180 },                 // A3 landscape
            new PageSpec { MediaBox = new double[] { 100, 50, 695, 892 }, CropBox = new double[] { 120, 80, 675, 862 }, Rotate = 270 },
            new PageSpec { Unbalanced = true },
        };
    }
}
```

## Program.cs

移設先: tests/Kuronuri.Core.Tests/（xUnit に分割）

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Poc
{
    internal static class Program
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _fails;
        private static string OutDir = "";
        private static TestPdfFactory.Result Td = new TestPdfFactory.Result();
        private static List<PageGeometry> Geoms = new List<PageGeometry>();
        private static List<PageInfo> Pages = new List<PageInfo>();
        private static List<RedactionItem> Items = new List<RedactionItem>();
        private static byte[] Json = new byte[0];

        private static void Log(string id, string status, string name, string detail)
        {
            string line = id.PadRight(4) + " " + status.PadRight(4) + " " + name + (detail.Length > 0 ? "  | " + detail : "");
            Lines.Add(line); Console.WriteLine(line);
        }
        private static void Check(string id, string name, bool ok, string detail = "") { if (!ok) _fails++; Log(id, ok ? "PASS" : "FAIL", name, detail); }
        private static void Info(string id, string name, string detail) => Log(id, "INFO", name, detail);
        private static void Run(string id, Action a)
        {
            try { a(); }
            catch (Exception e) { Check(id, "EXCEPTION", false, e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim()); }
        }

        private static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

        private static int Count(byte[] hay, byte[] needle)
        {
            int c = 0;
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) c++;
            }
            return c;
        }
        private static int SecretCount(byte[] pdf) =>
            Count(pdf, Encoding.ASCII.GetBytes("SECRET")) + Count(pdf, Encoding.Unicode.GetBytes("SECRET")) + Count(pdf, Encoding.BigEndianUnicode.GetBytes("SECRET"));

        private static int CharCount(IntPtr page)
        {
            IntPtr tp = N.FPDFText_LoadPage(page);
            if (tp == IntPtr.Zero) return -1;
            int n = N.FPDFText_CountChars(tp);
            N.FPDFText_ClosePage(tp);
            return n;
        }
        private static string PageText(IntPtr page)
        {
            IntPtr tp = N.FPDFText_LoadPage(page);
            if (tp == IntPtr.Zero) return "";
            int n = N.FPDFText_CountChars(tp);
            var buf = new byte[(n + 1) * 2];
            int got = n > 0 ? N.FPDFText_GetText(tp, 0, n, buf) : 0;
            N.FPDFText_ClosePage(tp);
            return got > 0 ? Encoding.Unicode.GetString(buf, 0, (got - 1) * 2) : "";
        }
        private static bool Near(Raster r, double xd, double yd, int er, int eg, int eb, int tol, out string got)
        {
            r.Rgb(xd, yd, out int cr, out int cg, out int cb);
            got = "(" + cr + "," + cg + "," + cb + ")";
            return Math.Abs(cr - er) <= tol && Math.Abs(cg - eg) <= tol && Math.Abs(cb - eb) <= tol;
        }
        private static T WithPage<T>(PdfDoc doc, int index, Func<IntPtr, T> f)
        {
            IntPtr page = N.FPDF_LoadPage(doc.Handle, index);
            if (page == IntPtr.Zero) throw new InvalidOperationException("load page " + index);
            try { return f(page); } finally { N.FPDF_ClosePage(page); }
        }
        private static string D(double v) => v.ToString("0.###");

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            OutDir = Path.GetFullPath(args.Length > 0 ? args[0] : "out");
            Directory.CreateDirectory(OutDir);
            Run("P0", P0);
            if (_fails > 0) return Finish();
            Run("P1", P1);
            if (_fails > 0) return Finish();
            Run("P2", P2);
            Run("P3-5", P345);
            Run("P4", P4);
            Run("P6", P6);
            Run("P7", P7);
            Run("P8", P8);
            Run("P9", P9);
            Run("P10", P10);
            Run("P11", P11);
            Run("P12", P12);
            Run("P13", P13);
            Run("PNG", () =>
            {
                DumpPng("hybrid.pdf", 0, "view_hybrid_p1.png");
                DumpPng("hybrid.pdf", 1, "view_hybrid_p2_rot90.png");
                DumpPng("hybrid.pdf", 3, "view_hybrid_p4_rot270.png");
                DumpPng("final_WhiteWithBorder_auto.pdf", 3, "view_final_white_p4.png");
                DumpPng("final_Black_jpeg.pdf", 1, "view_final_black_p2.png");
            });
            return Finish();
        }

        private static int Finish()
        {
            Console.WriteLine();
            Console.WriteLine(_fails == 0 ? "ALL PASS" : "FAILURES: " + _fails);
            File.WriteAllLines(Path.Combine(OutDir, "poc-result.txt"), Lines, new UTF8Encoding(false));
            return _fails;
        }

        // ---------------------------------------------------------------- P0 environment
        private static void P0()
        {
            PdfiumLibrary.Load();
            var fi = new FileInfo(PdfiumLibrary.DllPath);
            Check("P0", "pdfium.dll absolute-path load + init", true);
            Info("P0", "pdfium.dll", fi.Length + " bytes, fileVersion=" + FileVersionInfo.GetVersionInfo(fi.FullName).FileVersion + ", sha256=" + Sha(File.ReadAllBytes(fi.FullName)));
            Info("P0", "runtime", "CLR " + Environment.Version + ", x64=" + (IntPtr.Size == 8));
            string dir = Path.GetDirectoryName(fi.FullName)!;
            var files = Directory.GetFiles(dir).Where(f => !f.EndsWith(".pdb")).Select(f => new FileInfo(f)).ToList();
            Info("P0", "app folder", files.Count + " files, " + files.Sum(f => f.Length) / 1024 + " KB: " + string.Join(", ", files.Select(f => f.Name + "=" + f.Length / 1024 + "KB")));
        }

        // ---------------------------------------------------------------- P1 test data
        private static void P1()
        {
            Td = TestPdfFactory.Build(TestPdfFactory.StandardSpecs(), true);
            string path = Path.Combine(OutDir, "original.pdf");
            File.WriteAllBytes(path, Td.Bytes);
            using (var doc = PdfDoc.FromBytes(Td.Bytes))
            {
                Check("P1", "test PDF opens (memory loader), 5 pages", doc.PageCount == 5, "pages=" + doc.PageCount);
                bool text = WithPage(doc, 0, p => PageText(p).Contains(TestPdfFactory.SecretText));
                bool meta = N.FPDF_GetMetaText(doc.Handle, "Title", null, 0) > 2;
                bool bm = N.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero) != IntPtr.Zero;
                int att = N.FPDFDoc_GetAttachmentCount(doc.Handle);
                int ann = WithPage(doc, 0, p => N.FPDFPage_GetAnnotCount(p));
                Check("P1", "secrets are present in the original (text/meta/bookmark/attachment/annotation)", text && meta && bm && att == 1 && ann == 1,
                    "text=" + text + " meta=" + meta + " bookmark=" + bm + " attachments=" + att + " annots=" + ann + " rawSECRET=" + SecretCount(Td.Bytes));

                for (int i = 0; i < doc.PageCount; i++)
                {
                    var g = WithPage(doc, i, PageGeometry.FromPage);
                    Geoms.Add(g);
                    Pages.Add(new PageInfo(g.Wd, g.Hd, g.Rotation * 90));
                    // Redaction 1: rectangle over the secret text. Redaction 2: freehand stroke over the blue marker.
                    double[] tb = Td.TextBox[i], bb = Td.BlueBox[i];
                    CoordMap.UserToDisplay(g, tb[0], tb[1], out double ax, out double ay);
                    CoordMap.UserToDisplay(g, tb[0] + tb[2], tb[1] + tb[3], out double bx, out double by);
                    var rect = new RectShape(Math.Min(ax, bx) - 4, Math.Min(ay, by) - 4, Math.Abs(bx - ax) + 8, Math.Abs(by - ay) + 8);
                    var pts = new List<Point2>();
                    foreach (double ux in new[] { bb[0] + 5, bb[0] + bb[2] / 2, bb[0] + bb[2] - 5 })
                    {
                        CoordMap.UserToDisplay(g, ux, bb[1] + bb[3] / 2, out double px, out double py);
                        pts.Add(new Point2(px, py));
                    }
                    Items.Add(new RedactionItem(Guid.NewGuid(), i, new Shape[] { rect }, new Reason("1号", "個人情報", "")));
                    Items.Add(new RedactionItem(Guid.NewGuid(), i, new Shape[] { new StrokeShape(pts, 30) }, null));
                }
            }
            using (var doc = PdfDoc.FromStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
                Check("P1", "FPDF_LoadCustomDocument via read-only FileStream callback", doc.PageCount == 5 && WithPage(doc, 3, p => PageText(p).Contains(TestPdfFactory.SecretText)));
            var project = new RedactionProject(new OriginalInfo(Sha(Td.Bytes), Td.Bytes.Length, "original.pdf"), Pages, Items, RedactionStyle.Black);
            Json = ProjectJson.Serialize(project, "0.0.0-poc");
        }

        // ---------------------------------------------------------------- P2 coordinates
        private static void P2()
        {
            double[][] expected = { new[] { 595.0, 842 }, new[] { 842.0, 595 }, new[] { 1191.0, 842 }, new[] { 782.0, 555 }, new[] { 595.0, 842 } };
            using (var doc = PdfDoc.FromBytes(Td.Bytes))
                for (int i = 0; i < doc.PageCount; i++)
                {
                    var g = Geoms[i];
                    Info("P2", "page " + (i + 1) + " geometry", "rot=" + g.Rotation * 90 + " box=[" + D(g.X0) + " " + D(g.Y0) + " " + D(g.X1) + " " + D(g.Y1) + "] display=" + D(g.Wd) + "x" + D(g.Hd));
                    Check("P2", "page " + (i + 1) + " display size", Math.Abs(g.Wd - expected[i][0]) < 0.01 && Math.Abs(g.Hd - expected[i][1]) < 0.01);
                    double[] m = CoordMap.DisplayToUserMatrix(g);
                    bool pix = true, dev = true, inv = true, d2p = true; string detail = "";
                    WithPage(doc, i, page =>
                    {
                        using (var r = Raster.Render(page, 100))
                        {
                            foreach (var mk in Td.Markers[i])
                            {
                                CoordMap.UserToDisplay(g, mk.UX, mk.UY, out double xd, out double yd);
                                if (!Near(r, xd, yd, mk.R, mk.G, mk.B, 12, out string got)) { pix = false; detail += mk.Name + got + " "; }
                                N.FPDF_PageToDevice(page, 0, 0, r.W, r.H, 0, mk.UX, mk.UY, out int dx, out int dy);
                                if (Math.Abs(dx - xd * r.Sx) > 1.5 || Math.Abs(dy - yd * r.Sy) > 1.5) { dev = false; detail += mk.Name + ":dev(" + dx + "," + dy + ")vs(" + D(xd * r.Sx) + "," + D(yd * r.Sy) + ") "; }
                                CoordMap.DisplayToUser(m, xd, yd, out double ux, out double uy);
                                if (Math.Abs(ux - mk.UX) > 1e-6 || Math.Abs(uy - mk.UY) > 1e-6) inv = false;
                            }
                            foreach (var px in new[] { new[] { 100, 200 }, new[] { r.W - 50, r.H - 80 } })
                            {
                                N.FPDF_DeviceToPage(page, 0, 0, r.W, r.H, 0, px[0], px[1], out double X, out double Y);
                                CoordMap.DisplayToUser(m, px[0] / r.Sx, px[1] / r.Sy, out double ux, out double uy);
                                if (Math.Abs(ux - X) > 0.75 || Math.Abs(uy - Y) > 0.75) { d2p = false; detail += "d2p(" + D(X) + "," + D(Y) + ")vs(" + D(ux) + "," + D(uy) + ") "; }
                            }
                        }
                        return 0;
                    });
                    Check("P2", "page " + (i + 1) + " marker pixels at formula positions", pix, detail);
                    Check("P2", "page " + (i + 1) + " formula == FPDF_PageToDevice / FPDF_DeviceToPage, matrix inverse", dev && d2p && inv, detail);
                }
        }

        // ---------------------------------------------------------------- P3-P5 burn, final export, verification
        private static void P345()
        {
            foreach (var combo in new[] { new { Style = RedactionStyle.Black, Lossless = false }, new { Style = RedactionStyle.Black, Lossless = true }, new { Style = RedactionStyle.WhiteWithBorder, Lossless = true } })
            {
                var style = combo.Style;
                var ms = new MemoryStream();
                List<FinalPageReport> rep;
                var sw = Stopwatch.StartNew();
                using (var doc = PdfDoc.FromBytes(Td.Bytes)) rep = FinalExporter.Export(doc, Items, style, ms, new FinalExportOptions { TryLossless = combo.Lossless });
                sw.Stop();
                byte[] fin = ms.ToArray();
                File.WriteAllBytes(Path.Combine(OutDir, "final_" + style + (combo.Lossless ? "_auto" : "_jpeg") + ".pdf"), fin);
                string tag = "[" + style + (combo.Lossless ? ", auto codec] " : ", JPEG only] ");
                Info("P4", tag + "export 5 pages @200dpi q85", fin.Length / 1024 + " KB, " + sw.ElapsedMilliseconds + " ms, " +
                    string.Join(" ", rep.Select(r => r.WidthPx + "x" + r.HeightPx + (r.Color ? "c" : "g") + (r.Lossless ? "/flate:" : "/jpeg:") + r.ImageBytes / 1024 + "KB")));
                Check("P4", tag + "codec choice as expected", rep.All(r => r.Lossless == combo.Lossless));
                Check("P3", tag + "burn verified on the pixel buffer (export did not throw)", true);
                var s1 = OutputVerifier.VerifyStructure(fin, 5);
                Check("P5", tag + "structure whitelist (objects = 2+3N, contiguous, nothing else)", s1.Count == 0, string.Join("; ", s1));
                var s2 = OutputVerifier.VerifyWithPdfium(fin, Pages, Items, style);
                Check("P5", tag + "PDFium re-open: no text/annots/attachments/bookmarks/metadata, 1 image per page, burned pixels", s2.Count == 0, string.Join("; ", s2));
                Check("P5", tag + "raw byte search for SECRET (ASCII/UTF-16)", SecretCount(fin) == 0, "count=" + SecretCount(fin));
                Check("P4", tag + "colour pages detected as colour", rep.All(r => r.Color));

                if (style != RedactionStyle.Black) continue;
                int tol = combo.Lossless ? 2 : 40;
                bool keep = true, burned = true, outside = true; string detail = "";
                using (var doc = PdfDoc.FromBytes(fin))
                    for (int i = 0; i < 5; i++)
                        WithPage(doc, i, page =>
                        {
                            using (var r = Raster.Render(page, 100))
                            {
                                foreach (var mk in Td.Markers[i])
                                {
                                    CoordMap.UserToDisplay(Geoms[i], mk.UX, mk.UY, out double xd, out double yd);
                                    if (mk.Name == "blue") { if (!Near(r, xd, yd, 0, 0, 0, 48, out string g1)) { burned = false; detail += "p" + (i + 1) + "blue" + g1 + " "; } }
                                    else if (!Near(r, xd, yd, mk.R, mk.G, mk.B, tol, out string g2)) { keep = false; detail += "p" + (i + 1) + mk.Name + g2 + " "; }
                                }
                                var rc = (RectShape)Items[i * 2].Shapes[0];
                                if (!Near(r, rc.X - 3, rc.Y - 3, 255, 255, 255, 40, out string g3)) { outside = false; detail += "p" + (i + 1) + "outside" + g3 + " "; }
                            }
                            return 0;
                        });
                Check("P5", "final output: unredacted markers kept at the same display positions (all rotations)", keep, detail);
                Check("P5", "final output: stroke burned over the blue marker, white just outside the rectangle", burned && outside, detail);

                // Negative tests: the verifier must notice tampering.
                byte[] tampered = fin.Concat(Encoding.ASCII.GetBytes("\n1 0 obj\n<< /X (SECRET) >>\nendobj\n")).ToArray();
                Check("P5", "verifier rejects bytes appended after EOF", OutputVerifier.VerifyStructure(tampered, 5).Count > 0);
                var ms2 = new MemoryStream();
                using (var doc = PdfDoc.FromBytes(Td.Bytes)) FinalExporter.Export(doc, new List<RedactionItem>(), style, ms2, new FinalExportOptions());
                Check("P5", "verifier rejects an output whose redactions were not burned", OutputVerifier.VerifyWithPdfium(ms2.ToArray(), Pages, Items, style).Count > 0);
            }
        }

        // ---------------------------------------------------------------- P4 size / time / auto dpi
        private static void P4()
        {
            var dense = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { NoMarkers = true, TextLines = 68 } }, false);
            bool gray = true;
            foreach (int dpi in new[] { 150, 200, 300 })
            {
                var ms = new MemoryStream();
                var sw = Stopwatch.StartNew();
                List<FinalPageReport> rep;
                using (var doc = PdfDoc.FromBytes(dense.Bytes)) rep = FinalExporter.Export(doc, new List<RedactionItem>(), RedactionStyle.Black, ms, new FinalExportOptions { Dpi = dpi });
                sw.Stop();
                gray &= !rep[0].Color;
                Info("P4", "dense text page (A4, black text) @" + dpi + "dpi, auto codec", rep[0].WidthPx + "x" + rep[0].HeightPx + " " + (rep[0].Color ? "colour" : "gray") + (rep[0].Lossless ? " flate" : " jpeg") + ", " + ms.Length / 1024 + " KB, " + sw.ElapsedMilliseconds + " ms");
                if (dpi == 200) Check("P4", "dense text page output (gray, lossless) passes structure + PDFium verification",
                    OutputVerifier.VerifyStructure(ms.ToArray(), 1).Count == 0 && OutputVerifier.VerifyWithPdfium(ms.ToArray(), new[] { new PageInfo(595, 842, 0) }, new List<RedactionItem>(), RedactionStyle.Black).Count == 0);
            }
            Check("P4", "black-text page detected as gray (/DeviceGray)", gray);

            var a0 = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { MediaBox = new double[] { 0, 0, 2384, 3370 }, TextLines = 20 } }, false);
            var msA0 = new MemoryStream();
            var swA0 = Stopwatch.StartNew();
            List<FinalPageReport> repA0;
            using (var doc = PdfDoc.FromBytes(a0.Bytes)) repA0 = FinalExporter.Export(doc, new List<RedactionItem>(), RedactionStyle.Black, msA0, new FinalExportOptions { Dpi = 300 });
            swA0.Stop();
            long px = (long)repA0[0].WidthPx * repA0[0].HeightPx;
            Check("P4", "A0 page @300dpi is reduced under 60 MP automatically", px <= 60_000_000 && OutputVerifier.VerifyStructure(msA0.ToArray(), 1).Count == 0,
                repA0[0].WidthPx + "x" + repA0[0].HeightPx + " = " + px / 1000000 + " MP, effective " + D(repA0[0].EffectiveDpi) + " dpi, " + swA0.ElapsedMilliseconds + " ms");
        }

        // ---------------------------------------------------------------- P6 hybrid (vector import + attachments)
        private static void P6()
        {
            var opt = new HybridOptions { DrawText = false };
            byte[] hyb = HybridWriter.Build(Td.Bytes, Json, Items, opt);
            File.WriteAllBytes(Path.Combine(OutDir, "hybrid_notext.pdf"), hyb);
            bool read = HybridReader.TryRead(hyb, out byte[]? orig, out byte[]? json);
            Check("P6", "attachments read back", read);
            Check("P6", "embedded original is byte-identical (SHA-256)", read && Sha(orig!) == Sha(Td.Bytes), read ? orig!.Length + " bytes" : "");
            Check("P6", "project JSON is byte-identical and deserializes to the same data", read && json!.SequenceEqual(Json) &&
                ProjectJson.Serialize(ProjectJson.Deserialize(json!), "0.0.0-poc").SequenceEqual(Json));
            Info("P6", "size", "original " + Td.Bytes.Length + " B, hybrid " + hyb.Length + " B (x" + D((double)hyb.Length / Td.Bytes.Length) + ")");

            bool size = true, text = true, overlay = true, markers = true; string detail = "";
            using (var doc = PdfDoc.FromBytes(hyb))
            {
                Check("P6", "hybrid page count", doc.PageCount == 5);
                for (int i = 0; i < doc.PageCount; i++)
                    WithPage(doc, i, page =>
                    {
                        var g = PageGeometry.FromPage(page);
                        if (Math.Abs(g.Wd - Pages[i].Width) > 0.01 || Math.Abs(g.Hd - Pages[i].Height) > 0.01 || g.Rotation != Geoms[i].Rotation) size = false;
                        if (!PageText(page).Contains(TestPdfFactory.SecretText)) text = false;
                        using (var r = Raster.Render(page, 100))
                        {
                            var rc = (RectShape)Items[i * 2].Shapes[0];
                            r.Rgb(rc.X + 1.5, rc.Y + 1.5, out int cr, out int cg, out int cb);
                            if (!(cr >= 200 && cg >= 100 && cg <= 235 && cr - cg >= 20 && Math.Abs(cg - cb) <= 12)) { overlay = false; detail += "p" + (i + 1) + "rect(" + cr + "," + cg + "," + cb + ") "; }
                            r.Rgb(rc.X - 3, rc.Y - 3, out cr, out cg, out cb);
                            if (!(cr >= 240 && cg >= 240 && cb >= 240)) { overlay = false; detail += "p" + (i + 1) + "outside(" + cr + "," + cg + "," + cb + ") "; }
                            foreach (var mk in Td.Markers[i])
                            {
                                CoordMap.UserToDisplay(Geoms[i], mk.UX, mk.UY, out double xd, out double yd);
                                r.Rgb(xd, yd, out cr, out cg, out cb);
                                if (mk.Name == "blue") { if (!(cr >= 40 && cb <= 235 && cb >= 100)) { overlay = false; detail += "p" + (i + 1) + "stroke(" + cr + "," + cg + "," + cb + ") "; } }
                                else if (Math.Abs(cr - mk.R) > 12 || Math.Abs(cg - mk.G) > 12 || Math.Abs(cb - mk.B) > 12) { markers = false; detail += "p" + (i + 1) + mk.Name + "(" + cr + "," + cg + "," + cb + ") "; }
                            }
                        }
                        return 0;
                    });
            }
            Check("P6", "imported pages keep size / rotation; text still extractable (searchable for reviewers)", size && text);
            Check("P6", "translucent red overlays at the right display positions on all rotations (incl. unbalanced CTM page)", overlay, detail);
            Check("P6", "original markers unchanged in the hybrid", markers, detail);

            // Re-save cycles: always rebuilt from the embedded original.
            byte[] cur = hyb; var lens = new List<int> { hyb.Length }; bool same = true;
            for (int k = 0; k < 3; k++)
            {
                HybridReader.TryRead(cur, out byte[]? o, out byte[]? j);
                same &= Sha(o!) == Sha(Td.Bytes);
                cur = HybridWriter.Build(o!, j!, ProjectJson.Deserialize(j!).Items, opt);
                lens.Add(cur.Length);
            }
            Check("P6", "3 re-save cycles: original hash stable, size does not grow", same && lens.All(l => Math.Abs(l - lens[0]) <= 16), string.Join(" -> ", lens));
        }

        // ---------------------------------------------------------------- P7 fallback hybrid (raster visual layer, own writer)
        private static void P7()
        {
            var ms = new MemoryStream();
            using (var doc = PdfDoc.FromBytes(Td.Bytes)) HybridRasterWriter.Build(doc, Td.Bytes, Json, Items, ms, 150, 75);
            byte[] hyb = ms.ToArray();
            File.WriteAllBytes(Path.Combine(OutDir, "hybrid_raster.pdf"), hyb);
            bool read = HybridReader.TryRead(hyb, out byte[]? orig, out byte[]? json);
            Check("P7", "own-writer EmbeddedFiles readable through PDFium; original byte-identical; JSON identical",
                read && Sha(orig!) == Sha(Td.Bytes) && json!.SequenceEqual(Json), "hybrid " + hyb.Length + " B (x" + D((double)hyb.Length / Td.Bytes.Length) + ")");
            bool overlay = true; string detail = "";
            using (var doc = PdfDoc.FromBytes(hyb))
                for (int i = 0; i < doc.PageCount; i++)
                    WithPage(doc, i, page =>
                    {
                        using (var r = Raster.Render(page, 100))
                        {
                            var rc = (RectShape)Items[i * 2].Shapes[0];
                            r.Rgb(rc.X + 1.5, rc.Y + 1.5, out int cr, out int cg, out int cb);
                            if (!(cr >= 200 && cg >= 100 && cg <= 235 && cr - cg >= 20)) { overlay = false; detail += "p" + (i + 1) + "(" + cr + "," + cg + "," + cb + ") "; }
                        }
                        return 0;
                    });
            Check("P7", "raster hybrid: overlays at the right positions on all pages", overlay, detail);
        }

        // ---------------------------------------------------------------- P8 Japanese text as outlines (no fonts)
        private static void P8()
        {
            byte[] plain = HybridWriter.Build(Td.Bytes, Json, Items, new HybridOptions { DrawText = false });
            byte[] withText = HybridWriter.Build(Td.Bytes, Json, Items, new HybridOptions());
            File.WriteAllBytes(Path.Combine(OutDir, "hybrid.pdf"), withText);
            int chars0, chars1; long diff = 0;
            using (var a = PdfDoc.FromBytes(plain))
            using (var b = PdfDoc.FromBytes(withText))
            {
                chars0 = WithPage(a, 0, CharCount); chars1 = WithPage(b, 0, CharCount);
                IntPtr pa = N.FPDF_LoadPage(a.Handle, 0), pb = N.FPDF_LoadPage(b.Handle, 0);
                using (var ra = Raster.Render(pa, 100))
                using (var rb = Raster.Render(pb, 100))
                    unsafe
                    {
                        uint* x = (uint*)ra.Ptr, y = (uint*)rb.Ptr;
                        for (long i = 0; i < (long)ra.W * ra.H; i++) if (x[i] != y[i]) diff++;
                    }
                N.FPDF_ClosePage(pa); N.FPDF_ClosePage(pb);
            }
            int fonts0 = Count(plain, Encoding.ASCII.GetBytes("/Font")), fonts1 = Count(withText, Encoding.ASCII.GetBytes("/Font"));
            Check("P8", "Japanese watermark + labels drawn as outline paths (pixels changed, no text objects, no fonts added)",
                diff > 500 && chars0 == chars1 && fonts0 == fonts1, "changed pixels=" + diff + ", chars " + chars0 + "->" + chars1 + ", /Font " + fonts0 + "->" + fonts1);
            Info("P8", "size cost of outlines", (withText.Length - plain.Length) / 5 + " bytes per page (watermark + 2 labels)");
            Check("P8", "hybrid with text still round-trips the original", HybridReader.TryRead(withText, out byte[]? o, out _) && Sha(o!) == Sha(Td.Bytes));

            // Per-page outline paths vs one shared Form XObject: must look the same, shared must be smaller.
            byte[] perPage = HybridWriter.Build(Td.Bytes, Json, Items, new HybridOptions { SharedWatermark = false });
            File.WriteAllBytes(Path.Combine(OutDir, "hybrid_perpage_wm.pdf"), perPage);
            long worst = 0; string per = "";
            using (var a = PdfDoc.FromBytes(perPage))
            using (var b = PdfDoc.FromBytes(withText))
                for (int i = 0; i < 5; i++)
                {
                    IntPtr pa = N.FPDF_LoadPage(a.Handle, i), pb = N.FPDF_LoadPage(b.Handle, i);
                    long d = 0;
                    using (var ra = Raster.Render(pa, 100))
                    using (var rb = Raster.Render(pb, 100))
                        unsafe
                        {
                            byte* x = (byte*)ra.Ptr, y = (byte*)rb.Ptr;
                            for (long k = 0; k < (long)ra.W * ra.H * 4; k++) if (Math.Abs(x[k] - y[k]) > 24) { d++; k |= 3; }
                        }
                    N.FPDF_ClosePage(pa); N.FPDF_ClosePage(pb);
                    worst = Math.Max(worst, d); per += d + " ";
                }
            Check("P8", "shared Form XObject watermark renders the same as per-page paths on all rotations", worst < 200, "differing pixels per page: " + per);
            Info("P8", "watermark size", "per-page paths " + (perPage.Length - plain.Length) / 5 + " B/page, shared XObject " + (withText.Length - plain.Length) / 5 + " B/page (5 pages); /FlateDecode count=" + Count(withText, Encoding.ASCII.GetBytes("/FlateDecode")));
        }

        private static void DumpPng(string pdfName, int pageIndex, string pngName)
        {
            using (var doc = PdfDoc.FromBytes(File.ReadAllBytes(Path.Combine(OutDir, pdfName))))
                WithPage(doc, pageIndex, page =>
                {
                    using (var r = Raster.Render(page, 60))
                    {
                        var src = System.Windows.Media.Imaging.BitmapSource.Create(r.W, r.H, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, r.Ptr, r.W * r.H * 4, r.W * 4);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
                        using (var fs = File.Create(Path.Combine(OutDir, pngName))) enc.Save(fs);
                    }
                    return 0;
                });
        }

        // ---------------------------------------------------------------- P12 experiment: JPEG vs Flate for text pages
        private static unsafe void P12()
        {
            var dense = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { NoMarkers = true, TextLines = 68 } }, false);
            using (var doc = PdfDoc.FromBytes(dense.Bytes))
                WithPage(doc, 0, page =>
                {
                    PageRenderer.PixelSize(595, 842, 200, long.MaxValue, out int w, out int h, out _);
                    using (var buf = new NativeBuffer())
                    using (var scratch = new NativeBuffer())
                    using (var jpeg = new MemoryStream())
                    {
                        buf.Ensure((long)w * h * 4);
                        PageRenderer.RenderFull(page, buf.Ptr, w, h);
                        foreach (bool noisy in new[] { false, true })
                        {
                            uint* p = (uint*)buf.Ptr;
                            if (noisy)   // simulate scanner grain: +-10 per pixel
                            {
                                uint seed = 12345;
                                for (long i = 0; i < (long)w * h; i++)
                                {
                                    seed = seed * 1664525 + 1013904223;
                                    int v = (int)(p[i] & 0xFF) + (int)((seed >> 24) % 21) - 10;
                                    uint c = (uint)Math.Min(255, Math.Max(0, v));
                                    p[i] = 0xFF000000 | (c << 16) | (c << 8) | c;
                                }
                            }
                            var g8 = new byte[w * h];
                            for (int i = 0; i < g8.Length; i++) g8[i] = (byte)(p[i] & 0xFF);
                            var up = new byte[(w + 1) * h];   // PNG "Up" predictor rows
                            for (int y = 0; y < h; y++)
                            {
                                up[y * (w + 1)] = 2;
                                for (int x = 0; x < w; x++) up[y * (w + 1) + 1 + x] = (byte)(g8[y * w + x] - (y > 0 ? g8[(y - 1) * w + x] : 0));
                            }
                            var g4 = new byte[((w + 1) / 2) * h];
                            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) g4[y * ((w + 1) / 2) + x / 2] |= (byte)((g8[y * w + x] >> 4) << (x % 2 == 0 ? 4 : 0));
                            var g1 = new byte[((w + 7) / 8) * h];
                            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (g8[y * w + x] >= 128) g1[y * ((w + 7) / 8) + x / 8] |= (byte)(0x80 >> (x % 8));

                            string tag = noisy ? "noisy (scan-like) text page @200dpi" : "clean text page @200dpi";
                            var sw = Stopwatch.StartNew();
                            JpegCodec.Encode(buf.Ptr, scratch, w, h, false, 85, jpeg);
                            string r = "JPEG q85 " + jpeg.Length / 1024 + "KB/" + sw.ElapsedMilliseconds + "ms";
                            void M(string name, byte[] data, System.IO.Compression.CompressionLevel lv)
                            {
                                var t = Stopwatch.StartNew();
                                int n = MiniPdfWriter.ZlibCompress(data, lv).Length;
                                r += ", " + name + " " + n / 1024 + "KB/" + t.ElapsedMilliseconds + "ms";
                            }
                            M("Flate8", g8, System.IO.Compression.CompressionLevel.Optimal);
                            M("Flate8-fastest", g8, System.IO.Compression.CompressionLevel.Fastest);
                            M("Flate8-Up", up, System.IO.Compression.CompressionLevel.Optimal);
                            M("Flate4bpp", g4, System.IO.Compression.CompressionLevel.Optimal);
                            M("Flate1bpp", g1, System.IO.Compression.CompressionLevel.Optimal);
                            Info("P12", tag, r);
                        }
                    }
                    return 0;
                });
        }

        // ---------------------------------------------------------------- P13 print paginator (to an in-memory XPS, no printer needed)
        private static void P13()
        {
            var ms = new MemoryStream();
            var uri = new Uri("pack://kuronuri-poc.xps");
            int calls;
            using (var doc = PdfDoc.FromBytes(Td.Bytes))
            {
                var pag = new Kuronuri.Core.Printing.RedactedPaginator(doc, Items, RedactionStyle.Black, 150, new System.Windows.Size(793.7, 1122.5));
                var pkg = System.IO.Packaging.Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
                System.IO.Packaging.PackageStore.AddPackage(uri, pkg);
                try
                {
                    var xps = new System.Windows.Xps.Packaging.XpsDocument(pkg, System.IO.Packaging.CompressionOption.Normal, uri.AbsoluteUri);
                    System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xps).Write(pag);
                    xps.Close();
                }
                finally { System.IO.Packaging.PackageStore.RemovePackage(uri); pkg.Close(); pag.Release(); }
                calls = pag.Calls;
            }
            Check("P13", "DocumentPaginator renders + burns each page on demand into an XPS (same path PrintDialog.PrintDocument uses)",
                calls >= 5 && ms.Length > 10000, "GetPage calls=" + calls + ", XPS " + ms.Length / 1024 + " KB");
        }

        // ---------------------------------------------------------------- P9 300 pages, memory
        private static void P9()
        {
            var specs = Enumerable.Range(0, 300).Select(_ => new TestPdfFactory.PageSpec { TextLines = 40 }).ToList();
            string path = Path.Combine(OutDir, "big300.pdf"), outPath = Path.Combine(OutDir, "big300_final.pdf");
            File.WriteAllBytes(path, TestPdfFactory.Build(specs, false).Bytes);
            var items = Enumerable.Range(0, 300).Select(i => new RedactionItem(Guid.NewGuid(), i, new Shape[] { new RectShape(100, 100, 200, 30) }, null)).ToList();
            var proc = Process.GetCurrentProcess();
            foreach (int gcEvery in new[] { 0, 8 })
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var ws = new Dictionary<int, long>();
                long peak = 0;
                var sw = Stopwatch.StartNew();
                using (var doc = PdfDoc.FromStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    FinalExporter.Export(doc, items, RedactionStyle.Black, fs, new FinalExportOptions { GcEveryPages = gcEvery }, (done, total) =>
                    {
                        proc.Refresh();
                        long w = proc.WorkingSet64;
                        if (w > peak) peak = w;
                        if (done == 20 || done == 150 || done == 300) ws[done] = w;
                    });
                sw.Stop();
                long mb(long v) => v / (1024 * 1024);
                Info("P9", "300 pages @200dpi, GcEveryPages=" + gcEvery, sw.ElapsedMilliseconds / 300 + " ms/page, output " + mb(new FileInfo(outPath).Length) + " MB, working set MB @20/150/300 = " +
                    mb(ws[20]) + "/" + mb(ws[150]) + "/" + mb(ws[300]) + ", peak " + mb(peak));
                bool flat = ws[300] - ws[20] < 100L * 1024 * 1024 && peak < 600L * 1024 * 1024;
                if (gcEvery == 0) Info("P9", "memory flat without forced GC", flat ? "yes" : "NO");
                else Check("P9", "memory stays flat over 300 pages (with GcEveryPages=8)", flat);
            }
            Check("P9", "300-page output passes the structure whitelist", OutputVerifier.VerifyStructure(File.ReadAllBytes(outPath), 300).Count == 0);
        }

        // ---------------------------------------------------------------- P10 experiment: incremental save
        private static void P10()
        {
            byte[] inc;
            using (var doc = PdfDoc.FromBytes(Td.Bytes))
            {
                IntPtr page = N.FPDF_LoadPage(doc.Handle, 0);
                N.FPDFPage_InsertObject(page, HybridWriter.ShapePath(CoordMap.DisplayToUserMatrix(Geoms[0]), Items[0].Shapes[0]));
                N.FPDFPage_GenerateContent(page);
                N.FPDF_ClosePage(page);
                using (var ms = new MemoryStream()) { doc.SaveTo(ms, N.FPDF_INCREMENTAL); inc = ms.ToArray(); }
            }
            bool prefix = inc.Length > Td.Bytes.Length && inc.Take(Td.Bytes.Length).SequenceEqual(Td.Bytes);
            bool reopen = false;
            try { using (var doc = PdfDoc.FromBytes(inc)) reopen = doc.PageCount == 5; } catch { }
            Info("P10", "experiment: FPDF_INCREMENTAL save (classic-xref original only)", "original bytes kept as prefix=" + prefix + ", re-opens=" + reopen + ", appended " + (inc.Length - Td.Bytes.Length) + " bytes");
        }

        // ---------------------------------------------------------------- P11 misc net48 facts
        private static void P11()
        {
            var tricky = new RedactionProject(new OriginalInfo("ab", 1234567890123, "名前 \"引用\" \\ /.pdf"),
                new[] { new PageInfo(595.275573730469, 841.889770507813, 90) },
                new[]
                {
                    new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(0.1 + 0.2, 1e-7, 123.456789012345678, 1.0 / 3) }, new Reason("2号", "法人等情報", "自由記述\n改行")),
                    new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new StrokeShape(new[] { new Point2(1.5, 2.25), new Point2(Math.PI, Math.E) }, 14.173228346456693) }, null),
                }, RedactionStyle.WhiteWithBorder);
            byte[] j1 = ProjectJson.Serialize(tricky, "1.0.0");
            var back = ProjectJson.Deserialize(j1);
            var r0 = (RectShape)back.Items[0].Shapes[0]; var s1 = (StrokeShape)back.Items[1].Shapes[0];
            bool exact = r0.X == 0.1 + 0.2 && r0.Y == 1e-7 && r0.H == 1.0 / 3 && s1.Points[1].X == Math.PI && s1.WidthPt == 14.173228346456693 &&
                back.Pages[0].Width == 595.275573730469 && back.Items[0].Reason!.FreeText == "自由記述\n改行" && back.Original.FileName == tricky.Original.FileName &&
                back.Items[1].Reason == null && back.Style == RedactionStyle.WhiteWithBorder && back.Items[0].Id == tricky.Items[0].Id;
            Check("P11", "DataContractJsonSerializer round-trip (doubles exact, Japanese, null reason)", exact && ProjectJson.Serialize(back, "1.0.0").SequenceEqual(j1));
            Info("P11", "JSON sample", Encoding.UTF8.GetString(j1));

            var a = new RectShape(1, 2, 3, 4);
            Check("P11", "records / with-expressions / value equality compile and work on net48", (a with { W = 5 }).W == 5 && a == new RectShape(1, 2, 3, 4));
            var asm = typeof(System.Windows.Controls.Ribbon.RibbonWindow).Assembly.GetName();
            Check("P11", "in-box Ribbon assembly resolves", asm.Name == "System.Windows.Controls.Ribbon", asm.Name + " " + asm.Version);
        }
    }
}
```
