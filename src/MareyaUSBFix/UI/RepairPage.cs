using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>Page « Réparation » : analyse et nettoyage du virus raccourci sur le lecteur choisi.</summary>
    public sealed class RepairPage : UserControl
    {
        private readonly ModernButton _scan = new ModernButton("Analyser", ButtonKind.Primary, Theme.Icons.Search);
        private readonly ModernButton _repair = new ModernButton("Réparer la sélection", ButtonKind.Primary, Theme.Icons.Repair);
        private readonly ModernButton _vaccinate = new ModernButton("Vacciner", ButtonKind.Secondary, Theme.Icons.Lock);
        private readonly ToggleSwitch _includeSystem = new ToggleSwitch("Nettoyer aussi l'ordinateur (recommandé)");
        private readonly Banner _banner = new Banner { Dock = DockStyle.Fill };
        private readonly SlimProgress _progress = new SlimProgress { Dock = DockStyle.Fill, Visible = false, Value = -1 };
        private readonly CheckList _list = new CheckList { Dock = DockStyle.Fill, EmptyText = "Les résultats de l'analyse s'afficheront ici." };
        private readonly ModernButton _toggleLog = new ModernButton("Journal détaillé", ButtonKind.Ghost, Theme.Icons.Info) { Height = 30 };
        private readonly TextBox _log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Font = Theme.Mono, BackColor = Theme.Surface, BorderStyle = BorderStyle.None
        };
        private readonly Panel _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 150, Visible = false, Padding = new Padding(0, 10, 0, 0), BackColor = Color.White };

        private List<Finding> _findings = new List<Finding>();
        private DriveEntry _drive;
        private string _scannedRoot;

        public bool Busy { get; private set; }
        public event Action BusyChanged;

        public RepairPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            _includeSystem.SetSilently(true);

            var actions = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 14), Margin = new Padding(0, 0, 0, 14) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Color.White };
            _includeSystem.Margin = new Padding(10, 5, 0, 0);
            flow.Controls.AddRange(new Control[] { _scan, _repair, _vaccinate, _includeSystem });
            actions.Controls.Add(flow);

            var results = new Card { Dock = DockStyle.Fill, Title = "Résultats de l'analyse", Padding = new Padding(16, 46, 16, 14), Margin = new Padding(0) };
            _toggleLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            results.Controls.Add(_list);
            _logPanel.Controls.Add(_log);
            results.Controls.Add(_logPanel);
            results.Controls.Add(_toggleLog);
            results.Resize += (s, e) => _toggleLog.Location = new Point(results.Width - _toggleLog.Width - 14, 10);
            _toggleLog.BringToFront();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background, Padding = new Padding(0) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(_banner, 0, 1);
            var progressHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 7, 4, 7), BackColor = Theme.Background };
            progressHost.Controls.Add(_progress);
            layout.Controls.Add(progressHost, 0, 2);
            layout.Controls.Add(results, 0, 3);
            _banner.Margin = new Padding(0);
            Controls.Add(layout);

            _scan.Click += async (s, e) => await ScanAsync();
            _repair.Click += async (s, e) => await RepairAsync();
            _vaccinate.Click += (s, e) => Vaccinate();
            _toggleLog.Click += (s, e) => { _logPanel.Visible = !_logPanel.Visible; _toggleLog.Text = _logPanel.Visible ? "Masquer le journal" : "Journal détaillé"; };
            _list.RowChecked += row => { if (row.Tag is Finding f) f.Selected = row.Checked; UpdateButtons(); };

            _banner.Set(BannerKind.Info, "Branchez une clé USB, choisissez-la ci-dessus puis cliquez sur « Analyser ».",
                "Vos fichiers ne sont jamais supprimés : ce qui est retiré va en quarantaine et peut être restauré.");
            UpdateButtons();
        }

        public void SetDrive(DriveEntry drive)
        {
            if (string.Equals(drive?.Root, _drive?.Root, StringComparison.OrdinalIgnoreCase)) { _drive = drive; return; }
            _drive = drive;
            _findings = new List<Finding>();
            _list.Clear();
            _log.Clear();
            if (drive == null)
                _banner.Set(BannerKind.Info, "Aucun disque sélectionné.", "Branchez une clé USB ou un disque externe.");
            else if (drive.IsSystem)
                _banner.Set(BannerKind.Neutral, $"{drive.Letter} est le disque de Windows.",
                    "La réparation concerne les clés et disques externes. Choisissez une autre tuile, ou utilisez l'onglet Diagnostic.");
            else
                _banner.Set(BannerKind.Info, $"Disque {drive.Letter} « {drive.Name} » sélectionné.", "Cliquez sur « Analyser » pour rechercher le virus raccourci.");
            UpdateButtons();
        }

        public async Task ScanAsync()
        {
            var d = _drive;
            if (d == null || d.IsSystem || Busy) return;
            SetBusy(true);
            _banner.Set(BannerKind.Info, $"Analyse de {d.Letter} en cours…", "Recherche des raccourcis piégés, scripts cachés et traces du virus.");
            _findings = new List<Finding>();
            _list.Clear();
            bool withSystem = _includeSystem.Checked;
            try
            {
                _findings = await Task.Run(() =>
                {
                    var all = new List<Finding>();
                    if (withSystem) all.AddRange(new SystemCleaner().Scan(LogFromWorker));
                    all.AddRange(new DriveScanner(d.Root).Scan(LogFromWorker));
                    return all;
                });
                _scannedRoot = d.Root;
                ShowFindings();
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                _banner.Set(BannerKind.Danger, "L'analyse a échoué.", ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async Task RepairAsync()
        {
            var selected = _findings.Where(f => f.Selected).ToList();
            if (selected.Count == 0 || Busy) return;
            int suspects = selected.Count(f => f.Severity == Severity.Suspect);
            if (suspects > 0 && MessageBox.Show(this,
                    $"Vous avez coché {suspects} élément(s) « à vérifier ». Ils seront placés en quarantaine sur le lecteur (restaurables) ; " +
                    "les dossiers found.000 sont, eux, supprimés.\n\nContinuer ?",
                    "Confirmer la réparation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string root = _scannedRoot;
            SetBusy(true);
            _banner.Set(BannerKind.Info, "Réparation en cours…", "Ne débranchez pas le lecteur.");
            try
            {
                int logStart = _log.TextLength;
                int ok = await Task.Run(() => DriveScanner.Repair(_findings, LogFromWorker));
                Log($"{ok} correction(s) sur {selected.Count} appliquée(s).");
                Application.DoEvents();
                try
                {
                    var lines = _log.Text.Substring(logStart).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    Log("Rapport enregistré : " + QuarantineStore.SaveReport(root, lines));
                }
                catch (Exception ex) { Log("Rapport non enregistré : " + ex.Message); }

                bool withSystem = _includeSystem.Checked;
                _findings = await Task.Run(() =>
                {
                    var all = new List<Finding>();
                    if (withSystem) all.AddRange(new SystemCleaner().Scan(LogFromWorker));
                    all.AddRange(new DriveScanner(root).Scan(LogFromWorker));
                    return all;
                });
                ShowFindings();

                int left = _findings.Count(f => f.Severity == Severity.Malicious);
                if (left == 0)
                {
                    _banner.Set(BannerKind.Success, "Réparation terminée : vos fichiers sont de nouveau visibles.",
                        "Les éléments retirés sont en quarantaine (menu « Quarantaine ») et un rapport a été enregistré.");
                    if (!Vaccine.IsVaccinated(root) && MessageBox.Show(this,
                            "Réparation terminée ! Vos fichiers sont de nouveau visibles.\n\nVoulez-vous vacciner cette clé pour mieux la protéger à l'avenir ?",
                            "Mareya USB Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Vaccinate();
                    try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true }); } catch { }
                }
                else
                    _banner.Set(BannerKind.Danger, $"{left} élément(s) n'ont pas pu être retirés.",
                        "Fermez les fichiers et fenêtres ouverts sur ce lecteur, puis recommencez. Détails dans le journal.");
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                _banner.Set(BannerKind.Danger, "La réparation a échoué.", ex.Message);
            }
            finally { SetBusy(false); }
        }

        private void Vaccinate()
        {
            var d = _drive;
            if (d == null || d.IsSystem) return;
            try
            {
                Vaccine.Apply(d.Root);
                Log($"{d.Root} vacciné : un dossier protégé autorun.inf empêche le virus d'y créer le sien.");
                _banner.Set(BannerKind.Success, $"Clé {d.Letter} vaccinée.", "Un dossier protégé empêche de nombreux virus de s'installer sur cette clé.");
            }
            catch (Exception ex)
            {
                Log("Vaccination impossible : " + ex.Message);
                _banner.Set(BannerKind.Danger, "Vaccination impossible.", ex.Message);
            }
        }

        private void ShowFindings()
        {
            _list.SetRows(_findings.OrderBy(f => f.Severity).ThenBy(f => f.Category).Select(f => new CheckRow
            {
                Tag = f,
                Checked = f.Selected,
                Pill = f.Severity == Severity.Malicious ? "Virus" : "À vérifier",
                PillFore = f.Severity == Severity.Malicious ? Theme.Danger : Theme.Warning,
                PillBack = f.Severity == Severity.Malicious ? Theme.DangerBack : Theme.WarningBack,
                Title = f.Description,
                Subtitle = f.Location,
                Right = f.Category
            }));

            int bad = _findings.Count(f => f.Severity == Severity.Malicious);
            int doubt = _findings.Count(f => f.Severity == Severity.Suspect);
            if (_findings.Count == 0)
            {
                _list.EmptyText = "Aucun problème trouvé.";
                _banner.Set(BannerKind.Success, "Aucun virus trouvé : ce lecteur est propre.", "Vous pouvez le vacciner pour mieux le protéger.");
            }
            else if (bad > 0)
                _banner.Set(BannerKind.Danger, $"Virus détecté : {bad} élément(s) à corriger" + (doubt > 0 ? $", {doubt} à vérifier." : "."),
                    "Les éléments du virus sont déjà cochés. Cliquez sur « Réparer la sélection ».");
            else
                _banner.Set(BannerKind.Warning, $"{doubt} élément(s) à vérifier.", "Ils ne sont pas cochés : cochez seulement ceux que vous ne reconnaissez pas.");
            UpdateButtons();
        }

        private void SetBusy(bool busy)
        {
            Busy = busy;
            _progress.Visible = busy;
            _progress.Value = -1;
            UseWaitCursor = busy;
            UpdateButtons();
            BusyChanged?.Invoke();
        }

        private void UpdateButtons()
        {
            bool usable = _drive != null && !_drive.IsSystem && !Busy;
            _scan.Enabled = usable;
            _vaccinate.Enabled = usable;
            _includeSystem.Enabled = !Busy;
            _repair.Enabled = !Busy && _findings.Any(f => f.Selected);
        }

        private void LogFromWorker(string message)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => Log(message)));
            else Log(message);
        }

        private void Log(string message) => _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");

        // ---------- Démonstration (captures d'écran) ----------

        internal void LoadDemo()
        {
            _drive = new DriveEntry { Root = @"E:\", Name = "KINGSTON", IsRemovable = true };
            _findings = new List<Finding>
            {
                new Finding(Severity.Malicious, "Raccourci", "Raccourci piégé : il lance un script ou un programme caché", @"E:\Documents.lnk", () => { }),
                new Finding(Severity.Malicious, "Raccourci", "Raccourci piégé : il lance un script ou un programme caché", @"E:\Photos.lnk", () => { }),
                new Finding(Severity.Malicious, "Script", "Fichier lancé par un raccourci piégé", @"E:\x8f2k.vbe", () => { }),
                new Finding(Severity.Malicious, "Fichiers cachés", "Dossier sans nom créé par le virus : vos fichiers seront remis à leur place", @"E:\ ", () => { }),
                new Finding(Severity.Malicious, "Démarrage", "Lancement automatique d'un script au démarrage", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run → helper", () => { }),
                new Finding(Severity.Suspect, "Raccourci", "Raccourci à la racine (vérifiez s'il vous appartient)", @"E:\Lien bloc-notes.lnk", () => { }),
            };
            _scannedRoot = @"E:\";
            ShowFindings();
            Log("Analyse de E:\\… 214 éléments parcourus. 6 problème(s) trouvé(s).");
        }
    }
}
