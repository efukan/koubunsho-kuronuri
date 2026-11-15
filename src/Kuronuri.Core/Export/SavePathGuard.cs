using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kuronuri.Core.Export
{
    /// <summary>The destination is the original PDF or the review PDF that is currently open. Carries no path.</summary>
    public sealed class ForbiddenDestinationException : Exception
    {
        public ForbiddenDestinationException() : base("destination is a file that must not be overwritten") { }
    }

    /// <summary>
    /// Destination rules shared by every save (ARCHITECTURE §7.2, §9.3): never write over the original or the open
    /// review PDF (compared by normalized path and, when both exist, by NTFS file identity so that 8.3 names, different
    /// spellings or hard links are caught too); write "&lt;dest&gt;.part" next to the destination and rename at the end.
    /// </summary>
    public static class SavePathGuard
    {
        public const string PartSuffix = ".part";

        public static string PartPath(string destPath) => destPath + PartSuffix;

        public static bool IsAllowed(string destPath, IEnumerable<string> forbiddenPaths)
        {
            string dest = Normalize(destPath);
            string part = Normalize(PartPath(destPath));
            foreach (var f in forbiddenPaths)
            {
                if (string.IsNullOrEmpty(f)) continue;
                string nf = Normalize(f);
                if (string.Equals(nf, dest, StringComparison.OrdinalIgnoreCase) || string.Equals(nf, part, StringComparison.OrdinalIgnoreCase)) return false;
                if (SameFile(destPath, f) || SameFile(PartPath(destPath), f)) return false;
            }
            return true;
        }

        public static void EnsureAllowed(string destPath, IEnumerable<string> forbiddenPaths)
        {
            if (!IsAllowed(destPath, forbiddenPaths)) throw new ForbiddenDestinationException();
        }

        private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');

        /// <summary>Moves the finished .part over the destination (replacing an existing file).</summary>
        public static void ReplaceWithPart(string partPath, string destPath)
        {
            if (File.Exists(destPath)) File.Replace(partPath, destPath, null, true);
            else File.Move(partPath, destPath);
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // ---- file identity (volume serial + file index)

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

        private static bool TryIdentity(string path, out (uint Vol, uint Hi, uint Lo) id)
        {
            id = default;
            try
            {
                if (!File.Exists(path)) return false;
                // Read access with the widest sharing does not conflict with the app's own read-only handle on the original.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (!GetFileInformationByHandle(fs.SafeFileHandle, out var info)) return false;
                    id = (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
                    return true;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { return false; }
        }

        public static bool SameFile(string a, string b) =>
            TryIdentity(a, out var x) && TryIdentity(b, out var y) && x == y;
    }
}
