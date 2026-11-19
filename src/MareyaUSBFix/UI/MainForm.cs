using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>Fenêtre principale : choisir un lecteur, analyser, réparer.</summary>
    public sealed class MainForm : Form
    {
        private readonly ComboBox _drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Dock = DockStyle.Fill };
        private readonly CheckBox _includeSystem = new CheckBox { Text = "Nettoyer aussi l'ordinateur (recommandé : c'est lui qui réinfecte les clés)", Checked = true, AutoSize = true, Font = Theme.Body };
        private readonly Button _scan = MakeButton("Analyser", primary: true);
        private readonly Button _repair = MakeButton("Réparer la sélection", primary: true);
        private readonly Button _vaccinate = MakeButton("Vacciner la clé", primary: false);
        private readonly Button _refresh = MakeButton("Actualiser", primary: false);
        private readonly ListView _list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
            Font = Theme.Body, HideSelection = false, BorderStyle = BorderStyle.FixedSingle, ShowItemToolTips = true
        };
        private readonly TextBox _log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Font = Theme.Mono, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
        };
        private readonly Label _status = new Label { AutoSize = false, Dock = DockStyle.Fill, Font = Theme.Bold, TextAlign = ContentAlignment.MiddleLeft };
        private readonly CheckBox _autoProtect = new CheckBox { Text = "Protection automatique : nettoyer chaque clé dès qu'elle est branchée", AutoSize = true, Font = Theme.Body };
        private readonly ProgressBar _progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Visible = false, Dock = DockStyle.Fill, MarqueeAnimationSpeed = 30 };

        private List<Finding> _findings = new List<Finding>();
        private string _scannedRoot;
        private DeviceWatcher _watcher;
        private bool _busy;

        public MainForm()
        {
            Text = "Mareya USB Fix — Anti virus raccourci";
            Icon = Theme.AppIcon;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 600);
            Size = new Size(980, 700);
            BackColor = Color.White;

            BuildLayout();
            WireEvents();
            LoadDrives();
            UpdateButtons();
            _autoProtect.Checked = SafeIsAutoEnabled();
            _status.Text = "Branchez une clé USB, choisissez-la puis cliquez sur « Analyser ».";
            _status.ForeColor = Theme.Muted;
        }

        private static Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat, Font = Theme.Bold, Cursor = Cursors.Hand,
                BackColor = primary ? Theme.Primary : Color.White, ForeColor = primary ? Color.White : Theme.PrimaryDark
            };
            b.FlatAppearance.BorderColor = primary ? Theme.Primary : Theme.Primary;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.PrimaryDark : Theme.Surface;
            return b;
        }

        private void BuildLayout()
        {
            // En-tête
            var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Primary, Padding = new Padding(20, 12, 20, 12) };
            var title = new Label { Text = "Mareya USB Fix", Font = Theme.Title, ForeColor = Color.White, AutoSize = true, Location = new Point(20, 10) };
            var subtitle = new Label
            {
                Text = "Supprime le virus « raccourci » et récupère vos fichiers cachés sur les clés USB et disques durs.",
                Font = Theme.Subtitle, ForeColor = Color.FromArgb(0xDD, 0xE8, 0xFF), AutoSize = true, Location = new Point(22, 46)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            // Corps
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 10), ColumnCount = 1, RowCount = 7 };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // lecteur
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // option ordinateur
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // boutons
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // statut
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 62));   // liste
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 38));   // journal
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // protection auto

            var driveRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            driveRow.Controls.Add(new Label { Text = "Lecteur à réparer :", AutoSize = true, Font = Theme.Bold, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            driveRow.Controls.Add(_drives, 1, 0);
            _refresh.Margin = new Padding(8, 0, 0, 0);
            driveRow.Controls.Add(_refresh, 2, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 4), WrapContents = true };
            buttons.Controls.AddRange(new Control[] { _scan, _repair, _vaccinate });

            var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            statusRow.Controls.Add(_status, 0, 0);
            statusRow.Controls.Add(_progress, 1, 0);
            _progress.Margin = new Padding(0, 10, 0, 10);

            _list.Columns.Add("Type", 130);
            _list.Columns.Add("Problème", 420);
            _list.Columns.Add("Emplacement", 360);

            var logBox = new GroupBox { Text = "Journal", Dock = DockStyle.Fill, Font = Theme.Bold, Padding = new Padding(6) };
            logBox.Controls.Add(_log);

            body.Controls.Add(driveRow, 0, 0);
            body.Controls.Add(_includeSystem, 0, 1);
            body.Controls.Add(buttons, 0, 2);
            body.Controls.Add(statusRow, 0, 3);
            body.Controls.Add(_list, 0, 4);
            body.Controls.Add(logBox, 0, 5);
            _autoProtect.Margin = new Padding(0, 8, 0, 0);
            body.Controls.Add(_autoProtect, 0, 6);

            Controls.Add(body);
            Controls.Add(header);
        }

        private void WireEvents()
        {
            _refresh.Click += (s, e) => LoadDrives();
            _drives.SelectedIndexChanged += (s, e) => { ClearFindings(); UpdateButtons(); };
            _scan.Click += async (s, e) => await ScanAsync();
            _repair.Click += async (s, e) => await RepairAsync();
            _vaccinate.Click += (s, e) => VaccinateSelected();
            _list.ItemChecked += (s, e) =>
            {
                if (e.Item.Tag is Finding f) f.Selected = e.Item.Checked;
                UpdateButtons();
            };
            _autoProtect.Click += (s, e) => ToggleAutoProtection();

            Load += (s, e) =>
            {
                _watcher = new DeviceWatcher(this);
                _watcher.DrivesChanged += () => BeginInvoke(new Action(LoadDrives));
                _watcher.DriveArrived += root =>
                {
                    SelectDrive(root);
                    if (!_busy) _ = ScanAsync();
                };
            };
            FormClosed += (s, e) => _watcher?.Dispose();
        }

        // ---------- Lecteurs ----------

        private sealed class DriveItem
        {
            public string Root;
            public string Label;
            public override string ToString() => Label;
        }

        private void LoadDrives()
        {
            string previous = (_drives.SelectedItem as DriveItem)?.Root;
            _drives.Items.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (DriveScanner.IsSystemDrive(d.RootDirectory.FullName)) continue;
                    string kind = d.DriveType == DriveType.Removable ? "Clé USB / carte mémoire" : "Disque";
                    string name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Sans nom" : d.VolumeLabel;
                    _drives.Items.Add(new DriveItem
                    {
                        Root = d.RootDirectory.FullName,
                        Label = $"{d.RootDirectory.FullName.TrimEnd('\\')}  —  {name}  ({kind}, {FormatSize(d.TotalSize)})"
                    });
                }
                catch { /* lecteur non accessible */ }
            }

            if (_drives.Items.Count == 0)
            {
                _drives.Items.Add(new DriveItem { Root = null, Label = "Aucune clé ou disque externe détecté : branchez-en un" });
                _drives.SelectedIndex = 0;
            }
            else
            {
                SelectDrive(previous);
                if (_drives.SelectedIndex < 0)
                {
                    // de préférence une clé USB
                    var usb = _drives.Items.Cast<DriveItem>().FirstOrDefault(i => i.Label.Contains("Clé USB"));
                    _drives.SelectedItem = usb ?? _drives.Items[0];
                }
            }
            UpdateButtons();
        }

        private void SelectDrive(string root)
        {
            if (root == null) return;
            if (!_drives.Items.Cast<DriveItem>().Any(i => string.Equals(i.Root, root, StringComparison.OrdinalIgnoreCase)))
                LoadDrives();
            var item = _drives.Items.Cast<DriveItem>().FirstOrDefault(i => string.Equals(i.Root, root, StringComparison.OrdinalIgnoreCase));
            if (item != null) _drives.SelectedItem = item;
        }

        private static string FormatSize(long bytes)
        {
            double gb = bytes / 1024d / 1024d / 1024d;
            return gb >= 1 ? $"{gb:0.#} Go" : $"{bytes / 1024d / 1024d:0} Mo";
        }

        private string SelectedRoot => (_drives.SelectedItem as DriveItem)?.Root;

        // ---------- Analyse et réparation ----------

        private async Task ScanAsync()
        {
            string root = SelectedRoot;
            if (root == null || _busy) return;
            SetBusy(true, $"Analyse de {root} en cours…");
            ClearFindings();
            bool withSystem = _includeSystem.Checked;
            try
            {
                var found = await Task.Run(() =>
                {
                    var all = new List<Finding>();
                    if (withSystem) all.AddRange(new SystemCleaner().Scan(LogFromWorker));
                    all.AddRange(new DriveScanner(root).Scan(LogFromWorker));
                    return all;
                });
                _findings = found;
                _scannedRoot = root;
                ShowFindings();
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                SetStatus("L'analyse a échoué : " + ex.Message, Theme.Danger);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task RepairAsync()
        {
            var selected = _findings.Where(f => f.Selected).ToList();
            if (selected.Count == 0 || _busy) return;

            int suspects = selected.Count(f => f.Severity == Severity.Suspect);
            if (suspects > 0)
            {
                var answer = MessageBox.Show(this,
                    $"Vous avez coché {suspects} élément(s) « à vérifier ». Ils seront supprimés définitivement.\n\nContinuer ?",
                    "Confirmer la réparation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            string root = _scannedRoot;
            SetBusy(true, "Réparation en cours…");
            try
            {
                int ok = await Task.Run(() => DriveScanner.Repair(_findings, LogFromWorker));
                Log($"{ok} correction(s) sur {selected.Count} appliquée(s).");

                // nouvelle analyse pour vérifier
                bool withSystem = _includeSystem.Checked;
                var after = await Task.Run(() =>
                {
                    var all = new List<Finding>();
                    if (withSystem) all.AddRange(new SystemCleaner().Scan(LogFromWorker));
                    all.AddRange(new DriveScanner(root).Scan(LogFromWorker));
                    return all;
                });
                _findings = after;
                ShowFindings();

                int left = after.Count(f => f.Severity == Severity.Malicious);
                if (left == 0)
                {
                    SetStatus("Réparation terminée : vos fichiers sont de nouveau visibles. ✔", Theme.Success);
                    if (!Vaccine.IsVaccinated(root) && MessageBox.Show(this,
                            "Réparation terminée ! Vos fichiers sont de nouveau visibles.\n\n" +
                            "Voulez-vous vacciner cette clé pour mieux la protéger à l'avenir ?",
                            "Mareya USB Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        VaccinateSelected();
                    }
                    TryOpen(root);
                }
                else
                {
                    SetStatus($"{left} élément(s) n'ont pas pu être supprimés. Fermez les fichiers ouverts et recommencez.", Theme.Danger);
                }
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                SetStatus("La réparation a échoué : " + ex.Message, Theme.Danger);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void VaccinateSelected()
        {
            string root = SelectedRoot;
            if (root == null) return;
            try
            {
                Vaccine.Apply(root);
                Log($"{root} vacciné : un dossier protégé autorun.inf empêche le virus d'y créer le sien.");
                SetStatus($"Clé {root.TrimEnd('\\')} vaccinée.", Theme.Success);
            }
            catch (Exception ex)
            {
                Log("Vaccination impossible : " + ex.Message);
                SetStatus("Vaccination impossible : " + ex.Message, Theme.Danger);
            }
        }

        private void ToggleAutoProtection()
        {
            try
            {
                if (_autoProtect.Checked)
                {
                    AutoProtection.Enable();
                    Log("Protection automatique activée : Mareya USB Fix démarrera avec Windows et nettoiera chaque clé branchée.");
                    Process.Start(new ProcessStartInfo(AutoProtection.InstalledExe, AutoProtection.TrayArgument) { UseShellExecute = true });
                }
                else
                {
                    AutoProtection.Disable();
                    Log("Protection automatique désactivée (elle s'arrêtera au prochain redémarrage).");
                }
            }
            catch (Exception ex)
            {
                _autoProtect.Checked = SafeIsAutoEnabled();
                MessageBox.Show(this, ex.Message, "Protection automatique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool SafeIsAutoEnabled()
        {
            try { return AutoProtection.IsEnabled; } catch { return false; }
        }

        // ---------- Affichage ----------

        private void ShowFindings()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var f in _findings.OrderBy(f => f.Severity).ThenBy(f => f.Category))
            {
                var item = new ListViewItem(new[] { f.Category, f.Description, f.Location })
                {
                    Tag = f,
                    Checked = f.Selected,
                    ForeColor = f.Severity == Severity.Malicious ? Theme.Danger : Theme.Warning,
                    BackColor = f.Severity == Severity.Malicious ? Theme.DangerBack : Theme.WarningBack,
                    ToolTipText = f.Severity == Severity.Malicious ? "Élément du virus" : "À vérifier : cochez-le si vous ne le reconnaissez pas"
                };
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            int bad = _findings.Count(f => f.Severity == Severity.Malicious);
            int doubt = _findings.Count(f => f.Severity == Severity.Suspect);
            if (_findings.Count == 0)
                SetStatus("Aucun virus trouvé. Votre clé est propre. ✔", Theme.Success);
            else if (bad > 0)
                SetStatus($"Virus détecté : {bad} élément(s) à corriger" + (doubt > 0 ? $", {doubt} à vérifier" : "") + ". Cliquez sur « Réparer la sélection ».", Theme.Danger);
            else
                SetStatus($"{doubt} élément(s) à vérifier (non cochés). Cochez ceux que vous ne reconnaissez pas.", Theme.Warning);
            UpdateButtons();
        }

        private void ClearFindings()
        {
            _findings = new List<Finding>();
            _list.Items.Clear();
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            _progress.Visible = busy;
            UseWaitCursor = busy;
            if (message != null) SetStatus(message, Theme.PrimaryDark);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasDrive = SelectedRoot != null;
            _scan.Enabled = hasDrive && !_busy;
            _vaccinate.Enabled = hasDrive && !_busy;
            _refresh.Enabled = !_busy;
            _drives.Enabled = !_busy;
            _repair.Enabled = !_busy && _findings.Any(f => f.Selected);
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void LogFromWorker(string message)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => Log(message)));
            else Log(message);
        }

        private void Log(string message)
        {
            _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        }

        private static void TryOpen(string root)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true }); }
            catch { /* simple confort */ }
        }
    }
}
