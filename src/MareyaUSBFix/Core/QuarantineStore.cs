using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MareyaUSBFix.Core
{
    /// <summary>Un élément en quarantaine : où il était, où il est rangé.</summary>
    public sealed class QuarantineEntry
    {
        public string Stamp { get; set; }          // dossier daté de la quarantaine
        public string OriginalRelative { get; set; } // chemin d'origine, relatif à la racine du lecteur
        public string StoredPath { get; set; }     // chemin complet (\\?\…) dans la quarantaine
        public bool IsDirectory => Directory.Exists(StoredPath);
    }

    /// <summary>
    /// Registre de la quarantaine d'un lecteur : chaque dossier daté contient « restauration.txt »
    /// (une ligne par élément : chemin d'origine, tabulation, nom rangé) et le rapport de la réparation.
    /// </summary>
    public static class QuarantineStore
    {
        public const string ManifestName = "restauration.txt";
        private const string LongPrefix = @"\\?\";

        public static string QuarantineRoot(string root) => Path.Combine(root, DriveScanner.QuarantineFolderName);

        /// <summary>Note un élément mis en quarantaine (appelé par DriveScanner).</summary>
        public static void Record(string boxLong, string originalRelative, string storedLong)
        {
            string storedRelative = storedLong.Substring(boxLong.TrimEnd('\\').Length + 1);
            File.AppendAllText(Path.Combine(boxLong, ManifestName),
                originalRelative + "\t" + storedRelative + Environment.NewLine, Encoding.UTF8);
        }

        public static List<QuarantineEntry> List(string root)
        {
            var result = new List<QuarantineEntry>();
            string qRoot = LongPrefix + QuarantineRoot(root);
            if (!Directory.Exists(qRoot)) return result;

            foreach (string box in Directory.GetDirectories(qRoot).OrderByDescending(d => d))
            {
                string manifest = Path.Combine(box, ManifestName);
                if (!File.Exists(manifest)) continue;
                foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 2) continue;
                    string stored = Path.Combine(box, parts[1]);
                    if (!File.Exists(stored) && !Directory.Exists(stored)) continue;
                    result.Add(new QuarantineEntry
                    {
                        Stamp = Path.GetFileName(box),
                        OriginalRelative = parts[0],
                        StoredPath = stored
                    });
                }
            }
            return result;
        }

        /// <summary>Remet l'élément à son emplacement d'origine (renommé « (2) » si la place est prise).</summary>
        public static string Restore(string root, QuarantineEntry entry)
        {
            string rootLong = LongPrefix + root;
            string original = Path.Combine(rootLong, entry.OriginalRelative);
            string parent = Path.GetDirectoryName(original);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            bool isDir = entry.IsDirectory;
            string target = FileOps.FreeName(parent ?? rootLong, Path.GetFileName(original), isDir);
            if (isDir) Directory.Move(entry.StoredPath, target);
            else File.Move(entry.StoredPath, target);

            RemoveFromManifest(entry);
            return target.StartsWith(LongPrefix, StringComparison.Ordinal) ? target.Substring(LongPrefix.Length) : target;
        }

        private static void RemoveFromManifest(QuarantineEntry entry)
        {
            string box = Path.GetDirectoryName(entry.StoredPath);
            // l'élément peut être rangé dans un sous-dossier : remonter jusqu'au dossier daté
            while (box != null && !File.Exists(Path.Combine(box, ManifestName))) box = Path.GetDirectoryName(box);
            if (box == null) return;
            string manifest = Path.Combine(box, ManifestName);
            string storedRelative = entry.StoredPath.Substring(box.TrimEnd('\\').Length + 1);
            var kept = File.ReadAllLines(manifest, Encoding.UTF8)
                .Where(l => !l.EndsWith("\t" + storedRelative, StringComparison.OrdinalIgnoreCase)).ToList();
            File.WriteAllLines(manifest, kept, Encoding.UTF8);
        }

        /// <summary>Enregistre le rapport d'une réparation dans la quarantaine du lecteur.</summary>
        public static string SaveReport(string root, IEnumerable<string> lines)
        {
            string dir = LongPrefix + QuarantineRoot(root);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"rapport {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt");
            var header = new[]
            {
                "Mareya USB Fix — rapport de réparation",
                $"Lecteur : {root}    Date : {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                "Les éléments retirés sont dans ce dossier et peuvent être restaurés (bouton « Quarantaine… »).",
                new string('-', 70)
            };
            File.WriteAllLines(file, header.Concat(lines), Encoding.UTF8);
            return file.Substring(LongPrefix.Length);
        }
    }
}
