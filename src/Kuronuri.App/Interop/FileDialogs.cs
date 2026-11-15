using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kuronuri.App.Interop
{
    /// <summary>
    /// Vista-style file dialogs through IFileDialog so that FOS_DONTADDTORECENT can be set (ARCHITECTURE §13):
    /// opened / saved files are not added to the shell's "recent files". net48's OpenFileDialog has no such option.
    /// </summary>
    public static class FileDialogs
    {
        private const uint FOS_OVERWRITEPROMPT = 0x2, FOS_STRICTFILETYPES = 0x4, FOS_NOCHANGEDIR = 0x8, FOS_FORCEFILESYSTEM = 0x40,
            FOS_PATHMUSTEXIST = 0x800, FOS_FILEMUSTEXIST = 0x1000, FOS_DONTADDTORECENT = 0x2000000;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

        public static string? Open(Window owner, string title)
        {
            var dlg = (IFileDialog)new FileOpenDialogRcw();
            try
            {
                dlg.SetOptions(FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_FILEMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT);
                dlg.SetTitle(title);
                dlg.SetFileTypes(2, new[] { new COMDLG_FILTERSPEC("PDF ファイル (*.pdf)", "*.pdf"), new COMDLG_FILTERSPEC("すべてのファイル (*.*)", "*.*") });
                return ShowAndGet(dlg, owner);
            }
            finally { Marshal.FinalReleaseComObject(dlg); }
        }

        public static string? Save(Window owner, string title, string? folder, string fileName)
        {
            var dlg = (IFileDialog)new FileSaveDialogRcw();
            try
            {
                dlg.SetOptions(FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_OVERWRITEPROMPT | FOS_NOCHANGEDIR | FOS_STRICTFILETYPES | FOS_DONTADDTORECENT);
                dlg.SetTitle(title);
                dlg.SetFileTypes(1, new[] { new COMDLG_FILTERSPEC("PDF ファイル (*.pdf)", "*.pdf") });
                dlg.SetDefaultExtension("pdf");
                dlg.SetFileName(fileName);
                if (folder != null && Directory.Exists(folder))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out var item) == 0 && item != null)
                    {
                        dlg.SetFolder(item);
                        Marshal.FinalReleaseComObject(item);
                    }
                }
                return ShowAndGet(dlg, owner);
            }
            finally { Marshal.FinalReleaseComObject(dlg); }
        }

        private static string? ShowAndGet(IFileDialog dlg, Window owner)
        {
            IntPtr hwnd = new WindowInteropHelper(owner).Handle;
            int hr = dlg.Show(hwnd);
            if (hr == ERROR_CANCELLED_HR) return null;
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            dlg.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr p);
                try { return Marshal.PtrToStringUni(p); }
                finally { Marshal.FreeCoTaskMem(p); }
            }
            finally { Marshal.FinalReleaseComObject(item); }
        }

        [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] private class FileOpenDialogRcw { }
        [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")] private class FileSaveDialogRcw { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
            public COMDLG_FILTERSPEC(string name, string spec) { Name = name; Spec = spec; }
        }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
