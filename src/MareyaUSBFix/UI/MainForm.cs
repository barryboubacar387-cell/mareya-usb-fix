using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>
    /// Fenêtre principale : menu latéral (Réparation, Diagnostic, Quarantaine, À propos),
    /// choix du lecteur en tuiles commun à toutes les pages, et la page active.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly RepairPage _repair = new RepairPage();
        private readonly DiagnosticPanel _diagnostic = new DiagnosticPanel();
        private readonly QuarantinePage _quarantine = new QuarantinePage();
        private readonly AboutPage _about = new AboutPage();
        private readonly List<NavItem> _nav = new List<NavItem>();
        private readonly Panel _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly Label _pageTitle = new Label { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Location = new Point(0, 0), BackColor = Theme.Background };
        private readonly Label _pageSubtitle = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Location = new Point(2, 40), BackColor = Theme.Background };
        private readonly Panel _driveStrip = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Theme.Background, Padding = new Padding(0, 4, 0, 16) };
        private readonly FlowLayoutPanel _tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = Theme.Background };
        private readonly ModernButton _refresh = new ModernButton("Actualiser", ButtonKind.Ghost, Theme.Icons.Refresh);
        private readonly ToggleSwitch _autoProtect = new ToggleSwitch("Protection automatique") { TextColor = Color.White, Font = Theme.Bold };
        private readonly bool _demo;
        private DriveEntry _selected;
        private DeviceWatcher _watcher;
        private int _page = -1;

        private static readonly (string title, string subtitle, string icon)[] Pages =
        {
            ("Réparation", "Supprime le virus « raccourci » et récupère vos fichiers cachés.", Theme.Icons.Shield),
            ("Diagnostic", "Santé du disque et test d'authenticité (fausses clés, vitesse réelle).", Theme.Icons.Health),
            ("Quarantaine", "Ce que le logiciel a retiré : tout peut être remis à sa place.", Theme.Icons.Box),
            ("À propos", "Mareya USB Fix, logiciel gratuit de Mareya Group.", Theme.Icons.Info),
        };

        public MainForm() : this(demo: false) { }

        internal MainForm(bool demo)
        {
            _demo = demo;
            Text = "Mareya USB Fix";
            Icon = Theme.AppIcon;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 700);
            Size = new Size(1200, 800);
            BackColor = Theme.Background;

            BuildLayout();
            _repair.BusyChanged += OnBusyChanged;
            _diagnostic.BusyChanged += OnBusyChanged;

            if (demo) LoadDemo();
            else
            {
                LoadDrives();
                _autoProtect.SetSilently(SafeIsAutoEnabled());
                _autoProtect.CheckedChanged += (s, e) => ToggleAutoProtection();
                Load += (s, e) =>
                {
                    _watcher = new DeviceWatcher(this);
                    _watcher.DrivesChanged += () => BeginInvoke(new Action(LoadDrives));
                    _watcher.DriveArrived += root =>
                    {
                        LoadDrives();
                        SelectDrive(root);
                        if (_page == 0 && !_repair.Busy) _ = _repair.ScanAsync();
                    };
                };
                FormClosed += (s, e) => _watcher?.Dispose();
            }
            ShowPage(0);
        }

        // ---------- Mise en page ----------

        private void BuildLayout()
        {
            // Menu latéral
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Navy };
            var brand = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Theme.Navy };
            var logo = new PictureBox { Size = new Size(40, 40), Location = new Point(22, 30), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Navy };
            try { logo.Image = new Icon(Theme.AppIcon, 64, 64).ToBitmap(); } catch { }
            brand.Controls.Add(logo);
            brand.Controls.Add(new Label { Text = "Mareya", Font = Theme.Heading, ForeColor = Color.White, AutoSize = true, Location = new Point(72, 28), BackColor = Theme.Navy });
            brand.Controls.Add(new Label { Text = "USB Fix  ·  v" + Theme.Version, Font = Theme.Small, ForeColor = Color.FromArgb(0x9F, 0xB3, 0xD6), AutoSize = true, Location = new Point(73, 52), BackColor = Theme.Navy });

            var navFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Navy, Padding = new Padding(0, 6, 0, 0) };
            for (int i = 0; i < Pages.Length; i++)
            {
                int index = i;
                var item = new NavItem(Pages[i].title, Pages[i].icon) { Dock = DockStyle.None, Width = 236, Margin = new Padding(0, 0, 0, 4) };
                item.Click += (s, e) => ShowPage(index);
                _nav.Add(item);
                navFlow.Controls.Add(item);
            }

            var protect = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Navy, Padding = new Padding(20, 0, 16, 20) };
            _autoProtect.Location = new Point(18, 16);
            _autoProtect.Width = 216;
            protect.Controls.Add(_autoProtect);
            protect.Controls.Add(new Label
            {
                Text = "Nettoie automatiquement chaque clé dès qu'elle est branchée.",
                Font = Theme.Small, ForeColor = Color.FromArgb(0x9F, 0xB3, 0xD6), BackColor = Theme.Navy,
                Location = new Point(20, 50), Size = new Size(204, 40)
            });

            sidebar.Controls.Add(navFlow);
            sidebar.Controls.Add(protect);
            sidebar.Controls.Add(brand);

            // Contenu
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(30, 24, 30, 24) };
            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Background };
            header.Controls.Add(_pageTitle);
            header.Controls.Add(_pageSubtitle);

            // « Actualiser » en haut à droite de l'en-tête : toute la largeur reste aux tuiles
            _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _refresh.Click += (s, e) => LoadDrives();
            header.Controls.Add(_refresh);
            header.Resize += (s, e) => _refresh.Location = new Point(header.Width - _refresh.Width, 8);
            _driveStrip.Controls.Add(_tiles);
            _driveStrip.Resize += (s, e) => LayoutTiles();

            content.Controls.Add(_host);
            content.Controls.Add(_driveStrip);
            content.Controls.Add(header);

            Controls.Add(content);
            Controls.Add(sidebar);
        }

        public void ShowPage(int index)
        {
            if (index == _page) return;
            _page = index;
            for (int i = 0; i < _nav.Count; i++) _nav[i].Selected = i == index;
            _pageTitle.Text = Pages[index].title;
            _pageSubtitle.Text = Pages[index].subtitle;
            _driveStrip.Visible = index != 3;

            Control page = index == 0 ? _repair : index == 1 ? (Control)_diagnostic : index == 2 ? _quarantine : _about;
            _host.SuspendLayout();
            _host.Controls.Clear();
            _host.Controls.Add(page);
            _host.ResumeLayout();
            if (index == 2 && !_demo) _quarantine.Reload();
        }

        // ---------- Lecteurs (tuiles) ----------

        private void LoadDrives()
        {
            var drives = new List<DriveEntry>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed)) continue;
                    bool sys = DriveScanner.IsSystemDrive(d.RootDirectory.FullName);
                    drives.Add(new DriveEntry
                    {
                        Root = d.RootDirectory.FullName,
                        Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (sys ? "Windows" : "Sans nom") : d.VolumeLabel,
                        Kind = sys ? "Disque de Windows" : d.DriveType == DriveType.Removable ? "Clé USB / carte mémoire" : "Disque dur",
                        Total = d.TotalSize, Free = d.AvailableFreeSpace,
                        IsRemovable = d.DriveType == DriveType.Removable, IsSystem = sys
                    });
                }
                catch { /* lecteur indisponible */ }
            }
            // clés USB d'abord, puis disques, le disque de Windows en dernier
            ShowTiles(drives.OrderBy(d => d.IsSystem).ThenBy(d => !d.IsRemovable).ThenBy(d => d.Root).ToList());
        }

        private void ShowTiles(List<DriveEntry> drives)
        {
            string previous = _selected?.Root;
            _tiles.SuspendLayout();
            _tiles.Controls.Clear();
            foreach (var d in drives)
            {
                var tile = new DriveTile(d);
                tile.Click += (s, e) => { if (!AnyBusy) SelectDrive(d.Root); };
                _tiles.Controls.Add(tile);
            }
            if (drives.Count == 0)
                _tiles.Controls.Add(new Label { Text = "Aucun disque détecté : branchez une clé USB.", AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Margin = new Padding(4, 30, 0, 0) });
            _tiles.ResumeLayout();
            LayoutTiles();

            var keep = drives.FirstOrDefault(d => string.Equals(d.Root, previous, StringComparison.OrdinalIgnoreCase))
                       ?? drives.FirstOrDefault(d => d.IsRemovable) ?? drives.FirstOrDefault(d => !d.IsSystem) ?? drives.FirstOrDefault();
            SelectDrive(keep?.Root, force: true);
        }

        private void SelectDrive(string root, bool force = false)
        {
            var tiles = _tiles.Controls.OfType<DriveTile>().ToList();
            var tile = tiles.FirstOrDefault(t => string.Equals(t.Drive.Root, root, StringComparison.OrdinalIgnoreCase));
            foreach (var t in tiles) t.Selected = t == tile;
            var entry = tile?.Drive;
            if (!force && entry != null && _selected != null && string.Equals(entry.Root, _selected.Root, StringComparison.OrdinalIgnoreCase)) return;
            _selected = entry;
            _repair.SetDrive(entry);
            _diagnostic.SetDrive(entry);
            if (_page == 2 || force) _quarantine.SetDrive(entry);
        }

        /// <summary>Les tuiles se partagent la largeur disponible ; défilement seulement s'il y a beaucoup de disques.</summary>
        private void LayoutTiles()
        {
            var tiles = _tiles.Controls.OfType<DriveTile>().ToList();
            if (tiles.Count == 0) { _driveStrip.Height = 108; return; }
            int available = _driveStrip.ClientSize.Width - 4;
            int width = Math.Max(176, Math.Min(300, (available - 12 * tiles.Count) / tiles.Count));
            bool overflow = (width + 12) * tiles.Count > available;
            foreach (var t in tiles) t.Width = width;
            _driveStrip.Height = overflow ? 126 : 108;
        }

        private bool AnyBusy => _repair.Busy || _diagnostic.Busy;

        private void OnBusyChanged()
        {
            bool busy = AnyBusy;
            _refresh.Enabled = !busy;
            foreach (var t in _tiles.Controls.OfType<DriveTile>()) t.Enabled = !busy || t.Selected;
        }

        // ---------- Protection automatique ----------

        private void ToggleAutoProtection()
        {
            try
            {
                if (_autoProtect.Checked)
                {
                    AutoProtection.Enable();
                    Process.Start(new ProcessStartInfo(AutoProtection.InstalledExe, AutoProtection.TrayArgument) { UseShellExecute = true });
                    MessageBox.Show(this, "Protection automatique activée : Mareya USB Fix démarrera avec Windows et nettoiera chaque clé branchée.",
                        "Protection automatique", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AutoProtection.Disable();
                }
            }
            catch (Exception ex)
            {
                _autoProtect.SetSilently(SafeIsAutoEnabled());
                MessageBox.Show(this, ex.Message, "Protection automatique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool SafeIsAutoEnabled()
        {
            try { return AutoProtection.IsEnabled; } catch { return false; }
        }

        // ---------- Démonstration (captures d'écran) ----------

        private void LoadDemo()
        {
            ShowTiles(new List<DriveEntry>
            {
                new DriveEntry { Root = @"E:\", Name = "KINGSTON", Kind = "Clé USB / carte mémoire", Total = 15_500_000_000, Free = 9_100_000_000, IsRemovable = true },
                new DriveEntry { Root = @"F:\", Name = "MES FICHIERS", Kind = "Disque dur", Total = 254_000_000_000, Free = 61_000_000_000 },
                new DriveEntry { Root = @"C:\", Name = "Windows", Kind = "Disque de Windows", Total = 255_000_000_000, Free = 169_600_000_000, IsSystem = true },
            });
            _autoProtect.SetSilently(true);
            _repair.LoadDemo();
            _diagnostic.LoadDemo();
            _quarantine.LoadDemo();
        }

        /// <summary>Capture d'écran de la fenêtre (mode démonstration, pour la documentation et les tests visuels).</summary>
        internal static void CaptureScreens(string folder)
        {
            Directory.CreateDirectory(folder);
            using (var form = new MainForm(demo: true) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), ShowInTaskbar = false })
            {
                form.Show();
                string[] names = { "1-reparation", "2-diagnostic", "3-quarantaine", "4-a-propos" };
                for (int i = 0; i < names.Length; i++)
                {
                    form.ShowPage(i);
                    if (i == 1) form.SelectDrive(@"C:\", force: false);
                    for (int k = 0; k < 10; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                    if (i == 1) form._diagnostic.LoadDemo();
                    for (int k = 0; k < 10; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(Path.Combine(folder, names[i] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                form.Close();
            }
        }
    }
}
