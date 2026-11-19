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
            if (args.Any(a => a.Equals("/scan", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("/restore", StringComparison.OrdinalIgnoreCase)))
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
