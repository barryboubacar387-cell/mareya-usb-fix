using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MareyaUSBFix.Core
{
    /// <summary>
    /// Nettoie l'ordinateur : c'est lui qui réinfecte chaque clé branchée.
    /// Processus du virus, démarrage automatique, registre, tâches planifiées, réglages bloqués.
    /// </summary>
    public sealed class SystemCleaner
    {
        public const string OwnTaskName = "Mareya USB Fix";

        private static readonly string[] ScriptHostProcesses = { "wscript.exe", "cscript.exe", "mshta.exe" };

        private static readonly (RegistryHive hive, RegistryView view, string path)[] RunKeys =
        {
            (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        };

        /// <summary>Réglages que le virus désactive pour empêcher qu'on le retire.</summary>
        private static readonly (string path, string value, string label)[] BlockedSettings =
        {
            (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", "Gestionnaire des tâches bloqué"),
            (@"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", "Éditeur du registre bloqué"),
            (@"Software\Policies\Microsoft\Windows\System", "DisableCMD", "Invite de commandes bloquée"),
            (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoFolderOptions", "Options des dossiers bloquées"),
        };

        public List<Finding> Scan(Action<string> log = null)
        {
            log = log ?? (_ => { });
            var findings = new List<Finding>();
            log("Analyse de l'ordinateur…");
            Safe(() => ScanProcesses(findings), "processus", log);
            Safe(() => ScanRunKeys(findings), "registre (démarrage)", log);
            Safe(() => ScanStartupFolders(findings), "dossiers de démarrage", log);
            Safe(() => ScanScheduledTasks(findings), "tâches planifiées", log);
            Safe(() => ScanBlockedSettings(findings), "réglages bloqués", log);
            log(findings.Count == 0 ? "Ordinateur : aucun problème trouvé." : $"Ordinateur : {findings.Count} problème(s) trouvé(s).");
            return findings;
        }

        // ---------- Processus ----------

        private static void ScanProcesses(List<Finding> findings)
        {
            int self = Process.GetCurrentProcess().Id;
            var removable = RemovableRoots();
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject p in results)
                {
                    int pid = Convert.ToInt32(p["ProcessId"]);
                    if (pid == self) continue;
                    string name = (p["Name"] as string) ?? "";
                    string cmd = (p["CommandLine"] as string) ?? "";
                    string exe = (p["ExecutablePath"] as string) ?? "";

                    bool scriptHost = ScriptHostProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
                    bool loaderFromUsb = (name.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)
                                          || name.Equals("regsvr32.exe", StringComparison.OrdinalIgnoreCase))
                                         && removable.Any(r => cmd.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool exeOnUsb = exe.Length > 0 && removable.Any(r => exe.StartsWith(r, StringComparison.OrdinalIgnoreCase));

                    if (scriptHost || loaderFromUsb)
                    {
                        var scripts = Signatures.ReferencedScriptPaths(cmd).Where(IsUserLocation).ToList();
                        findings.Add(new Finding(Severity.Malicious, "Processus",
                            $"Script en cours d'exécution ({name}, PID {pid})" + (scripts.Count > 0 ? $" — fichier : {string.Join(", ", scripts)}" : ""),
                            cmd.Length > 0 ? cmd : name,
                            () => { Kill(pid); scripts.ForEach(DeleteQuietly); }, order: -1));
                    }
                    else if (exeOnUsb)
                    {
                        findings.Add(new Finding(Severity.Suspect, "Processus",
                            $"Programme lancé depuis une clé USB ({name}, PID {pid})", exe, () => Kill(pid), order: -1));
                    }
                }
            }
        }

        private static void Kill(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch (ArgumentException) { /* déjà terminé */ }
        }

        // ---------- Registre : démarrage automatique ----------

        private static void ScanRunKeys(List<Finding> findings)
        {
            var removable = RemovableRoots();
            foreach (var (hive, view, path) in RunKeys)
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(path))
                {
                    if (key == null) continue;
                    foreach (string valueName in key.GetValueNames())
                    {
                        string data = key.GetValue(valueName)?.ToString() ?? "";
                        bool script = Signatures.LooksLikeScriptHost(data);
                        bool fromUsb = removable.Any(r => data.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!script && !fromUsb) continue;

                        var scripts = Signatures.ReferencedScriptPaths(data).Where(IsUserLocation).ToList();
                        string where = $@"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\{path}{(view == RegistryView.Registry32 ? " (32 bits)" : "")} → {valueName}";
                        findings.Add(new Finding(Severity.Malicious, "Démarrage",
                            $"Lancement automatique d'un script au démarrage : {data}", where,
                            () =>
                            {
                                using (var b = RegistryKey.OpenBaseKey(hive, view))
                                using (var k = b.OpenSubKey(path, writable: true))
                                    k?.DeleteValue(valueName, throwOnMissingValue: false);
                                scripts.ForEach(DeleteQuietly);
                            }));
                    }
                }
            }
        }

        // ---------- Dossiers « Démarrage » ----------

        private static void ScanStartupFolders(List<Finding> findings)
        {
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders)
            {
                foreach (string file in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    if (Signatures.IsScript(file))
                    {
                        findings.Add(new Finding(Severity.Malicious, "Démarrage", "Script lancé à chaque démarrage de Windows", file,
                            () => FileOps.DeleteFile(file)));
                    }
                    else if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string text = Signatures.ReadShortcutText(file);
                        if (!Signatures.LooksLikeScriptHost(text)) continue;
                        var targets = Signatures.ReferencedScriptPaths(text).Where(IsUserLocation).ToList();
                        findings.Add(new Finding(Severity.Malicious, "Démarrage", "Raccourci de démarrage qui lance un script", file,
                            () => { FileOps.DeleteFile(file); targets.ForEach(DeleteQuietly); }));
                    }
                }
            }
        }

        // ---------- Tâches planifiées ----------

        private static void ScanScheduledTasks(List<Finding> findings)
        {
            string tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            if (!Directory.Exists(tasksDir)) return;
            var commandRegex = new Regex(@"<Command>(.*?)</Command>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var argsRegex = new Regex(@"<Arguments>(.*?)</Arguments>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var removable = RemovableRoots();

            foreach (var entry in FileOps.Walk(tasksDir, 10).OfType<FileInfo>())
            {
                string relative = entry.FullName.Substring(tasksDir.Length); // « \Dossier\Nom »
                if (relative.TrimStart('\\').Equals(OwnTaskName, StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue; // tâches de Windows

                string xml;
                try { xml = File.ReadAllText(entry.FullName); }
                catch { continue; }

                string command = string.Join(" ", commandRegex.Matches(xml).Cast<Match>().Select(m => m.Groups[1].Value));
                string args = string.Join(" ", argsRegex.Matches(xml).Cast<Match>().Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
                string full = System.Net.WebUtility.HtmlDecode(command) + " " + args;

                bool script = Signatures.LooksLikeScriptHost(full);
                bool fromUsb = removable.Any(r => full.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!script && !fromUsb) continue;

                var scripts = Signatures.ReferencedScriptPaths(full).Where(IsUserLocation).ToList();
                findings.Add(new Finding(Severity.Malicious, "Tâche planifiée", $"Tâche qui lance un script : {full.Trim()}", relative,
                    () =>
                    {
                        RunQuiet("schtasks.exe", $"/Delete /TN \"{relative}\" /F");
                        if (File.Exists(entry.FullName)) FileOps.DeleteFile(entry.FullName);
                        scripts.ForEach(DeleteQuietly);
                    }));
            }
        }

        // ---------- Réglages bloqués par le virus ----------

        private static void ScanBlockedSettings(List<Finding> findings)
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (var (path, value, label) in BlockedSettings)
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                    using (var key = baseKey.OpenSubKey(path))
                    {
                        if (key == null) continue;
                        object v = key.GetValue(value);
                        if (v == null || Convert.ToString(v) == "0") continue;
                        var h = hive;
                        // Au niveau de la machine, ce blocage peut venir d'un administrateur : on laisse décider.
                        findings.Add(new Finding(h == RegistryHive.CurrentUser ? Severity.Malicious : Severity.Suspect,
                            "Réglage bloqué", label, $@"{(h == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\{path} → {value}",
                            () =>
                            {
                                using (var b = RegistryKey.OpenBaseKey(h, RegistryView.Default))
                                using (var k = b.OpenSubKey(path, writable: true))
                                    k?.DeleteValue(value, throwOnMissingValue: false);
                            }));
                    }
                }
            }

            // Le virus casse l'option « Afficher les fichiers cachés » pour que l'utilisateur ne voie rien.
            const string showAll = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Folder\Hidden\SHOWALL";
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
            using (var key = baseKey.OpenSubKey(showAll))
            {
                object v = key?.GetValue("CheckedValue");
                if (key != null && (v == null || Convert.ToString(v) != "1"))
                {
                    findings.Add(new Finding(Severity.Malicious, "Réglage bloqué",
                        "L'option « Afficher les fichiers cachés » a été désactivée", $@"HKLM\{showAll} → CheckedValue",
                        () =>
                        {
                            using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                            using (var k = b.OpenSubKey(showAll, writable: true))
                            {
                                if (k == null) return;
                                k.DeleteValue("CheckedValue", false);
                                k.SetValue("CheckedValue", 1, RegistryValueKind.DWord);
                            }
                        }));
                }
            }
        }

        // ---------- Utilitaires ----------

        /// <summary>Racines des lecteurs amovibles branchés (E:\, F:\…).</summary>
        public static List<string> RemovableRoots() =>
            DriveInfo.GetDrives()
                .Where(d => { try { return d.DriveType == DriveType.Removable && d.IsReady; } catch { return false; } })
                .Select(d => d.RootDirectory.FullName)
                .ToList();

        /// <summary>On ne supprime un script cité que s'il est hors de Windows et des Programmes.</summary>
        private static bool IsUserLocation(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string[] protectedRoots =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                };
                return !protectedRoots.Where(r => !string.IsNullOrEmpty(r))
                    .Any(r => full.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteQuietly(string path)
        {
            try { FileOps.DeleteFile(path); } catch { /* le fichier peut être déjà supprimé */ }
        }

        public static int RunQuiet(string exe, string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(30000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }

        private static void Safe(Action action, string what, Action<string> log)
        {
            try { action(); }
            catch (Exception ex) { log($"Analyse incomplète ({what}) : {ex.Message}"); }
        }
    }
}
