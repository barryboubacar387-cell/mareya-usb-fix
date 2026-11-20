using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MareyaUSBFix.UI
{
    /// <summary>Une ligne de liste : case à cocher, étiquette colorée, titre, sous-titre, mention à droite.</summary>
    public sealed class CheckRow
    {
        public object Tag;
        public bool Checked;
        public bool Checkable = true;
        public string Pill;
        public Color PillFore = Theme.Muted, PillBack = Theme.Surface;
        public string Title;
        public string Subtitle;
        public string Right;
        public Color TitleColor = Theme.Text;
    }

    /// <summary>
    /// Liste aérée dessinée sur mesure (remplace la ListView) : lignes de deux niveaux, survol,
    /// case à cocher arrondie. Défilement vertical automatique.
    /// </summary>
    public class CheckList : Control
    {
        private readonly List<CheckRow> _rows = new List<CheckRow>();
        private readonly VScrollBar _scroll = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        private int _hover = -1;
        private const int RowHeight = 58;

        public event Action<CheckRow> RowChecked;
        public string EmptyText { get; set; } = "";

        public CheckList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
            Controls.Add(_scroll);
            _scroll.ValueChanged += (s, e) => Invalidate();
        }

        public IReadOnlyList<CheckRow> Rows => _rows;

        public void SetRows(IEnumerable<CheckRow> rows)
        {
            _rows.Clear();
            _rows.AddRange(rows);
            _hover = -1;
            _scroll.Value = 0;
            UpdateScroll();
            Invalidate();
        }

        public void Clear() => SetRows(Enumerable.Empty<CheckRow>());

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

        private void UpdateScroll()
        {
            int content = _rows.Count * RowHeight;
            _scroll.Visible = content > Height;
            _scroll.Maximum = Math.Max(0, content);
            _scroll.LargeChange = Math.Max(1, Height);
            _scroll.SmallChange = RowHeight;
        }

        private int Offset => _scroll.Visible ? _scroll.Value : 0;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!_scroll.Visible) return;
            int max = Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);
            _scroll.Value = Math.Max(0, Math.Min(max, _scroll.Value - e.Delta / 120 * RowHeight));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = (e.Y + Offset) / RowHeight;
            int h = i >= 0 && i < _rows.Count ? i : -1;
            if (h != _hover) { _hover = h; Cursor = h >= 0 && _rows[h].Checkable ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = (e.Y + Offset) / RowHeight;
            if (i < 0 || i >= _rows.Count || !_rows[i].Checkable) return;
            _rows[i].Checked = !_rows[i].Checked;
            Invalidate();
            RowChecked?.Invoke(_rows[i]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int width = Width - (_scroll.Visible ? _scroll.Width : 0);

            if (_rows.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Theme.Body, new Rectangle(0, 0, width, Math.Min(Height, 120)), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                int y = i * RowHeight - Offset;
                if (y + RowHeight < 0) continue;
                if (y > Height) break;
                var row = _rows[i];

                if (i == _hover)
                    using (var path = Theme.Rounded(new RectangleF(2, y + 2, width - 4, RowHeight - 4), 8))
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);

                int x = 12;
                if (row.Checkable)
                {
                    var box = new RectangleF(x, y + (RowHeight - 20) / 2f, 20, 20);
                    using (var path = Theme.Rounded(box, 5))
                    {
                        if (row.Checked) using (var b = new SolidBrush(Theme.Primary)) g.FillPath(b, path);
                        else using (var p = new Pen(Color.FromArgb(0x9A, 0xA4, 0xB4), 1.4f)) g.DrawPath(p, path);
                    }
                    if (row.Checked)
                        using (var f = Theme.IconFont(9f))
                            TextRenderer.DrawText(g, Theme.Icons.Check, f, Rectangle.Round(box), Color.White,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += 34;
                }

                if (!string.IsNullOrEmpty(row.Pill))
                {
                    Pill.Draw(g, row.Pill, new Rectangle(x, y, 120, RowHeight), row.PillFore, row.PillBack);
                    x += 118;
                }

                int rightW = string.IsNullOrEmpty(row.Right) ? 0 : 150;
                int textW = width - x - rightW - 16;
                TextRenderer.DrawText(g, row.Title, Theme.Bold, new Rectangle(x, y + 9, textW, 20), row.TitleColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, row.Subtitle, Theme.Small, new Rectangle(x, y + 30, textW, 18), Theme.Muted,
                    TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
                if (rightW > 0)
                    TextRenderer.DrawText(g, row.Right, Theme.Small, new Rectangle(width - rightW - 12, y, rightW, RowHeight), Theme.Muted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (i < _rows.Count - 1)
                    using (var p = new Pen(Theme.Border)) g.DrawLine(p, 12, y + RowHeight - 1, width - 12, y + RowHeight - 1);
            }
        }
    }

    /// <summary>Petite carte de statistique : intitulé gris et valeur en grand.</summary>
    public class StatTile : Control
    {
        private string _value = "", _hint = "";
        private Color _valueColor = Theme.Text;

        public StatTile(string label)
        {
            Text = label;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(196, 84);
            Margin = new Padding(0, 0, 12, 12);
        }

        public void Set(string value, string hint = "", Color? color = null)
        {
            _value = value ?? "";
            _hint = hint ?? "";
            _valueColor = color ?? Theme.Text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Color.White)) g.FillRectangle(bg, ClientRectangle);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Theme.Small, new Rectangle(14, 10, Width - 24, 18), Theme.Muted, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _value, Theme.Big, new Rectangle(12, 28, Width - 22, 30), _valueColor, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _hint, Theme.Small, new Rectangle(14, 60, Width - 24, 18), Theme.Muted, TextFormatFlags.EndEllipsis);
        }
    }
}
