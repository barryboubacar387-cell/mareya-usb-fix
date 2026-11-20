using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>Onglet « Diagnostic du disque » : santé (SMART) et test d'authenticité (fausse capacité).</summary>
    public sealed class DiagnosticPanel : UserControl
    {
        private readonly ComboBox _drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, Dock = DockStyle.Fill };
        private readonly Button _refresh = Btn("Actualiser", false);
        private readonly Button _health = Btn("Vérifier la santé", true);
        private readonly Button _speed = Btn("Test de vitesse (rapide)", false);
        private readonly Button _full = Btn("Test d'authenticité (complet)", true);
        private readonly Button _stop = Btn("Arrêter", false);
        private readonly Label _verdict = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, Font = Theme.Body, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Maximum = 1000 };
        private readonly ListView _details = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Mono, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        private CancellationTokenSource _cts;
        private bool _busy;

        public DiagnosticPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            Padding = new Padding(20, 14, 20, 10);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // lecteur
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // boutons
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // verdict
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // progression
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // statut
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));    // détails
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));    // journal

            var driveRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            driveRow.Controls.Add(new Label { Text = "Disque à diagnostiquer :", AutoSize = true, Font = Theme.Bold, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            driveRow.Controls.Add(_drives, 1, 0);
            _refresh.Margin = new Padding(8, 0, 0, 0);
            driveRow.Controls.Add(_refresh, 2, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            buttons.Controls.AddRange(new Control[] { _health, _speed, _full, _stop });

            _details.Columns.Add("Élément", 220);
            _details.Columns.Add("Valeur", 600);
            var logBox = new GroupBox { Text = "Journal", Dock = DockStyle.Fill, Font = Theme.Bold, Padding = new Padding(6) };
            logBox.Controls.Add(_log);

            layout.Controls.Add(driveRow, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(_verdict, 0, 2);
            layout.Controls.Add(_progress, 0, 3);
            layout.Controls.Add(_status, 0, 4);
            layout.Controls.Add(_details, 0, 5);
            layout.Controls.Add(logBox, 0, 6);
            Controls.Add(layout);

            _refresh.Click += (s, e) => LoadDrives();
            _health.Click += async (s, e) => await CheckHealthAsync();
            _speed.Click += async (s, e) => await CapacityAsync(CapacityTestMode.Speed);
            _full.Click += async (s, e) => await CapacityAsync(CapacityTestMode.Full);
            _stop.Click += (s, e) => _cts?.Cancel();
            _drives.SelectedIndexChanged += (s, e) => UpdateButtons();

            LoadDrives();
            _verdict.Text = "Choisissez un disque puis lancez un diagnostic.";
            _verdict.ForeColor = Theme.Muted;
            _status.Text = "« Vérifier la santé » est instantané et ne modifie rien. Le test d'authenticité détecte les fausses clés.";
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat, Font = Theme.Bold, Cursor = Cursors.Hand,
                BackColor = primary ? Theme.Primary : Color.White, ForeColor = primary ? Color.White : Theme.PrimaryDark
            };
            b.FlatAppearance.BorderColor = Theme.Primary;
            return b;
        }

        private sealed class DriveItem
        {
            public string Root, Label;
            public bool IsSystem;
            public override string ToString() => Label;
        }

        public void LoadDrives()
        {
            string previous = (_drives.SelectedItem as DriveItem)?.Root;
            _drives.Items.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed)) continue;
                    bool sys = DriveScanner.IsSystemDrive(d.RootDirectory.FullName);
                    string kind = d.DriveType == DriveType.Removable ? "Clé USB / carte" : sys ? "Disque de Windows" : "Disque";
                    string name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Sans nom" : d.VolumeLabel;
                    _drives.Items.Add(new DriveItem
                    {
                        Root = d.RootDirectory.FullName, IsSystem = sys,
                        Label = $"{d.RootDirectory.FullName.TrimEnd('\\')}  —  {name}  ({kind}, {d.TotalSize / 1e9:0.#} Go, {d.AvailableFreeSpace / 1e9:0.#} Go libres)"
                    });
                }
                catch { /* lecteur indisponible */ }
            }
            var again = _drives.Items.Cast<DriveItem>().FirstOrDefault(i => i.Root == previous)
                        ?? _drives.Items.Cast<DriveItem>().FirstOrDefault(i => i.Label.Contains("Clé USB"))
                        ?? _drives.Items.Cast<DriveItem>().FirstOrDefault();
            if (again != null) _drives.SelectedItem = again;
            UpdateButtons();
        }

        private DriveItem Selected => _drives.SelectedItem as DriveItem;

        private async Task CheckHealthAsync()
        {
            var d = Selected;
            if (d == null || _busy) return;
            SetBusy(true);
            _details.Items.Clear();
            ShowVerdict("Lecture des informations du disque…", Theme.PrimaryDark);
            try
            {
                var report = await Task.Run(() => DiskHealth.Check(d.Root, LogFromWorker));
                foreach (var kv in report.Details) _details.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value }));
                foreach (var reason in report.Reasons)
                    _details.Items.Add(new ListViewItem(new[] { "⚠ Constat", reason }) { ForeColor = Theme.Warning });
                Color c = report.Verdict == HealthVerdict.Good ? Theme.Success
                        : report.Verdict == HealthVerdict.Warning ? Theme.Warning
                        : report.Verdict == HealthVerdict.Critical ? Theme.Danger : Theme.Muted;
                string icon = report.Verdict == HealthVerdict.Good ? "🟢" : report.Verdict == HealthVerdict.Warning ? "🟠"
                            : report.Verdict == HealthVerdict.Critical ? "🔴" : "⚪";
                ShowVerdict($"{icon}  Santé : {report.VerdictText}", c);
                Log($"Santé de {d.Root} : {report.VerdictText}");
            }
            catch (Exception ex)
            {
                ShowVerdict("Diagnostic impossible : " + ex.Message, Theme.Danger);
                Log("Erreur : " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async Task CapacityAsync(CapacityTestMode mode)
        {
            var d = Selected;
            if (d == null || _busy) return;
            if (d.IsSystem)
            {
                MessageBox.Show(this, "Le test d'authenticité remplit l'espace libre : il n'est pas proposé pour le disque de Windows.",
                    "Test d'authenticité", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (mode == CapacityTestMode.Full)
            {
                var free = new DriveInfo(d.Root).AvailableFreeSpace;
                var answer = MessageBox.Show(this,
                    $"Le test va écrire puis relire {free / 1e9:0.#} Go de données de test dans l'ESPACE LIBRE de {d.Root.TrimEnd('\\')}.\n\n" +
                    "• Vos fichiers ne sont pas touchés ; les fichiers de test sont supprimés à la fin, même si vous l'arrêtez.\n" +
                    "• Durée : de quelques minutes à plusieurs heures (clé USB 2.0 : ≈ 1 h pour 64 Go).\n" +
                    "• Pour vérifier toute la capacité annoncée, testez la clé vide.\n\nLancer le test ?",
                    "Test d'authenticité", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer != DialogResult.Yes) return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            _details.Items.Clear();
            ShowVerdict(mode == CapacityTestMode.Full ? "Test d'authenticité en cours…" : "Test de vitesse en cours…", Theme.PrimaryDark);
            var progress = new Progress<CapacityProgress>(p =>
            {
                _progress.Value = (int)Math.Min(1000, p.Total > 0 ? p.Done * 1000 / p.Total : 0);
                double remaining = p.MBps > 0 ? (p.Total - p.Done) / 1e6 / p.MBps : 0;
                _status.Text = $"{p.Phase} : {p.Done / 1e9:0.00} / {p.Total / 1e9:0.00} Go  —  {p.MBps:0.0} Mo/s" +
                               (remaining > 0 ? $"  —  reste ≈ {TimeSpan.FromSeconds(remaining):hh\\:mm\\:ss}" : "") +
                               (p.BadBytes > 0 ? $"  —  {p.BadBytes / 1e6:0} Mo CORROMPUS" : "");
            });
            try
            {
                var token = _cts.Token;
                var r = await Task.Run(() => CapacityTest.Run(d.Root, mode, progress, token, LogFromWorker));
                _details.Items.Add(new ListViewItem(new[] { "Données écrites", $"{r.BytesWritten / 1e9:0.00} Go" }));
                _details.Items.Add(new ListViewItem(new[] { "Données vérifiées", $"{r.BytesVerified / 1e9:0.00} Go" }));
                _details.Items.Add(new ListViewItem(new[] { "Données corrompues", $"{r.BytesBad / 1e6:0} Mo" }) { ForeColor = r.BytesBad > 0 ? Theme.Danger : Theme.Success });
                _details.Items.Add(new ListViewItem(new[] { "Vitesse d'écriture réelle", $"{r.WriteMBps:0.0} Mo/s" }));
                _details.Items.Add(new ListViewItem(new[] { "Vitesse de lecture réelle", $"{r.ReadMBps:0.0} Mo/s" }));
                _details.Items.Add(new ListViewItem(new[] { "Conclusion", r.Summary() }));
                if (r.Cancelled) ShowVerdict("⏹  Test arrêté (résultat partiel ci-dessous)", Theme.Warning);
                else if (r.BytesBad > 0) ShowVerdict("🔴  FAUX OU DÉFECTUEUX : des données sont perdues", Theme.Danger);
                else if (mode == CapacityTestMode.Full) ShowVerdict("🟢  AUTHENTIQUE : toute la mémoire testée est fiable", Theme.Success);
                else ShowVerdict($"🟢  Aucune erreur — écriture {r.WriteMBps:0.0} Mo/s, lecture {r.ReadMBps:0.0} Mo/s", Theme.Success);
                _progress.Value = 1000;
            }
            catch (Exception ex)
            {
                ShowVerdict("Test impossible : " + ex.Message, Theme.Danger);
                Log("Erreur : " + ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void ShowVerdict(string text, Color color)
        {
            _verdict.Text = text;
            _verdict.ForeColor = color;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UseWaitCursor = busy && _cts == null;
            if (busy) _progress.Value = 0;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = Selected != null;
            _health.Enabled = has && !_busy;
            _speed.Enabled = has && !_busy && !(Selected?.IsSystem ?? true);
            _full.Enabled = has && !_busy && !(Selected?.IsSystem ?? true);
            _stop.Enabled = _busy && _cts != null;
            _drives.Enabled = _refresh.Enabled = !_busy;
        }

        private void LogFromWorker(string message)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => Log(message)));
            else Log(message);
        }

        private void Log(string message) => _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }
}
