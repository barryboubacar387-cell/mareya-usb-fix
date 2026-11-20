using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MareyaUSBFix.Core;
using MareyaUSBFix.UI;

namespace MareyaUSBFix
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static int Main(string[] args)
        {
            // MareyaUSBFix.exe /selftest : vérifie les calculs internes (code 0 = tout est juste)
            if (args.Any(a => a.Equals("/selftest", StringComparison.OrdinalIgnoreCase)))
            {
                AttachConsole(-1);
                var failures = HealthScore.SelfTest();
                foreach (var f in failures) Console.WriteLine("ÉCHEC " + f);
                Console.WriteLine(failures.Count == 0 ? "Calcul de la santé : tous les cas sont justes." : $"{failures.Count} cas faux.");
                int logIdx = Array.FindIndex(args, a => a.Equals("/log", StringComparison.OrdinalIgnoreCase));
                if (logIdx >= 0 && logIdx + 1 < args.Length)
                    File.WriteAllLines(args[logIdx + 1], failures.Count == 0 ? new[] { "OK" } : failures.ToArray());
                return failures.Count;
            }

            // MareyaUSBFix.exe /capture dossier : captures d'écran de l'interface (données de démonstration)
            int cap = Array.FindIndex(args, a => a.Equals("/capture", StringComparison.OrdinalIgnoreCase));
            if (cap >= 0 && cap + 1 < args.Length)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm.CaptureScreens(args[cap + 1]);
                return 0;
            }

            if (args.Any(a => a.Equals("/scan", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("/restore", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("/health", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("/captest", StringComparison.OrdinalIgnoreCase)))
                return RunCommandLine(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool tray = args.Any(a => a.Equals(AutoProtection.TrayArgument, StringComparison.OrdinalIgnoreCase));
            Application.Run(tray ? (ApplicationContext)new TrayContext() : new ApplicationContext(new MainForm()));
            return 0;
        }

        /// <summary>
        /// Mode ligne de commande (pour les techniciens et les tests automatiques) :
        ///   MareyaUSBFix.exe /scan E:\ [/repair] [/all] [/system] [/vaccinate] [/log fichier.txt]
        /// Code de retour : nombre d'éléments malveillants restants (0 = propre).
        /// </summary>
        private static int RunCommandLine(string[] args)
        {
            AttachConsole(-1);
            var lines = new List<string>();
            void Log(string s) { Console.WriteLine(s); lines.Add(s); }

            string Arg(string name)
            {
                int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            bool Flag(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

            string target = Arg("/scan");
            string logFile = Arg("/log");
            int remaining;

            // MareyaUSBFix.exe /health E:\ : état de santé du disque (lecture seule)
            string healthRoot = Arg("/health");
            if (healthRoot != null)
            {
                try
                {
                    var h = DiskHealth.Check(healthRoot, Log);
                    Log("Verdict : " + h.VerdictText);
                    Log("Santé : " + (h.Percent.HasValue ? $"{h.Percent} % ({HealthScore.Label(h.Percent.Value)})" : "inconnue"));
                    foreach (var b in h.PercentBreakdown) Log("  " + b);
                    foreach (var r in h.Reasons) Log("  - " + r);
                    foreach (var d in h.Details) Log($"  {d.Key} : {d.Value}");
                }
                catch (Exception ex) { Log("Erreur : " + ex.Message); return -1; }
                finally { if (!string.IsNullOrEmpty(logFile)) File.WriteAllLines(logFile, lines); }
                return 0;
            }

            // MareyaUSBFix.exe /captest E:\ [/speed] [/limit Mo] : test d'authenticité (0 = authentique, 1 = erreurs)
            string capRoot = Arg("/captest");
            if (capRoot != null)
            {
                try
                {
                    long.TryParse(Arg("/limit"), out long limitMb);
                    var r = CapacityTest.Run(capRoot, Flag("/speed") ? CapacityTestMode.Speed : CapacityTestMode.Full,
                        null, System.Threading.CancellationToken.None, Log, limitMb << 20);
                    return r.Authentic ? 0 : 1;
                }
                catch (Exception ex) { Log("Erreur : " + ex.Message); return -1; }
                finally { if (!string.IsNullOrEmpty(logFile)) File.WriteAllLines(logFile, lines); }
            }

            // MareyaUSBFix.exe /restore E:\ : remet à leur place tous les éléments de la quarantaine
            string restoreRoot = Arg("/restore");
            if (restoreRoot != null)
            {
                int failed = 0;
                foreach (var entry in QuarantineStore.List(restoreRoot))
                {
                    try { Log("Restauré : " + QuarantineStore.Restore(restoreRoot, entry)); }
                    catch (Exception ex) { failed++; Log($"Échec : {entry.OriginalRelative} ({ex.Message})"); }
                }
                if (!string.IsNullOrEmpty(logFile)) File.WriteAllLines(logFile, lines);
                return failed;
            }

            try
            {
                var findings = new List<Finding>();
                if (Flag("/system")) findings.AddRange(new SystemCleaner().Scan(Log));
                var scanner = new DriveScanner(target);
                findings.AddRange(scanner.Scan(Log));
                foreach (var f in findings) Log(f.ToString());

                if (Flag("/all"))
                    foreach (var f in findings) f.Selected = true; // inclut les éléments « à vérifier »

                if (Flag("/repair"))
                {
                    int fixedCount = DriveScanner.Repair(findings, Log);
                    Log($"{fixedCount} correction(s) appliquée(s).");
                    try { Log("Rapport enregistré : " + QuarantineStore.SaveReport(target, lines.ToList())); }
                    catch (Exception ex) { Log("Rapport non enregistré : " + ex.Message); }
                    var after = new DriveScanner(target).Scan(Log);
                    foreach (var f in after) Log("Restant : " + f);
                    remaining = after.Count(f => f.Severity == Severity.Malicious);
                }
                else
                {
                    remaining = findings.Count(f => f.Severity == Severity.Malicious);
                }

                if (Flag("/vaccinate"))
                {
                    Vaccine.Apply(target);
                    Log("Clé vaccinée.");
                }
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                remaining = -1;
            }

            if (!string.IsNullOrEmpty(logFile))
                File.WriteAllLines(logFile, lines);
            return remaining;
        }
    }
}
