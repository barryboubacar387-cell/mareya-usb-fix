using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// Analyse une clé USB ou un disque : raccourcis piégés, scripts cachés, autorun.inf,
    /// dossiers du virus, fichiers de l'utilisateur rendus invisibles.
    /// </summary>
    public sealed class DriveScanner
    {
        public const string VaccineFolderName = "autorun.inf";
        public const string RecoveredFolderName = "Fichiers récupérés";
        private const int MaxDepth = 12;
        private const int MaxEntries = 300_000;

        private readonly string _root;

        public DriveScanner(string root)
        {
            _root = Path.GetFullPath(root);
        }

        /// <summary>Le disque où Windows est installé : on ne le traite jamais comme une clé infectée.</summary>
        public static bool IsSystemDrive(string root)
        {
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            return string.Equals(Path.GetFullPath(root).TrimEnd('\\'), systemRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public List<Finding> Scan(Action<string> log = null)
        {
            log = log ?? (_ => { });
            if (IsSystemDrive(_root))
                throw new InvalidOperationException("Le disque système (où Windows est installé) ne peut pas être analysé par cet outil.");
            if (!Directory.Exists(_root))
                throw new DirectoryNotFoundException($"Lecteur introuvable : {_root}");

            var findings = new List<Finding>();
            var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            log($"Analyse de {_root}…");

            // 1. Parcours complet (avec limite de sécurité pour les très gros disques)
            var all = new List<FileSystemInfo>();
            foreach (var entry in FileOps.Walk(_root, MaxDepth, IsVaccine))
            {
                all.Add(entry);
                if (all.Count >= MaxEntries)
                {
                    log($"Limite de {MaxEntries} éléments atteinte : le reste du disque n'est pas analysé.");
                    break;
                }
            }
            log($"{all.Count} éléments parcourus.");

            var rootDir = new DirectoryInfo(_root);
            Func<FileSystemInfo, bool> atRoot = e => string.Equals(ParentOf(e), rootDir.FullName.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            var hiddenNames = new HashSet<string>(
                all.Where(FileOps.IsHiddenOrSystem).Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);

            // 2. Raccourcis
            foreach (var lnk in all.OfType<FileInfo>().Where(f => f.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                string text = Signatures.ReadShortcutText(lnk.FullName);
                string twin = Path.Combine(lnk.DirectoryName ?? _root, Path.GetFileNameWithoutExtension(lnk.Name));
                bool hasHiddenTwin = hiddenNames.Contains(twin);
                bool launcher = Signatures.LooksLikeLauncher(text);

                if (launcher || hasHiddenTwin)
                {
                    foreach (var name in Signatures.ReferencedFileNames(text)) referencedNames.Add(name);
                    string reason = launcher
                        ? "Raccourci piégé : il lance un script ou un programme caché"
                        : "Raccourci qui remplace un dossier ou fichier caché";
                    Add(findings, flagged, Severity.Malicious, "Raccourci", reason, lnk.FullName, () => FileOps.DeleteFile(lnk.FullName));
                }
                else if (atRoot(lnk))
                {
                    Add(findings, flagged, Severity.Suspect, "Raccourci", "Raccourci à la racine (vérifiez s'il vous appartient)", lnk.FullName,
                        () => FileOps.DeleteFile(lnk.FullName));
                }
            }

            // 3. Fichiers : autorun.inf, scripts, programmes cachés, fichiers cités par les raccourcis piégés
            foreach (var file in all.OfType<FileInfo>())
            {
                if (flagged.Contains(file.FullName)) continue;
                bool hidden = FileOps.IsHiddenOrSystem(file);
                bool referenced = referencedNames.Contains(file.Name);
                bool isScript = Signatures.IsScript(file.FullName);
                bool isExe = Signatures.IsExecutable(file.FullName);
                string path = file.FullName;

                if (atRoot(file) && file.Name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase))
                {
                    Add(findings, flagged, Severity.Malicious, "Autorun", "Fichier de lancement automatique (autorun.inf)", path, () => FileOps.DeleteFile(path));
                }
                else if ((isScript || isExe) && (referenced || hidden))
                {
                    // À la racine, ou cité par un raccourci piégé et caché : c'est le virus.
                    // Plus profond dans vos dossiers, on laisse décider (un script caché peut vous appartenir).
                    bool sure = atRoot(file) || (referenced && hidden);
                    string reason = referenced
                        ? "Fichier lancé par un raccourci piégé"
                        : (isScript ? "Script caché" : "Programme caché");
                    Add(findings, flagged, sure ? Severity.Malicious : Severity.Suspect, isScript ? "Script" : "Programme",
                        sure ? reason : reason + " (vérifiez s'il vous appartient)", path, () => FileOps.DeleteFile(path));
                }
                else if (isScript && atRoot(file))
                {
                    Add(findings, flagged, Severity.Suspect, "Script", "Script à la racine (vérifiez s'il vous appartient)", path, () => FileOps.DeleteFile(path));
                }
            }

            // 4. Dossiers cachés à la racine
            foreach (var dir in all.OfType<DirectoryInfo>().Where(d => atRoot(d)))
            {
                if (Signatures.ProtectedFolders.Contains(dir.Name) || IsVaccine(dir)) continue;
                if (!FileOps.IsHiddenOrSystem(dir)) continue;

                if (IsVirusFolder(dir, all))
                {
                    string path = dir.FullName;
                    Add(findings, flagged, Severity.Malicious, "Dossier du virus", "Dossier caché qui ne contient que des scripts ou programmes", path,
                        () => FileOps.DeleteDirectory(path));
                    // inutile de signaler séparément ce qu'il contient
                    findings.RemoveAll(f => f.Location.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));
                }
            }

            // 5. Dossiers sans nom (espaces invisibles) où le virus range les fichiers de l'utilisateur
            foreach (var dir in all.OfType<DirectoryInfo>().Where(d => atRoot(d) && Signatures.IsBlankName(d.Name)))
            {
                if (flagged.Contains(dir.FullName)) continue;
                string path = dir.FullName;
                Add(findings, flagged, Severity.Malicious, "Fichiers cachés",
                    $"Dossier sans nom contenant vos fichiers : il sera renommé « {RecoveredFolderName} »", path,
                    () =>
                    {
                        string target = FileOps.FreeName(_root, RecoveredFolderName);
                        // préfixe \\?\ : sinon Windows supprime les espaces finaux et ne trouve pas le dossier
                        Directory.Move(@"\\?\" + path, @"\\?\" + target);
                        FileOps.Unhide(new DirectoryInfo(target));
                    }, order: 1);
            }

            // 6. Tout ce qui reste caché (vos fichiers) sera rendu visible
            var deletedPrefixes = findings.Where(f => f.Category == "Dossier du virus").Select(f => f.Location + "\\").ToList();
            int toRestore = all.Count(e =>
                FileOps.IsHiddenOrSystem(e)
                && ShouldUnhide(e)
                && !flagged.Contains(e.FullName)
                && !deletedPrefixes.Any(p => e.FullName.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
            if (toRestore > 0)
            {
                findings.Add(new Finding(Severity.Malicious, "Fichiers cachés",
                    $"{toRestore} fichier(s) ou dossier(s) rendus invisibles par le virus seront réaffichés", _root,
                    () => UnhideAll(log), order: 2));
            }

            log(findings.Count == 0 ? "Aucun problème trouvé." : $"{findings.Count} problème(s) trouvé(s).");
            return findings;
        }

        /// <summary>Applique les corrections sélectionnées. Retourne le nombre de corrections réussies.</summary>
        public static int Repair(IEnumerable<Finding> findings, Action<string> log = null)
        {
            log = log ?? (_ => { });
            int ok = 0;
            // suppressions d'abord, puis renommages, puis réaffichage des fichiers
            var ordered = findings.Where(f => f.Selected).OrderBy(f => f.Order).ToList();
            foreach (var f in ordered)
            {
                try
                {
                    f.Fix();
                    ok++;
                    log($"Corrigé : {f.Description} — {f.Location}");
                }
                catch (Exception ex)
                {
                    log($"Échec : {f.Description} — {f.Location} ({ex.Message})");
                }
            }
            return ok;
        }

        private void UnhideAll(Action<string> log)
        {
            int count = 0;
            foreach (var entry in FileOps.Walk(_root, MaxDepth, IsVaccine))
            {
                if (!ShouldUnhide(entry)) continue;
                try
                {
                    entry.Refresh();
                    if (entry.Exists && FileOps.Unhide(entry)) count++;
                }
                catch (Exception ex)
                {
                    log($"Impossible de réafficher {entry.FullName} ({ex.Message})");
                }
            }
            log($"{count} élément(s) réaffiché(s).");
        }

        /// <summary>Un dossier caché dont tout le contenu est fait de scripts ou programmes.</summary>
        private static bool IsVirusFolder(DirectoryInfo dir, List<FileSystemInfo> all)
        {
            string prefix = dir.FullName + "\\";
            var inside = all.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            var files = inside.OfType<FileInfo>().ToList();
            if (files.Count == 0) return false;
            bool onlyCode = files.All(f => Signatures.IsScript(f.FullName) || Signatures.IsExecutable(f.FullName)
                                          || f.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                                          || f.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                                          || f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase));
            bool hasCode = files.Any(f => Signatures.IsScript(f.FullName) || Signatures.IsExecutable(f.FullName));
            if (!hasCode) return false;
            if (Signatures.KnownMaliciousFolders.Contains(dir.Name)) return onlyCode || files.Count <= 10;
            return onlyCode && files.Count <= 50;
        }

        /// <summary>Fichiers que Windows garde normalement cachés : on ne les réaffiche pas.</summary>
        private bool ShouldUnhide(FileSystemInfo e) =>
            !Signatures.ProtectedFolders.Contains(e.Name)
            && !Signatures.NormallyHiddenFiles.Contains(e.Name)
            && !(e is DirectoryInfo d && IsVaccine(d));

        private bool IsVaccine(DirectoryInfo dir) =>
            dir.Name.Equals(VaccineFolderName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ParentOf(dir), _root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        private static string ParentOf(FileSystemInfo e) =>
            (e is FileInfo f ? f.DirectoryName : ((DirectoryInfo)e).Parent?.FullName)?.TrimEnd('\\') ?? string.Empty;

        private static void Add(List<Finding> findings, HashSet<string> flagged, Severity severity, string category,
            string description, string path, Action fix, int order = 0)
        {
            if (!flagged.Add(path)) return;
            findings.Add(new Finding(severity, category, description, path, fix, order));
        }
    }
}
