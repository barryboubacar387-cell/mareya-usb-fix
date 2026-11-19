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

        /// <summary>
        /// Supprime un dossier protégé (ex. found.000, propriété du système) : on essaie normalement,
        /// sinon on en reprend la propriété pour les Administrateurs, on s'accorde les droits, puis on réessaie.
        /// </summary>
        public static void ForceDeleteDirectory(string path, Action<string> log = null)
        {
            try
            {
                DeleteDirectory(path);
                return;
            }
            catch (Exception first)
            {
                log?.Invoke($"Suppression normale refusée ({first.Message}) : reprise de la propriété de {path}…");
            }

            // reprise de propriété élément par élément, avec les privilèges d'administrateur (comme takeown /r)
            Ownership.TakeOwnershipRecursive(path, log);
            try
            {
                DeleteDirectory(path);
            }
            catch
            {
                SystemCleaner.RunQuiet("cmd.exe", $"/c rd /s /q \"\\\\?\\{path}\"");
                if (Directory.Exists(path))
                    throw new IOException("Dossier toujours protégé. Lancez « chkdsk " + Path.GetPathRoot(path).TrimEnd('\\') +
                                          " /f » en administrateur pour réparer le disque, puis réessayez.");
            }
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
                        // un nom fait d'espaces est normalisé par Windows en « dossier parent » :
                        // y entrer reparcourrait le disque entier (il est traité à part, par renommage)
                        if (Signatures.IsBlankName(sub.Name)) continue;
                        if (skipDir != null && skipDir(sub)) continue;
                        stack.Push((sub, depth + 1));
                    }
                }
            }
        }

        /// <summary>
        /// Trouve un nom libre dans un dossier : « photo.jpg », sinon « photo (2).jpg », « photo (3).jpg »…
        /// Pour un dossier, l'extension n'est pas séparée.
        /// </summary>
        public static string FreeName(string parent, string name, bool isDirectory = true)
        {
            string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
            string ext = isDirectory ? "" : Path.GetExtension(name);
            string candidate = Path.Combine(parent, name);
            int i = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = Path.Combine(parent, $"{stem} ({i++}){ext}");
            return candidate;
        }
    }
}
