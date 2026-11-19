using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>
    /// Mode « protection automatique » : une icône près de l'horloge, et chaque clé branchée
    /// est nettoyée sans intervention (seuls les éléments sûrement malveillants sont supprimés).
    /// </summary>
    public sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly Control _sync = new Control();
        private readonly DeviceWatcher _watcher;
        private MainForm _window;

        public TrayContext()
        {
            _sync.CreateControl();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir Mareya USB Fix", null, (s, e) => OpenWindow());
            menu.Items.Add("Désactiver la protection automatique", null, (s, e) => DisableAndExit());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter (jusqu'au prochain démarrage)", null, (s, e) => ExitThread());

            _icon = new NotifyIcon
            {
                Icon = Theme.AppIcon,
                Text = "Mareya USB Fix — protection active",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => OpenWindow();

            _watcher = new DeviceWatcher(_sync);
            _watcher.DriveArrived += root => _ = CleanAsync(root);

            // nettoyage de l'ordinateur au démarrage, puis des clés déjà branchées
            _ = StartupCleanAsync();
        }

        private async Task StartupCleanAsync()
        {
            await CleanSystemAsync(notifyIfClean: false);
            foreach (string root in SystemCleaner.RemovableRoots())
                await CleanAsync(root, includeSystem: false);
        }

        private async Task CleanSystemAsync(bool notifyIfClean)
        {
            int count = await Task.Run(() =>
            {
                var findings = new SystemCleaner().Scan().Where(f => f.Severity == Severity.Malicious).ToList();
                return DriveScanner.Repair(findings);
            });
            if (count > 0)
                Notify("Ordinateur nettoyé", $"{count} élément(s) du virus raccourci supprimé(s) de l'ordinateur.", ToolTipIcon.Warning);
            else if (notifyIfClean)
                Notify("Ordinateur", "Aucun virus trouvé.", ToolTipIcon.Info);
        }

        private async Task CleanAsync(string root, bool includeSystem = true)
        {
            if (DriveScanner.IsSystemDrive(root)) return;
            try
            {
                var (fixedCount, total) = await Task.Run(() =>
                {
                    var findings = new List<Finding>();
                    // l'ordinateur d'abord, sinon le virus réinfecte la clé aussitôt nettoyée
                    if (includeSystem) findings.AddRange(new SystemCleaner().Scan());
                    findings.AddRange(new DriveScanner(root).Scan());
                    var sure = findings.Where(f => f.Severity == Severity.Malicious).ToList();
                    return (DriveScanner.Repair(sure), sure.Count);
                });

                string drive = root.TrimEnd('\\');
                if (total == 0)
                    Notify($"Clé {drive} vérifiée", "Aucun virus trouvé.", ToolTipIcon.Info);
                else if (fixedCount == total)
                    Notify($"Clé {drive} nettoyée", $"Virus raccourci supprimé ({fixedCount} correction(s)). Vos fichiers sont de nouveau visibles.", ToolTipIcon.Warning);
                else
                    Notify($"Clé {drive} : nettoyage partiel", $"{total - fixedCount} élément(s) n'ont pas pu être supprimés. Ouvrez Mareya USB Fix.", ToolTipIcon.Error);
            }
            catch (Exception ex)
            {
                Notify("Mareya USB Fix", $"Impossible d'analyser {root} : {ex.Message}", ToolTipIcon.Error);
            }
        }

        private void Notify(string title, string text, ToolTipIcon kind) =>
            _icon.ShowBalloonTip(8000, title, text, kind);

        private void OpenWindow()
        {
            if (_window == null || _window.IsDisposed)
            {
                _window = new MainForm();
                _window.Show();
            }
            _window.WindowState = FormWindowState.Normal;
            _window.Activate();
        }

        private void DisableAndExit()
        {
            AutoProtection.Disable();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _watcher.Dispose();
            base.ExitThreadCore();
        }
    }
}
