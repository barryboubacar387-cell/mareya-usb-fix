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
    /// <summary>Page « Diagnostic » : santé du disque (SMART) et test d'authenticité (fausse capacité).</summary>
    public sealed class DiagnosticPanel : UserControl
    {
        private readonly ModernButton _health = new ModernButton("Vérifier la santé", ButtonKind.Primary, Theme.Icons.Health);
        private readonly ModernButton _speed = new ModernButton("Test de vitesse", ButtonKind.Secondary, Theme.Icons.Speed);
        private readonly ModernButton _full = new ModernButton("Test d'authenticité", ButtonKind.Primary, Theme.Icons.Shield);
        private readonly ModernButton _stop = new ModernButton("Arrêter", ButtonKind.Secondary, Theme.Icons.Stop);
        private readonly Banner _banner = new Banner { Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly SlimProgress _progress = new SlimProgress { Dock = DockStyle.Top, Visible = false };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Background };
        private readonly FlowLayoutPanel _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, WrapContents = true };
        private readonly Label _reasons = new Label { Dock = DockStyle.Top, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Warning, BackColor = Color.White, Padding = new Padding(2, 4, 0, 8) };
        private readonly ModernButton _toggleLog = new ModernButton("Journal détaillé", ButtonKind.Ghost, Theme.Icons.Info) { Height = 30 };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Mono, BackColor = Theme.Surface, BorderStyle = BorderStyle.None };
        private readonly Panel _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 130, Visible = false, Padding = new Padding(0, 10, 0, 0), BackColor = Color.White };
        private readonly HealthGauge _gauge = new HealthGauge { Location = new Point(0, 4) };
        private readonly Label _gaugeTitle = new Label { AutoSize = true, Font = Theme.Heading, ForeColor = Theme.Text, BackColor = Color.White, Location = new Point(180, 14) };
        private readonly Label _gaugeText = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.White, Location = new Point(181, 46), MaximumSize = new Size(560, 0) };
        private readonly Panel _gaugePanel = new Panel { Dock = DockStyle.Top, Height = 176, BackColor = Color.White, Visible = false };
        private readonly Panel _scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        private CancellationTokenSource _cts;
        private DriveEntry _drive;

        public bool Busy { get; private set; }
        public event Action BusyChanged;

        public DiagnosticPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;

            var actions = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 14), Margin = new Padding(0, 0, 0, 14) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Color.White };
            flow.Controls.AddRange(new Control[] { _health, _speed, _full, _stop });
            actions.Controls.Add(flow);

            var details = new Card { Dock = DockStyle.Fill, Title = "Détails", Padding = new Padding(16, 46, 16, 14), Margin = new Padding(0) };
            var scroller = _scroller;
            scroller.Controls.Add(_reasons);
            scroller.Controls.Add(_tiles);
            _gaugePanel.Controls.AddRange(new Control[] { _gauge, _gaugeTitle, _gaugeText });
            scroller.Controls.Add(_gaugePanel);
            scroller.Resize += (s, e) => _gaugeText.MaximumSize = new Size(Math.Max(240, scroller.ClientSize.Width - 200), 0);
            details.Controls.Add(scroller);
            _logPanel.Controls.Add(_log);
            details.Controls.Add(_logPanel);
            details.Controls.Add(_toggleLog);
            details.Resize += (s, e) => _toggleLog.Location = new Point(details.Width - _toggleLog.Width - 14, 10);
            _toggleLog.BringToFront();

            var statusHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(4, 8, 4, 0) };
            statusHost.Controls.Add(_status);
            statusHost.Controls.Add(_progress);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(_banner, 0, 1);
            layout.Controls.Add(statusHost, 0, 2);
            layout.Controls.Add(details, 0, 3);
            Controls.Add(layout);

            _health.Click += async (s, e) => await CheckHealthAsync();
            _speed.Click += async (s, e) => await CapacityAsync(CapacityTestMode.Speed);
            _full.Click += async (s, e) => await CapacityAsync(CapacityTestMode.Full);
            _stop.Click += (s, e) => _cts?.Cancel();
            _toggleLog.Click += (s, e) => { _logPanel.Visible = !_logPanel.Visible; _toggleLog.Text = _logPanel.Visible ? "Masquer le journal" : "Journal détaillé"; };

            ResetView();
            UpdateButtons();
        }

        public void SetDrive(DriveEntry drive)
        {
            bool changed = !string.Equals(drive?.Root, _drive?.Root, StringComparison.OrdinalIgnoreCase);
            _drive = drive;
            if (changed) { ResetView(); _log.Clear(); }
            UpdateButtons();
        }

        private void ResetView()
        {
            _tiles.Controls.Clear();
            _reasons.Text = "";
            _gaugePanel.Visible = false;
            _progress.Visible = false;
            _status.Text = "« Vérifier la santé » est instantané et ne modifie rien. Le test d'authenticité détecte les fausses clés et faux disques.";
            if (_drive == null)
                _banner.Set(BannerKind.Info, "Choisissez un disque ci-dessus.", "");
            else
                _banner.Set(BannerKind.Info, $"Disque {_drive.Letter} « {_drive.Name} » sélectionné.",
                    _drive.IsSystem ? "Disque de Windows : la santé peut être vérifiée ; les tests d'écriture ne sont pas proposés."
                                    : "Lancez un diagnostic : santé (instantané), vitesse (quelques secondes) ou authenticité (complet).");
        }

        private async Task CheckHealthAsync()
        {
            var d = _drive;
            if (d == null || Busy) return;
            SetBusy(true);
            _tiles.Controls.Clear();
            _reasons.Text = "";
            _gaugePanel.Visible = false;
            _banner.Set(BannerKind.Info, "Lecture des informations du disque…", "");
            try
            {
                var report = await Task.Run(() => DiskHealth.Check(d.Root, LogFromWorker));
                ShowHealth(report);
                Log($"Santé de {d.Root} : {report.VerdictText}");
            }
            catch (Exception ex)
            {
                _banner.Set(BannerKind.Danger, "Diagnostic impossible.", ex.Message);
                Log("Erreur : " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private void ShowHealth(DiskHealthReport report)
        {
            _tiles.SuspendLayout();
            _tiles.Controls.Clear();
            foreach (var kv in report.Details)
            {
                string value = kv.Value, hint = "";
                int paren = value.IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0 && value.EndsWith(")")) { hint = value.Substring(paren + 2, value.Length - paren - 3); value = value.Substring(0, paren); }
                var tile = new StatTile(kv.Key);
                Color? color = null;
                if ((kv.Key.StartsWith("Secteurs") && value != "0")) color = Theme.Danger;
                if (kv.Key == "État selon Windows" && value == "Sain") color = Theme.Success;
                tile.Set(value, hint, color);
                _tiles.Controls.Add(tile);
            }
            _tiles.ResumeLayout();
            _reasons.Text = string.Join(Environment.NewLine, report.Reasons.Select(r => "•  " + r));

            // Jauge de santé en %
            _gauge.Set(report.Percent, report.Percent.HasValue ? HealthScore.Label(report.Percent.Value) : "inconnue");
            _gaugeTitle.Text = report.Percent.HasValue ? $"Santé du disque : {report.Percent} %" : "Santé du disque : inconnue";
            _gaugeTitle.ForeColor = HealthGauge.ColorFor(report.Percent);
            _gaugeText.Text = !report.Percent.HasValue
                ? "Ce disque ne transmet pas assez de mesures pour calculer un pourcentage (fréquent pour les clés USB). " +
                  "Le test d'authenticité vérifie, lui, la mémoire elle-même."
                : report.PercentBreakdown.Count == 0
                    ? "Aucune retenue : toutes les mesures disponibles sont bonnes.\n\nLe pourcentage tient compte de l'usure (SSD), " +
                      "des secteurs défectueux, des erreurs et de la température."
                    : "Comment ce pourcentage est calculé :\n" + string.Join("\n", report.PercentBreakdown.Select(b => "•  " + b));
            _gaugePanel.Visible = true;
            ScrollToTop();
            _reasons.ForeColor = report.Verdict == HealthVerdict.Critical ? Theme.Danger : report.Verdict == HealthVerdict.Unknown ? Theme.Muted : Theme.Warning;

            switch (report.Verdict)
            {
                case HealthVerdict.Good: _banner.Set(BannerKind.Success, "Santé : bon état" + (report.Percent.HasValue ? $" ({report.Percent} %)" : ""), "Aucun signe de panne n'a été détecté."); break;
                case HealthVerdict.Warning: _banner.Set(BannerKind.Warning, "Santé : à surveiller" + (report.Percent.HasValue ? $" ({report.Percent} %)" : ""), "Faites des sauvegardes régulières. Détails ci-dessous."); break;
                case HealthVerdict.Critical: _banner.Set(BannerKind.Danger, "Risque de panne : sauvegardez vos données au plus vite" + (report.Percent.HasValue ? $" ({report.Percent} %)" : ""), "Détails ci-dessous."); break;
                default: _banner.Set(BannerKind.Neutral, "État inconnu", "Ce disque ne transmet pas ses informations de santé (fréquent pour les clés USB)."); break;
            }
        }

        private async Task CapacityAsync(CapacityTestMode mode)
        {
            var d = _drive;
            if (d == null || d.IsSystem || Busy) return;
            if (mode == CapacityTestMode.Full)
            {
                long free = new DriveInfo(d.Root).AvailableFreeSpace;
                if (MessageBox.Show(this,
                        $"Le test va écrire puis relire {free / 1e9:0.#} Go de données de test dans l'ESPACE LIBRE de {d.Letter}.\n\n" +
                        "• Vos fichiers ne sont pas touchés ; les fichiers de test sont supprimés à la fin, même si vous l'arrêtez.\n" +
                        "• Durée : de quelques minutes à plusieurs heures (clé USB 2.0 : ≈ 1 h pour 64 Go).\n" +
                        "• Pour vérifier toute la capacité annoncée, testez la clé vide.\n\nLancer le test ?",
                        "Test d'authenticité", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            _tiles.Controls.Clear();
            _reasons.Text = "";
            _progress.Visible = true;
            _progress.Value = 0;
            _banner.Set(BannerKind.Info, mode == CapacityTestMode.Full ? "Test d'authenticité en cours…" : "Test de vitesse en cours…",
                "Vous pouvez l'arrêter à tout moment : les fichiers de test seront supprimés.");
            var progress = new Progress<CapacityProgress>(p =>
            {
                _progress.Value = (int)Math.Min(1000, p.Total > 0 ? p.Done * 1000 / p.Total : 0);
                double remaining = p.MBps > 0 ? (p.Total - p.Done) / 1e6 / p.MBps : 0;
                _status.Text = $"{p.Phase} : {p.Done / 1e9:0.00} / {p.Total / 1e9:0.00} Go  ·  {p.MBps:0.0} Mo/s" +
                               (remaining > 0 ? $"  ·  reste ≈ {TimeSpan.FromSeconds(remaining):hh\\:mm\\:ss}" : "") +
                               (p.BadBytes > 0 ? $"  ·  {p.BadBytes / 1e6:0} Mo CORROMPUS" : "");
            });
            try
            {
                var token = _cts.Token;
                var r = await Task.Run(() => CapacityTest.Run(d.Root, mode, progress, token, LogFromWorker));
                ShowCapacity(r, mode);
            }
            catch (Exception ex)
            {
                _banner.Set(BannerKind.Danger, "Test impossible.", ex.Message);
                Log("Erreur : " + ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void ShowCapacity(CapacityTestResult r, CapacityTestMode mode)
        {
            _gaugePanel.Visible = false;
            _tiles.Controls.Clear();
            void Tile(string label, string value, string hint = "", Color? c = null)
            {
                var t = new StatTile(label);
                t.Set(value, hint, c);
                _tiles.Controls.Add(t);
            }
            Tile("Données vérifiées", $"{r.BytesVerified / 1e9:0.00} Go", $"sur {r.BytesWritten / 1e9:0.00} Go écrits");
            Tile("Données corrompues", $"{r.BytesBad / 1e6:0} Mo", r.BytesBad > 0 ? "mémoire non fiable" : "aucune erreur", r.BytesBad > 0 ? Theme.Danger : Theme.Success);
            Tile("Écriture réelle", $"{r.WriteMBps:0.0} Mo/s", SpeedHint(r.WriteMBps));
            Tile("Lecture réelle", $"{r.ReadMBps:0.0} Mo/s", SpeedHint(r.ReadMBps));
            _reasons.ForeColor = r.BytesBad > 0 ? Theme.Danger : Theme.Muted;
            _reasons.Text = r.Summary();
            _progress.Value = 1000;
            _status.Text = "";
            ScrollToTop();

            if (r.Cancelled) _banner.Set(BannerKind.Warning, "Test arrêté", "Résultat partiel ci-dessous. Les fichiers de test ont été supprimés.");
            else if (r.BytesBad > 0) _banner.Set(BannerKind.Danger, "FAUX OU DÉFECTUEUX : des données sont perdues", "N'y stockez rien d'important. Détails ci-dessous.");
            else if (mode == CapacityTestMode.Full) _banner.Set(BannerKind.Success, "AUTHENTIQUE : toute la mémoire testée est fiable", "Chaque octet écrit a été relu sans erreur.");
            else _banner.Set(BannerKind.Success, "Aucune erreur sur l'échantillon testé", $"Écriture {r.WriteMBps:0.0} Mo/s · lecture {r.ReadMBps:0.0} Mo/s. Lancez le test d'authenticité pour prouver la capacité.");
        }

        /// <summary>Revenir en haut des détails (sinon la zone peut rester défilée et couper la jauge).</summary>
        private void ScrollToTop()
        {
            _scroller.AutoScrollPosition = Point.Empty;
            if (IsHandleCreated) BeginInvoke(new Action(() => _scroller.AutoScrollPosition = Point.Empty));
        }

        private static string SpeedHint(double mbps) =>
            mbps < 15 ? "lent (USB 2.0 ou clé bas de gamme)" : mbps < 60 ? "correct" : mbps < 200 ? "rapide (USB 3)" : "très rapide";

        private void SetBusy(bool busy)
        {
            Busy = busy;
            UseWaitCursor = busy && _cts == null;
            UpdateButtons();
            BusyChanged?.Invoke();
        }

        private void UpdateButtons()
        {
            bool has = _drive != null;
            bool writable = has && !_drive.IsSystem;
            _health.Enabled = has && !Busy;
            _speed.Enabled = writable && !Busy;
            _full.Enabled = writable && !Busy;
            _stop.Enabled = Busy && _cts != null;
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
            _drive = new DriveEntry { Root = @"C:\", Name = "Windows", IsSystem = true };
            var report = new DiskHealthReport { Verdict = HealthVerdict.Good };
            report.Inputs.Temperature = 40; report.Inputs.Reallocated = 0; report.Inputs.Wear = 3;
            var (pct, notes) = HealthScore.Compute(report.Inputs);
            report.Percent = pct; report.PercentBreakdown = notes;
            report.Add("État selon Windows", "Sain");
            report.Add("Température", "40 °C");
            report.Add("Heures d'utilisation", DiskHealth.FormatHours(182));
            report.Add("Secteurs réalloués", "0");
            report.Add("Usure (SSD)", "3 %");
            report.Add("Modèle", "SSD256GB");
            report.Add("Capacité annoncée", "256,1 Go (238,5 Gio)");
            report.Add("Connexion", "SATA");
            report.Add("Type", "SSD");
            ShowHealth(report);
            UpdateButtons();
        }
    }
}
