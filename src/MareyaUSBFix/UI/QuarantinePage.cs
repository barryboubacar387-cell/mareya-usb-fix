using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>Page « Quarantaine » : éléments retirés du lecteur choisi, restaurables en un clic.</summary>
    public sealed class QuarantinePage : UserControl
    {
        private readonly ModernButton _restore = new ModernButton("Remettre à leur place", ButtonKind.Primary, Theme.Icons.Undo);
        private readonly ModernButton _open = new ModernButton("Ouvrir le dossier", ButtonKind.Secondary, Theme.Icons.Folder);
        private readonly ModernButton _reload = new ModernButton("Actualiser", ButtonKind.Secondary, Theme.Icons.Refresh);
        private readonly Banner _banner = new Banner { Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly CheckList _list = new CheckList { Dock = DockStyle.Fill, EmptyText = "La quarantaine de ce lecteur est vide." };
        private DriveEntry _drive;

        public QuarantinePage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;

            var actions = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 14), Margin = new Padding(0, 0, 0, 14) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White };
            flow.Controls.AddRange(new Control[] { _restore, _open, _reload });
            actions.Controls.Add(flow);

            var listCard = new Card { Dock = DockStyle.Fill, Title = "Éléments en quarantaine", Padding = new Padding(16, 46, 16, 14), Margin = new Padding(0) };
            listCard.Controls.Add(_list);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(_banner, 0, 1);
            layout.Controls.Add(new Panel { BackColor = Theme.Background }, 0, 2);
            layout.Controls.Add(listCard, 0, 3);
            Controls.Add(layout);

            _restore.Click += (s, e) => RestoreChecked();
            _reload.Click += (s, e) => Reload();
            _open.Click += (s, e) =>
            {
                if (_drive == null) return;
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + QuarantineStore.QuarantineRoot(_drive.Root) + "\""); } catch { }
            };
            _list.RowChecked += _ => UpdateButtons();
            UpdateButtons();
        }

        public void SetDrive(DriveEntry drive)
        {
            _drive = drive;
            Reload();
        }

        public void Reload()
        {
            var rows = new List<CheckRow>();
            if (_drive != null)
            {
                try
                {
                    rows.AddRange(QuarantineStore.List(_drive.Root).Select(e => new CheckRow
                    {
                        Tag = e,
                        Pill = e.IsDirectory ? "Dossier" : "Fichier",
                        PillFore = Theme.PrimaryDark, PillBack = Theme.PrimaryLight,
                        Title = e.OriginalRelative,
                        Subtitle = StampText(e.Stamp),
                    }));
                }
                catch (Exception ex) { _banner.Set(BannerKind.Danger, "Quarantaine illisible.", ex.Message); }
            }
            _list.SetRows(rows);
            if (_drive == null) _banner.Set(BannerKind.Info, "Choisissez un disque ci-dessus.", "");
            else if (rows.Count == 0) _banner.Set(BannerKind.Success, $"Rien en quarantaine sur {_drive.Letter}.", "Les éléments retirés lors d'une réparation apparaîtront ici.");
            else _banner.Set(BannerKind.Info, $"{rows.Count} élément(s) en quarantaine sur {_drive.Letter}.",
                "Cochez ceux qui vous appartiennent puis « Remettre à leur place ». Ne restaurez pas un élément du virus (raccourci piégé, script .vbs/.js).");
            UpdateButtons();
        }

        private void RestoreChecked()
        {
            var chosen = _list.Rows.Where(r => r.Checked).Select(r => r.Tag).OfType<QuarantineEntry>().ToList();
            if (chosen.Count == 0 || _drive == null) return;
            var errors = new List<string>();
            foreach (var entry in chosen)
            {
                try { QuarantineStore.Restore(_drive.Root, entry); }
                catch (Exception ex) { errors.Add($"{entry.OriginalRelative} : {ex.Message}"); }
            }
            Reload();
            if (errors.Count == 0) _banner.Set(BannerKind.Success, $"{chosen.Count} élément(s) remis à leur place.", "");
            else _banner.Set(BannerKind.Warning, $"{chosen.Count - errors.Count} élément(s) remis, {errors.Count} échec(s).", string.Join(" · ", errors));
        }

        private static string StampText(string stamp) =>
            DateTime.TryParseExact(stamp, "yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt)
                ? $"Retiré le {dt:dd/MM/yyyy} à {dt:HH:mm}"
                : "Retiré le " + stamp;

        private void UpdateButtons()
        {
            _restore.Enabled = _list.Rows.Any(r => r.Checked);
            _open.Enabled = _drive != null;
            _reload.Enabled = _drive != null;
        }

        internal void LoadDemo()
        {
            _drive = new DriveEntry { Root = @"E:\", Name = "KINGSTON", IsRemovable = true };
            _list.SetRows(new[]
            {
                new CheckRow { Pill = "Fichier", PillFore = Theme.PrimaryDark, PillBack = Theme.PrimaryLight, Title = "Documents.lnk", Subtitle = StampText("2026-11-20 11-42-05") },
                new CheckRow { Pill = "Fichier", PillFore = Theme.PrimaryDark, PillBack = Theme.PrimaryLight, Title = "x8f2k.vbe", Subtitle = StampText("2026-11-20 11-42-05") },
                new CheckRow { Pill = "Dossier", PillFore = Theme.PrimaryDark, PillBack = Theme.PrimaryLight, Title = "WindowsServices", Subtitle = StampText("2026-11-20 11-42-05"), Checked = true },
            });
            _banner.Set(BannerKind.Info, "3 élément(s) en quarantaine sur E:.",
                "Cochez ceux qui vous appartiennent puis « Remettre à leur place ». Ne restaurez pas un élément du virus.");
            UpdateButtons();
        }
    }
}
