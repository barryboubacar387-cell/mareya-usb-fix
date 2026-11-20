using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// Ce qui caractérise le « virus raccourci » : des raccourcis qui lancent un interpréteur
    /// de scripts, des scripts cachés, et des fichiers qu'ils référencent.
    /// </summary>
    public static class Signatures
    {
        /// <summary>Extensions de scripts utilisées par ces virus.</summary>
        public static readonly HashSet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".ps1", ".bat", ".cmd", ".scr", ".pif"
        };

        /// <summary>Extensions de programmes, dangereuses seulement si elles sont cachées.</summary>
        public static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".com", ".cpl", ".ocx"
        };

        /// <summary>Indices, dans un raccourci, d'un lancement de script ou de programme piégé.</summary>
        private static readonly string[] LauncherTokens =
        {
            "cmd.exe", "cmd /c", "cmd  /c", "wscript", "cscript", "rundll32", "regsvr32",
            "powershell", "mshta", "/e:vbscript", "/e:jscript", "vbscript.encode", "jscript.encode",
            ".vbs", ".vbe", ".jse", ".wsf", ".hta"
        };

        /// <summary>Dossiers cachés connus créés par des variantes du virus.</summary>
        public static readonly HashSet<string> KnownMaliciousFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WindowsServices", "RECYCLER", "SysVolume"
        };

        /// <summary>Dossiers système légitimes à ne jamais toucher.</summary>
        public static readonly HashSet<string> ProtectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin", "$WinREAgent", "Recovery", "Boot", "EFI",
            "Quarantaine Mareya USB Fix", "Mareya Test Capacite"
        };

        /// <summary>Fichiers que Windows crée et garde cachés de lui-même.</summary>
        public static readonly HashSet<string> NormallyHiddenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "desktop.ini", "thumbs.db", "ehthumbs.db", ".DS_Store", "IndexerVolumeGuid", "WPSettings.dat",
            // fichiers système d'un disque (souvent verrouillés par Windows)
            "pagefile.sys", "swapfile.sys", "hiberfil.sys", "DumpStack.log", "DumpStack.log.tmp",
            "bootmgr", "BOOTNXT", "BOOTSECT.BAK", "MSOCache", "Config.Msi", "Documents and Settings",
            "ProgramData", "OneDriveTemp", ".Trash-1000", ".Spotlight-V100", ".fseventsd", ".Trashes"
        };

        /// <summary>Éléments gérés par Windows ou d'autres systèmes : noms commençant par « $ » (ex. $Windows.~BT).</summary>
        public static bool IsSystemManagedName(string name) =>
            name.StartsWith("$", StringComparison.Ordinal) || NormallyHiddenFiles.Contains(name) || ProtectedFolders.Contains(name)
            || IsChkdskFolder(name);

        private static readonly Regex ChkdskFolderRegex =
            new Regex(@"^found\.\d{3}( \(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Noms des dossiers « emballage » où des variantes du virus enferment les fichiers.</summary>
        public static readonly HashSet<string> WrapperFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Files are hidden by Trojan"
        };

        /// <summary>Dossier-emballage du virus : nom invisible (espaces) ou nom connu.</summary>
        public static bool IsWrapperFolder(string name) => IsBlankName(name) || WrapperFolderNames.Contains(name.Trim());

        /// <summary>Déchets laissés dans les dossiers-emballages : icône et autorun neutralisé.</summary>
        public static bool IsWrapperJunk(string name) =>
            name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(name, @"^autorun.*\.inf$", RegexOptions.IgnoreCase);

        /// <summary>Dossiers « found.000 » créés par la réparation de disque de Windows (chkdsk).</summary>
        public static bool IsChkdskFolder(string name) => ChkdskFolderRegex.IsMatch(name);

        /// <summary>Noms de programmes Windows cités dans les raccourcis piégés : ce ne sont pas des fichiers à supprimer.</summary>
        private static readonly HashSet<string> SystemFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "wscript.exe", "cscript.exe", "explorer.exe", "rundll32.exe", "regsvr32.exe",
            "powershell.exe", "mshta.exe", "conhost.exe", "shell32.dll", "kernel32.dll", "user32.dll"
        };

        private static readonly Regex ReferencedFileRegex = new Regex(
            @"([A-Za-z0-9_\-\.\$~#@!\(\)\[\] ]{1,120}\.(?:vbs|vbe|js|jse|wsf|wsh|hta|ps1|bat|cmd|exe|dll|scr|pif|com))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsScript(string path) => ScriptExtensions.Contains(Path.GetExtension(path));

        public static bool IsExecutable(string path) => ExecutableExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// Lit un fichier .lnk comme du texte (ASCII et UTF-16) pour y retrouver la commande lancée.
        /// Pas besoin de décoder tout le format : les chemins et arguments y figurent en clair.
        /// </summary>
        public static string ReadShortcutText(string lnkPath)
        {
            try
            {
                var info = new FileInfo(lnkPath);
                if (info.Length > 1024 * 1024) return string.Empty; // un vrai raccourci fait quelques Ko
                byte[] bytes = File.ReadAllBytes(lnkPath);
                string ascii = Encoding.ASCII.GetString(bytes);
                string unicode = Encoding.Unicode.GetString(bytes);
                return Clean(ascii) + "\n" + Clean(unicode) + "\n" + Clean(Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Clean(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsControl(c) ? ' ' : c);
            return sb.ToString();
        }

        /// <summary>Vrai si le texte (contenu d'un raccourci, valeur de registre…) lance un script ou un interpréteur.</summary>
        public static bool LooksLikeLauncher(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            return LauncherTokens.Any(t => lower.Contains(t));
        }

        private static readonly string[] ScriptHostTokens =
        {
            "wscript", "cscript", "mshta", "/e:vbscript", "/e:jscript", "vbscript.encode", "jscript.encode",
            ".vbs", ".vbe", ".jse", ".wsf", ".hta", ".js\"", ".js "
        };

        /// <summary>
        /// Version stricte pour le registre et les tâches planifiées, où cmd, rundll32 et powershell
        /// sont aussi utilisés par des logiciels normaux : seul un hôte de scripts est retenu.
        /// </summary>
        public static bool LooksLikeScriptHost(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant() + " ";
            return ScriptHostTokens.Any(t => lower.Contains(t)) || lower.TrimEnd().EndsWith(".js");
        }

        /// <summary>Noms de fichiers (scripts, programmes) cités dans un texte, sans les programmes de Windows.</summary>
        public static IEnumerable<string> ReferencedFileNames(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (Match m in ReferencedFileRegex.Matches(text))
            {
                string candidate = m.Groups[1].Value.Trim();
                // garder seulement le nom de fichier (après le dernier séparateur ou espace)
                int cut = candidate.LastIndexOfAny(new[] { '\\', '/', ' ' });
                string name = cut >= 0 ? candidate.Substring(cut + 1) : candidate;
                if (name.Length < 3 || SystemFileNames.Contains(name)) continue;
                yield return name;
            }
        }

        /// <summary>Chemins complets de scripts cités dans une commande (registre, tâche planifiée).</summary>
        public static IEnumerable<string> ReferencedScriptPaths(string command)
        {
            if (string.IsNullOrEmpty(command)) yield break;
            var regex = new Regex(@"(?:""([^""]+)""|([A-Za-z]:\\[^\s""]+|%[A-Za-z]+%\\[^\s""]+))", RegexOptions.IgnoreCase);
            foreach (Match m in regex.Matches(command))
            {
                string raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                string expanded = Environment.ExpandEnvironmentVariables(raw);
                if (IsScript(expanded)) yield return expanded;
            }
        }

        /// <summary>Un nom de dossier vide ou fait d'espaces (y compris l'espace insécable) : astuce du virus pour cacher les fichiers.</summary>
        public static bool IsBlankName(string name) =>
            string.IsNullOrWhiteSpace(name.Replace(' ', ' ').Replace(' ', ' ').Replace('　', ' ').Replace('​', ' '));
    }
}
