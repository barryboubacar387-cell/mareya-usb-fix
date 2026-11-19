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

            // 5. Dossiers sans nom (espaces invisibles) où le virus range les fichiers de l'utilisateur :
            //    on remet leur contenu à la racine, puis on supprime le dossier vide.
            foreach (var dir in all.OfType<DirectoryInfo>().Where(d => atRoot(d) && Signatures.IsBlankName(d.Name)))
            {
                if (flagged.Contains(dir.FullName)) continue;
                // Chemin construit avec le nom exact : Windows supprime les espaces finaux de FullName,
                // qui désignerait alors la racine elle-même.
                string rawName = dir.Name;
                string path = @"\\?\" + Path.Combine(_root, rawName);
                Add(findings, flagged, Severity.Malicious, "Fichiers cachés",
                    "Dossier sans nom créé par le virus : vos fichiers seront remis à leur place et le dossier supprimé", path,
                    () => MoveBackToRoot(path, log), order: 1);
            }

            //    Même chose pour un dossier « Fichiers récupérés » laissé par une version précédente du logiciel.
            foreach (var dir in all.OfType<DirectoryInfo>().Where(d => atRoot(d)
                         && !Signatures.IsBlankName(d.Name)
                         && (d.Name.Equals(RecoveredFolderName, StringComparison.OrdinalIgnoreCase)
                             || d.Name.StartsWith(RecoveredFolderName + " (", StringComparison.OrdinalIgnoreCase)
                             || Signatures.IsWrapperFolder(d.Name))))
            {
                if (flagged.Contains(dir.FullName)) continue;
                string path = dir.FullName;
                Add(findings, flagged, Severity.Malicious, "Fichiers cachés",
                    $"Vos fichiers du dossier « {dir.Name} » seront remis à leur place et le dossier supprimé", path,
                    () => MoveBackToRoot(path, log), order: 1);
            }

            //    Dossiers « found.000 » de Windows : proposés (non cochés) à la suppression.
            foreach (var dir in all.OfType<DirectoryInfo>().Where(d => atRoot(d) && Signatures.IsChkdskFolder(d.Name)))
            {
                string path = dir.FullName;
                Add(findings, flagged, Severity.Suspect, "Dossier Windows",
                    "Créé par la réparation de disque de Windows (morceaux de fichiers abîmés .CHK) : peut être supprimé", path,
                    () => FileOps.ForceDeleteDirectory(path, log));
            }

            // 6. Tout ce qui reste caché (vos fichiers) sera rendu visible
            var deletedPrefixes = findings.Where(f => f.Category == "Dossier du virus").Select(f => f.Location + "\\").ToList();
            var toRestore = all.Where(e =>
                FileOps.IsHiddenOrSystem(e)
                && ShouldUnhide(e)
                && !flagged.Contains(e.FullName)
                && !deletedPrefixes.Any(p => e.FullName.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
            if (toRestore.Count > 0)
            {
                // Sans aucune trace du virus, des fichiers cachés peuvent être normaux : on laisse décider.
                bool infected = findings.Any(f => f.Severity == Severity.Malicious);
                string sample = string.Join(", ", toRestore.Take(3).Select(e => e.Name)) + (toRestore.Count > 3 ? "…" : "");
                findings.Add(new Finding(infected ? Severity.Malicious : Severity.Suspect, "Fichiers cachés",
                    infected
                        ? $"{toRestore.Count} fichier(s) ou dossier(s) rendus invisibles par le virus seront réaffichés ({sample})"
                        : $"{toRestore.Count} fichier(s) ou dossier(s) cachés, à réafficher si ce sont les vôtres ({sample})",
                    _root, () => UnhideAll(log), order: 2));
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

        /// <summary>
        /// Remet à la racine tout ce que contient le dossier (fichiers de l'utilisateur), en renommant
        /// « nom (2).ext » en cas de doublon, puis supprime le dossier devenu vide.
        /// </summary>
        private void MoveBackToRoot(string folder, Action<string> log)
        {
            const string longPrefix = @"\\?\";
            // toujours en chemin long : accepte les noms finissant par un espace ou un point
            if (!folder.StartsWith(longPrefix, StringComparison.Ordinal)) folder = longPrefix + folder;
            string rootLong = longPrefix + _root;
            // garde-fou : ne jamais traiter la racine elle-même comme le dossier à vider
            if (string.Equals(folder.TrimEnd('\\'), rootLong.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(folder.TrimEnd('\\')).Length == 0)
                throw new InvalidOperationException("Chemin du dossier caché invalide : opération annulée par sécurité.");

            int moved = 0;
            var failures = new List<string>();
            EmptyInto(folder, rootLong, log, ref moved, failures, depth: 0);
            log($"{moved} élément(s) remis à leur place.");
            RestoreWindowsFolders();

            if (failures.Count > 0)
                throw new IOException($"{failures.Count} élément(s) bloqué(s) ({string.Join(", ", failures.Take(3))}). " +
                                      "Fermez les fichiers et fenêtres ouverts sur ce lecteur, puis relancez.");
        }

        /// <summary>
        /// Vide un dossier-emballage du virus dans la racine. Les emballages imbriqués (le virus a pu
        /// infecter le disque plusieurs fois : « Files are hidden by Trojan » dans « Files are hidden
        /// by Trojan »…) sont ouverts à leur tour ; leurs déchets (desktop.ini, autorun_.inf) supprimés.
        /// Le dossier n'est supprimé qu'une fois vide.
        /// </summary>
        private void EmptyInto(string folder, string rootLong, Action<string> log, ref int moved, List<string> failures, int depth)
        {
            const string longPrefix = @"\\?\";
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(folder); }
            catch (Exception ex)
            {
                failures.Add(Path.GetFileName(folder));
                log($"Contenu illisible : « {Path.GetFileName(folder)} » ({ex.Message})");
                return;
            }

            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                try
                {
                    bool isDir = Directory.Exists(entry);
                    if (isDir && Signatures.IsWrapperFolder(name) && depth < 50)
                    {
                        EmptyInto(entry, rootLong, log, ref moved, failures, depth + 1);
                        continue;
                    }
                    if (!isDir && Signatures.IsWrapperJunk(name))
                    {
                        FileOps.DeleteFile(entry);
                        continue;
                    }

                    string target = FileOps.FreeName(rootLong, name, isDir);
                    Action move = () =>
                    {
                        if (isDir)
                        {
                            new DirectoryInfo(entry).Attributes &= ~(FileOps.HiddenOrSystem | FileAttributes.ReadOnly);
                            Directory.Move(entry, target);
                        }
                        else
                        {
                            File.SetAttributes(entry, File.GetAttributes(entry) & ~FileOps.HiddenOrSystem);
                            File.Move(entry, target);
                        }
                    };
                    try
                    {
                        Retry(move);
                    }
                    catch (Exception ex) when (IsAccessDenied(ex))
                    {
                        // dossier protégé (propriété du système, droits retirés) : on en reprend la propriété puis on réessaie
                        log($"Accès refusé à « {name} » : reprise de la propriété…");
                        Ownership.TakeOwnershipRecursive(entry, log);
                        Retry(move);
                    }
                    moved++;
                    string shortTarget = target.Substring(longPrefix.Length);
                    if (isDir)
                    {
                        foreach (var inside in FileOps.Walk(shortTarget, MaxDepth).Where(e => ShouldUnhide(e, anyDepth: true)))
                        {
                            try { FileOps.Unhide(inside); } catch { /* réessayé par le réaffichage général */ }
                        }
                    }
                    if (!string.Equals(Path.GetFileName(shortTarget), name, StringComparison.OrdinalIgnoreCase))
                        log($"« {name} » existait déjà à la racine : remis sous le nom « {Path.GetFileName(shortTarget)} ».");
                }
                catch (Exception ex)
                {
                    failures.Add(name);
                    log($"Impossible de remettre « {name} » à sa place : {ex.Message}");
                }
            }

            // supprimer l'emballage s'il est vide (jamais de suppression récursive : rien ne peut être perdu)
            try
            {
                if (Directory.GetFileSystemEntries(folder).Length == 0)
                {
                    Retry(() =>
                    {
                        new DirectoryInfo(folder).Attributes = FileAttributes.Directory;
                        Directory.Delete(folder, recursive: false);
                    });
                }
            }
            catch (Exception ex)
            {
                failures.Add(Path.GetFileName(folder));
                log($"Dossier vide « {Path.GetFileName(folder)} » non supprimé ({ex.Message}). " +
                    "Fermez les fenêtres de l'Explorateur ouvertes dessus, puis relancez.");
            }
        }

        /// <summary>
        /// Refus d'accès. Attention : pour un dossier, Directory.Move le signale par une IOException
        /// (code Windows 5) et non par une UnauthorizedAccessException.
        /// </summary>
        private static bool IsAccessDenied(Exception ex) =>
            ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xFFFF) == 5);

        /// <summary>Réessaie quelques fois : l'Explorateur ou l'antivirus retiennent parfois un fichier un court instant.</summary>
        private static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { action(); return; }
                catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(400 * attempt); }
                catch (UnauthorizedAccessException) when (attempt < 5) { System.Threading.Thread.Sleep(400 * attempt); }
            }
        }

        /// <summary>Recache la Corbeille et le dossier système de Windows s'ils ont été rendus visibles.</summary>
        private void RestoreWindowsFolders()
        {
            foreach (string name in new[] { "$RECYCLE.BIN", "System Volume Information" })
            {
                try
                {
                    var dir = new DirectoryInfo(Path.Combine(_root, name));
                    if (dir.Exists && (dir.Attributes & FileOps.HiddenOrSystem) != FileOps.HiddenOrSystem)
                        dir.Attributes |= FileOps.HiddenOrSystem;
                }
                catch { /* sans gravité */ }
            }
        }

        private void UnhideAll(Action<string> log)
        {
            RestoreWindowsFolders();
            int count = 0, failed = 0;
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
                    failed++;
                    log($"Impossible de réafficher {entry.FullName} ({ex.Message})");
                }
            }
            log($"{count} élément(s) réaffiché(s).");
            if (failed > 0)
                throw new IOException($"{failed} élément(s) n'ont pas pu être réaffichés (voir le journal ci-dessus)");
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

        /// <summary>
        /// Ce qu'on réaffiche : ce que le virus a caché. On laisse cachés les fichiers gérés par Windows,
        /// les liens système, et — sous la racine — les éléments seulement « cachés » sans l'attribut
        /// « système » (ex. dossiers .git) : le virus, lui, pose les deux attributs.
        /// </summary>
        private bool ShouldUnhide(FileSystemInfo e) => ShouldUnhide(e, anyDepth: false);

        private bool ShouldUnhide(FileSystemInfo e, bool anyDepth)
        {
            if (Signatures.IsSystemManagedName(e.Name)) return false;
            if (Signatures.IsBlankName(e.Name)) return false; // ses attributs s'appliqueraient au dossier parent
            if (e is DirectoryInfo d && IsVaccine(d)) return false;
            FileAttributes attrs;
            try { attrs = e.Attributes; } catch { return false; }
            if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            if (anyDepth) return true;
            bool atRoot = string.Equals(ParentOf(e), _root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            bool hiddenAndSystem = (attrs & FileAttributes.Hidden) != 0 && (attrs & FileAttributes.System) != 0;
            return atRoot || hiddenAndSystem;
        }

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
