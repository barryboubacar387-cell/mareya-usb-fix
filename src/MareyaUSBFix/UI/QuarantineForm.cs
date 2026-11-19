using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MareyaUSBFix.Core;

namespace MareyaUSBFix.UI
{
    /// <summary>Liste la quarantaine d'un lecteur et permet de remettre des éléments à leur place.</summary>
    public sealed class QuarantineForm : Form
    {
        private readonly string _root;
        private readonly ListView _list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
            Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle
        };
        private readonly Label _info = new Label { Dock = DockStyle.Top, Height = 54, Font = Theme.Body, Padding = new Padding(0, 0, 0, 8) };

        public QuarantineForm(string root)
        {
            _root = root;
            Text = $"Quarantaine — {root.TrimEnd('\\')}";
            Icon = Theme.AppIcon;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 520);
            MinimumSize = new Size(600, 360);
            BackColor = Color.White;
            Padding = new Padding(16);

            _info.Text = "Éléments retirés par Mareya USB Fix. Cochez ceux qui vous appartiennent puis cliquez sur " +
                         "« Remettre à leur place ». Attention : ne restaurez pas un élément du virus (raccourci piégé, script .vbs/.js…).";

            _list.Columns.Add("Date", 150);
            _list.Columns.Add("Emplacement d'origine", 520);
            _list.Columns.Add("Type", 100);

            var restore = new Button
            {
                Text = "Remettre à leur place", AutoSize = true, Padding = new Padding(14, 6, 14, 6), FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Primary, ForeColor = Color.White, Font = Theme.Bold
            };
            var openFolder = new Button { Text = "Ouvrir le dossier", AutoSize = true, Padding = new Padding(14, 6, 14, 6), FlatStyle = FlatStyle.Flat, Font = Theme.Bold };
            var close = new Button { Text = "Fermer", AutoSize = true, Padding = new Padding(14, 6, 14, 6), FlatStyle = FlatStyle.Flat, Font = Theme.Bold };
            restore.Click += (s, e) => RestoreChecked();
            openFolder.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + QuarantineStore.QuarantineRoot(_root) + "\""); }
                catch { /* simple confort */ }
            };
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            buttons.Controls.AddRange(new Control[] { close, openFolder, restore });

            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(_info);
            LoadEntries();
        }

        private void LoadEntries()
        {
            _list.Items.Clear();
            List<QuarantineEntry> entries;
            try { entries = QuarantineStore.List(_root); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Quarantaine", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            foreach (var e in entries)
            {
                _list.Items.Add(new ListViewItem(new[] { e.Stamp, e.OriginalRelative, e.IsDirectory ? "Dossier" : "Fichier" }) { Tag = e });
            }
            if (entries.Count == 0)
                _list.Items.Add(new ListViewItem(new[] { "", "La quarantaine de ce lecteur est vide.", "" }));
        }

        private void RestoreChecked()
        {
            var chosen = _list.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<QuarantineEntry>().ToList();
            if (chosen.Count == 0) return;
            var errors = new List<string>();
            foreach (var entry in chosen)
            {
                try { QuarantineStore.Restore(_root, entry); }
                catch (Exception ex) { errors.Add($"{entry.OriginalRelative} : {ex.Message}"); }
            }
            LoadEntries();
            MessageBox.Show(this,
                errors.Count == 0
                    ? $"{chosen.Count} élément(s) remis à leur place."
                    : $"{chosen.Count - errors.Count} élément(s) remis à leur place.\n\nÉchecs :\n" + string.Join("\n", errors),
                "Quarantaine", MessageBoxButtons.OK, errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
