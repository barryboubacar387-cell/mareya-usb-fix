using System;
using System.Collections.Generic;
using System.IO;

namespace MareyaUSBFix.Core
{
    /// <summary>Opérations de fichiers robustes face aux attributs posés par le virus.</summary>
    public static class FileOps
    {
        public const FileAttributes HiddenOrSystem = FileAttributes.Hidden | FileAttributes.System;

        public static bool IsHiddenOrSystem(FileSystemInfo info)
        {
            try { return (info.Attributes & HiddenOrSystem) != 0; }
            catch { return false; }
        }

        public static void DeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        public static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var entry in Walk(path, int.MaxValue))
            {
                try { entry.Attributes = FileAttributes.Normal; } catch { /* on tente quand même la suppression */ }
            }
            new DirectoryInfo(path).Attributes = FileAttributes.Directory;
            Directory.Delete(path, true);
        }

        /// <summary>
        /// Retire les attributs « caché » et « système » (et « lecture seule » s'ils étaient posés ensemble,
        /// comme le fait le virus). Retourne vrai si l'attribut a été modifié.
        /// </summary>
        public static bool Unhide(FileSystemInfo info)
        {
            FileAttributes attrs = info.Attributes;
            if ((attrs & HiddenOrSystem) == 0) return false;
            FileAttributes cleaned = attrs & ~HiddenOrSystem;
            if ((attrs & FileAttributes.Hidden) != 0 && (attrs & FileAttributes.System) != 0)
                cleaned &= ~FileAttributes.ReadOnly;
            info.Attributes = cleaned;
            return true;
        }

        /// <summary>
        /// Parcourt l'arborescence sans suivre les liens symboliques ni entrer dans les dossiers système protégés.
        /// Les dossiers illisibles sont ignorés au lieu d'interrompre le parcours.
        /// </summary>
        public static IEnumerable<FileSystemInfo> Walk(string root, int maxDepth, Func<DirectoryInfo, bool> skipDir = null)
        {
            var stack = new Stack<(DirectoryInfo dir, int depth)>();
            stack.Push((new DirectoryInfo(root), 0));
            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); }
                catch { continue; }

                foreach (var entry in entries)
                {
                    yield return entry;
                    if (entry is DirectoryInfo sub && depth + 1 < maxDepth)
                    {
                        bool isLink;
                        try { isLink = (sub.Attributes & FileAttributes.ReparsePoint) != 0; }
                        catch { isLink = true; }
                        if (isLink) continue;
                        if (Signatures.ProtectedFolders.Contains(sub.Name)) continue;
                        if (skipDir != null && skipDir(sub)) continue;
                        stack.Push((sub, depth + 1));
                    }
                }
            }
        }

        /// <summary>Trouve un nom libre dans un dossier : « Fichiers récupérés », « Fichiers récupérés (2) »…</summary>
        public static string FreeName(string parent, string baseName)
        {
            string candidate = Path.Combine(parent, baseName);
            int i = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = Path.Combine(parent, $"{baseName} ({i++})");
            return candidate;
        }
    }
}
